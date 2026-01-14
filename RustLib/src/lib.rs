use std::collections::{HashMap, HashSet, VecDeque};
use std::ffi::CStr;
use std::io::{Read, Write};
use std::num::NonZeroUsize;
use std::os::raw::{c_char, c_uchar};
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Mutex, OnceLock, RwLock};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use lru::LruCache;
use once_cell::sync::Lazy;

// Define the Value enum to support multiple data structures
#[derive(Clone)]
enum Value {
    Bytes(Vec<u8>),
    Hash(HashMap<String, Vec<u8>>),
    List(Vec<Vec<u8>>),
    Set(HashSet<Vec<u8>>),
    SortedSet(HashMap<String, f64>), // Member -> Score
    Stream(StreamData),
}

#[derive(Clone)]
struct StreamEntry {
    id: u64,
    payload: Vec<u8>,
}

#[derive(Clone)]
struct StreamData {
    entries: Vec<StreamEntry>,
}

#[derive(Clone)]
struct Entry {
    value: Value,
    expires_at_ms: Option<u64>,
}

struct CacheState {
    map: LruCache<String, Entry>,
}

const DEFAULT_MAX_ITEMS: usize = 100_000;
static MAX_ITEMS: AtomicUsize = AtomicUsize::new(DEFAULT_MAX_ITEMS);

// Global Cache Storage (Phase2: LRU-backed)
static CACHE: Lazy<RwLock<CacheState>> = Lazy::new(|| {
    let cap = NonZeroUsize::new(DEFAULT_MAX_ITEMS).unwrap();
    RwLock::new(CacheState {
        map: LruCache::new(cap),
    })
});

static EXPIRY_THREAD_STARTED: OnceLock<()> = OnceLock::new();
static AOF_FILE: Lazy<Mutex<Option<std::fs::File>>> = Lazy::new(|| Mutex::new(None));

static STREAM_ID: AtomicU64 = AtomicU64::new(1);

// Helper for string conversion
unsafe fn to_string(ptr: *const c_char) -> String {
    if ptr.is_null() {
        return String::new();
    }
    CStr::from_ptr(ptr).to_string_lossy().into_owned()
}

unsafe fn to_bytes(ptr: *const c_uchar, len: usize) -> Vec<u8> {
    if ptr.is_null() || len == 0 {
        return Vec::new();
    }
    std::slice::from_raw_parts(ptr, len).to_vec()
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_else(|_| Duration::from_millis(0))
        .as_millis() as u64
}

fn is_expired(entry: &Entry) -> bool {
    match entry.expires_at_ms {
        Some(t) => now_ms() >= t,
        None => false,
    }
}

fn bytes_key_to_string(key_ptr: *const c_uchar, key_len: usize) -> String {
    if key_ptr.is_null() || key_len == 0 {
        return String::new();
    }
    let key_bytes = unsafe { std::slice::from_raw_parts(key_ptr, key_len) };
    let mut s = String::with_capacity(2 + key_bytes.len() * 2);
    s.push_str("b:");
    for b in key_bytes {
        use std::fmt::Write as _;
        let _ = write!(&mut s, "{:02x}", b);
    }
    s
}

fn maybe_remove_if_expired(state: &mut CacheState, key: &String) -> bool {
    if let Some(entry) = state.map.peek(key) {
        if is_expired(entry) {
            state.map.pop(key);
            notify_expired(key);
            return true;
        }
    }
    false
}

fn put_entry_with_lru(state: &mut CacheState, key: String, entry: Entry) {
    // Capture eviction for keyspace notifications.
    let cap = state.map.cap().get();
    if !state.map.contains(&key) && state.map.len() >= cap {
        if let Some((evicted_key, _evicted_entry)) = state.map.pop_lru() {
            notify_evicted(&evicted_key);
        }
    }
    state.map.put(key, entry);
}

fn start_expiry_thread_once() {
    let _ = EXPIRY_THREAD_STARTED.get_or_init(|| {
        std::thread::spawn(|| loop {
            std::thread::sleep(Duration::from_millis(250));
            let mut state = CACHE.write().unwrap();

            let mut expired_keys: Vec<String> = Vec::new();
            for (k, v) in state.map.iter() {
                if is_expired(v) {
                    expired_keys.push(k.clone());
                }
            }
            for k in expired_keys {
                state.map.pop(&k);
                notify_expired(&k);
            }
        });
    });
}

// --- AOF (Append Only File) ---

const AOF_OP_SET: u8 = 1;
const AOF_OP_REMOVE: u8 = 2;
const AOF_OP_CLEAR: u8 = 3;
const AOF_OP_EXPIRE: u8 = 4;
const AOF_OP_HSET: u8 = 5;
const AOF_OP_LPUSH: u8 = 6;
const AOF_OP_SADD: u8 = 7;
const AOF_OP_ZADD: u8 = 8;
const AOF_OP_XADD: u8 = 9;

fn aof_write(buf: &[u8]) {
    let mut guard = AOF_FILE.lock().unwrap();
    let Some(file) = guard.as_mut() else { return; };
    let _ = file.write_all(buf);
    let _ = file.flush();
}

fn aof_write_set(key: &str, val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + val.len());
    buf.push(AOF_OP_SET);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

fn aof_write_remove(key: &str) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len());
    buf.push(AOF_OP_REMOVE);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    aof_write(&buf);
}

fn aof_write_clear() {
    aof_write(&[AOF_OP_CLEAR]);
}

fn aof_write_expire(key: &str, ttl_ms: u64) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 8);
    buf.push(AOF_OP_EXPIRE);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&ttl_ms.to_le_bytes());
    aof_write(&buf);
}

fn aof_write_hset(key: &str, field: &str, val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + field.len() + 4 + val.len());
    buf.push(AOF_OP_HSET);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(field.len() as u32).to_le_bytes());
    buf.extend_from_slice(field.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

fn aof_write_lpush(key: &str, val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + val.len());
    buf.push(AOF_OP_LPUSH);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

fn aof_write_sadd(key: &str, val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + val.len());
    buf.push(AOF_OP_SADD);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

fn aof_write_zadd(key: &str, score: f64, member: &str) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 8 + 4 + member.len());
    buf.push(AOF_OP_ZADD);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&score.to_le_bytes());
    buf.extend_from_slice(&(member.len() as u32).to_le_bytes());
    buf.extend_from_slice(member.as_bytes());
    aof_write(&buf);
}

fn aof_write_xadd(key: &str, id: u64, payload: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 8 + 4 + payload.len());
    buf.push(AOF_OP_XADD);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&id.to_le_bytes());
    buf.extend_from_slice(&(payload.len() as u32).to_le_bytes());
    buf.extend_from_slice(payload);
    aof_write(&buf);
}

// --- Phase3: Pub/Sub + Keyspace Notifications ---

#[derive(Clone)]
struct NotifyEvent {
    kind: u8,
    key: String,
    at_ms: u64,
}

const NOTIFY_KIND_EXPIRED: u8 = 1;
const NOTIFY_KIND_EVICTED: u8 = 2;

static NOTIFY_QUEUE: Lazy<Mutex<VecDeque<NotifyEvent>>> = Lazy::new(|| Mutex::new(VecDeque::new()));

fn notify_expired(key: &str) {
    let mut q = NOTIFY_QUEUE.lock().unwrap();
    q.push_back(NotifyEvent {
        kind: NOTIFY_KIND_EXPIRED,
        key: key.to_string(),
        at_ms: now_ms(),
    });
}

fn notify_evicted(key: &str) {
    let mut q = NOTIFY_QUEUE.lock().unwrap();
    q.push_back(NotifyEvent {
        kind: NOTIFY_KIND_EVICTED,
        key: key.to_string(),
        at_ms: now_ms(),
    });
}

#[derive(Clone)]
struct PubMessage {
    channel: String,
    payload: Vec<u8>,
}

struct PubSubState {
    next_id: u64,
    subs: HashMap<u64, String>,
    channels: HashMap<String, Vec<u64>>,
    queues: HashMap<u64, VecDeque<PubMessage>>,
}

static PUBSUB: Lazy<Mutex<PubSubState>> = Lazy::new(|| {
    Mutex::new(PubSubState {
        next_id: 1,
        subs: HashMap::new(),
        channels: HashMap::new(),
        queues: HashMap::new(),
    })
});

fn read_exact_u8(r: &mut impl Read) -> Option<u8> {
    let mut b = [0u8; 1];
    r.read_exact(&mut b).ok()?;
    Some(b[0])
}

fn read_exact_u32(r: &mut impl Read) -> Option<u32> {
    let mut b = [0u8; 4];
    r.read_exact(&mut b).ok()?;
    Some(u32::from_le_bytes(b))
}

fn read_exact_u64(r: &mut impl Read) -> Option<u64> {
    let mut b = [0u8; 8];
    r.read_exact(&mut b).ok()?;
    Some(u64::from_le_bytes(b))
}

fn read_exact_f64(r: &mut impl Read) -> Option<f64> {
    let mut b = [0u8; 8];
    r.read_exact(&mut b).ok()?;
    Some(f64::from_le_bytes(b))
}

fn read_exact_vec(r: &mut impl Read, len: usize) -> Option<Vec<u8>> {
    let mut b = vec![0u8; len];
    r.read_exact(&mut b).ok()?;
    Some(b)
}

fn read_exact_string(r: &mut impl Read, len: usize) -> Option<String> {
    let bytes = read_exact_vec(r, len)?;
    Some(String::from_utf8_lossy(&bytes).into_owned())
}

fn apply_set_internal(state: &mut CacheState, key: String, val: Vec<u8>) {
    put_entry_with_lru(
        state,
        key,
        Entry {
            value: Value::Bytes(val),
            expires_at_ms: None,
        },
    );
}

fn apply_remove_internal(state: &mut CacheState, key: &String) {
    state.map.pop(key);
}

fn apply_clear_internal(state: &mut CacheState) {
    state.map.clear();
}

fn apply_expire_internal(state: &mut CacheState, key: &String, ttl_ms: u64) -> bool {
    if maybe_remove_if_expired(state, key) {
        return false;
    }
    let Some(mut entry) = state.map.pop(key) else { return false; };
    let expires_at = now_ms().saturating_add(ttl_ms);
    entry.expires_at_ms = Some(expires_at);
    put_entry_with_lru(state, key.clone(), entry);
    true
}

// Prepares a vector for FFI return: shrinks to fit (cap=len), forgets it, returns ptr/len
fn prepare_return(mut vec: Vec<u8>, out_len: *mut usize) -> *mut c_uchar {
    vec.shrink_to_fit();
    let len = vec.len();
    unsafe { *out_len = len };
    if len == 0 {
        return std::ptr::null_mut();
    }
    let ptr = vec.as_mut_ptr();
    std::mem::forget(vec);
    ptr
}

// --- Common ---

#[no_mangle]
pub extern "C" fn cache_init() {
    Lazy::force(&CACHE);
    start_expiry_thread_once();
}

#[no_mangle]
pub extern "C" fn cache_remove(key: *const c_char) {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();
    apply_remove_internal(&mut state, &key_str);
    aof_write_remove(&key_str);
}

#[no_mangle]
pub extern "C" fn cache_clear_all() {
    let mut state = CACHE.write().unwrap();
    apply_clear_internal(&mut state);
    aof_write_clear();
}

// --- Core / String (Value::Bytes) ---

#[no_mangle]
pub extern "C" fn cache_set(key: *const c_char, value: *const c_uchar, len: usize) {
    let key_str = unsafe { to_string(key) };
    let val_vec = unsafe { to_bytes(value, len) };
    let mut state = CACHE.write().unwrap();
    apply_set_internal(&mut state, key_str.clone(), val_vec.clone());
    aof_write_set(&key_str, &val_vec);
}

#[no_mangle]
pub extern "C" fn cache_get(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();

    if maybe_remove_if_expired(&mut state, &key_str) {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    if let Some(entry) = state.map.get(&key_str) {
        if let Value::Bytes(val) = &entry.value {
            return prepare_return(val.clone(), out_len);
        }
    }
    
    unsafe { *out_len = 0 };
    std::ptr::null_mut()
}

// --- Hashes ---

#[no_mangle]
pub extern "C" fn cache_hset(key: *const c_char, field: *const c_char, value: *const c_uchar, len: usize) {
    let key_str = unsafe { to_string(key) };
    let field_str = unsafe { to_string(field) };
    let val_vec = unsafe { to_bytes(value, len) };
    
    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        // fallthrough to create fresh
    }

    let mut entry = state
        .map
        .pop(&key_str)
        .unwrap_or(Entry { value: Value::Hash(HashMap::new()), expires_at_ms: None });

    match &mut entry.value {
        Value::Hash(hmap) => {
            hmap.insert(field_str.clone(), val_vec.clone());
        }
        _ => {
            let mut h = HashMap::new();
            h.insert(field_str.clone(), val_vec.clone());
            entry.value = Value::Hash(h);
        }
    }

    put_entry_with_lru(&mut state, key_str.clone(), entry);
    aof_write_hset(&key_str, &field_str, &val_vec);
}

#[no_mangle]
pub extern "C" fn cache_hget(key: *const c_char, field: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    let key_str = unsafe { to_string(key) };
    let field_str = unsafe { to_string(field) };
    
    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    if let Some(entry) = state.map.get(&key_str) {
        if let Value::Hash(hmap) = &entry.value {
            if let Some(val) = hmap.get(&field_str) {
                return prepare_return(val.clone(), out_len);
            }
        }
    }
    unsafe { *out_len = 0 };
    std::ptr::null_mut()
}

#[no_mangle]
pub extern "C" fn cache_hgetall(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();

    if maybe_remove_if_expired(&mut state, &key_str) {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    if let Some(entry) = state.map.get(&key_str) {
        if let Value::Hash(hmap) = &entry.value {
        let mut flat = Vec::new();
        // format: [Count (u32)] [KeyLen (u32)] [Key] [ValLen (u32)] [Val] ...
        flat.extend_from_slice(&(hmap.len() as u32).to_le_bytes());
        for (k, v) in hmap {
            let k_bytes = k.as_bytes();
            flat.extend_from_slice(&(k_bytes.len() as u32).to_le_bytes());
            flat.extend_from_slice(k_bytes);
            flat.extend_from_slice(&(v.len() as u32).to_le_bytes());
            flat.extend_from_slice(v);
        }
        return prepare_return(flat, out_len);
    }
    }
    
    unsafe { *out_len = 0 };
    std::ptr::null_mut()
}

// --- Lists ---

#[no_mangle]
pub extern "C" fn cache_lpush(key: *const c_char, value: *const c_uchar, len: usize) {
    let key_str = unsafe { to_string(key) };
    let val_vec = unsafe { to_bytes(value, len) };
    
    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        // create fresh
    }

    let mut entry = state
        .map
        .pop(&key_str)
        .unwrap_or(Entry { value: Value::List(Vec::new()), expires_at_ms: None });

    match &mut entry.value {
        Value::List(list) => list.insert(0, val_vec.clone()),
        _ => entry.value = Value::List(vec![val_vec.clone()]),
    }

    put_entry_with_lru(&mut state, key_str.clone(), entry);
    aof_write_lpush(&key_str, &val_vec);
}

#[no_mangle]
pub extern "C" fn cache_rpop(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    let key_str = unsafe { to_string(key) };

    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    let Some(mut entry) = state.map.pop(&key_str) else {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    };

    let mut popped: Option<Vec<u8>> = None;
    if let Value::List(list) = &mut entry.value {
        popped = list.pop();
    }

    // Keep key if list still exists (even empty) to match current behavior
    put_entry_with_lru(&mut state, key_str, entry);

    if let Some(val) = popped {
        return prepare_return(val, out_len);
    }
    unsafe { *out_len = 0 };
    std::ptr::null_mut()
}

#[no_mangle]
pub extern "C" fn cache_lrange(key: *const c_char, start: i32, end: i32, out_len: *mut usize) -> *mut c_uchar {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();

    if maybe_remove_if_expired(&mut state, &key_str) {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    if let Some(entry) = state.map.get(&key_str) {
        if let Value::List(list) = &entry.value {
         let len = list.len() as i32;
         let mut low = start;
         let mut high = end;
         
         if low < 0 { low += len; }
         if high < 0 { high += len; }
         if low < 0 { low = 0; }
         if high >= len { high = len - 1; }
         
         let mut flat = Vec::new();
         if low <= high {
             let count = (high - low + 1) as u32;
             flat.extend_from_slice(&count.to_le_bytes());
             for i in low..=high {
                 let item = &list[i as usize];
                 flat.extend_from_slice(&(item.len() as u32).to_le_bytes());
                 flat.extend_from_slice(item);
             }
         } else {
             flat.extend_from_slice(&(0u32).to_le_bytes());
         }
         return prepare_return(flat, out_len);
    }
        }
    
    unsafe { *out_len = 0 };
    std::ptr::null_mut()
}

// --- Sets ---

#[no_mangle]
pub extern "C" fn cache_sadd(key: *const c_char, value: *const c_uchar, len: usize) -> i32 {
    let key_str = unsafe { to_string(key) };
    let val_vec = unsafe { to_bytes(value, len) };
    
    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        // create fresh
    }

    let mut entry = state
        .map
        .pop(&key_str)
        .unwrap_or(Entry { value: Value::Set(HashSet::new()), expires_at_ms: None });

    let inserted = match &mut entry.value {
        Value::Set(set) => set.insert(val_vec.clone()),
        _ => {
            let mut s = HashSet::new();
            let inserted = s.insert(val_vec.clone());
            entry.value = Value::Set(s);
            inserted
        }
    };

    put_entry_with_lru(&mut state, key_str.clone(), entry);
    if inserted {
        aof_write_sadd(&key_str, &val_vec);
        1
    } else {
        0
    }
}

#[no_mangle]
pub extern "C" fn cache_sismember(key: *const c_char, value: *const c_uchar, len: usize) -> i32 {
    let key_str = unsafe { to_string(key) };
    let val_vec = unsafe { to_bytes(value, len) };
    
    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        return 0;
    }

    if let Some(entry) = state.map.get(&key_str) {
        if let Value::Set(set) = &entry.value {
            return if set.contains(&val_vec) { 1 } else { 0 };
        }
    }
    0
}

// --- Sorted Sets ---

#[no_mangle]
pub extern "C" fn cache_zadd(key: *const c_char, score: f64, member: *const c_char) {
    let key_str = unsafe { to_string(key) };
    let member_str = unsafe { to_string(member) };
    
    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        // create fresh
    }

    let mut entry = state
        .map
        .pop(&key_str)
        .unwrap_or(Entry { value: Value::SortedSet(HashMap::new()), expires_at_ms: None });

    match &mut entry.value {
        Value::SortedSet(ss) => {
            ss.insert(member_str.clone(), score);
        }
        _ => {
            let mut ss = HashMap::new();
            ss.insert(member_str.clone(), score);
            entry.value = Value::SortedSet(ss);
        }
    }

    put_entry_with_lru(&mut state, key_str.clone(), entry);
    aof_write_zadd(&key_str, score, &member_str);
}

#[no_mangle]
pub extern "C" fn cache_zrange(key: *const c_char, start: i32, end: i32, out_len: *mut usize) -> *mut c_uchar {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();

    if maybe_remove_if_expired(&mut state, &key_str) {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    if let Some(entry) = state.map.get(&key_str) {
        if let Value::SortedSet(ss) = &entry.value {
        let mut entries: Vec<(&String, &f64)> = ss.iter().collect();
        // Sort by score (asc)
        entries.sort_by(|a, b| a.1.partial_cmp(b.1).unwrap_or(std::cmp::Ordering::Equal));
        
        let len = entries.len() as i32;
        let mut low = start;
        let mut high = end;
        if low < 0 { low += len; }
        if high < 0 { high += len; }
        if low < 0 { low = 0; }
        if high >= len { high = len - 1; }
        
        let mut flat = Vec::new();
        if low <= high {
            let count = (high - low + 1) as u32;
            flat.extend_from_slice(&count.to_le_bytes());
            for i in low..=high {
                let (member, _) = entries[i as usize];
                let b = member.as_bytes();
                flat.extend_from_slice(&(b.len() as u32).to_le_bytes());
                flat.extend_from_slice(b);
            }
        } else {
             flat.extend_from_slice(&(0u32).to_le_bytes());
        }
        return prepare_return(flat, out_len);
    }
    }

    unsafe { *out_len = 0 };
    std::ptr::null_mut()
}

#[no_mangle]
pub extern "C" fn cache_free(ptr: *mut c_uchar, len: usize) {
    if ptr.is_null() || len == 0 {
        return;
    }
    unsafe {
        // Correctly free vector assuming cap == len
        let _ = Vec::from_raw_parts(ptr, len, len);
    }
}

// --- Phase2: LRU + TTL + AOF + Binary-Safe Keys ---

#[no_mangle]
pub extern "C" fn cache_set_max_items(max_items: usize) {
    let max_items = max_items.max(1);
    MAX_ITEMS.store(max_items, Ordering::Relaxed);
    let mut state = CACHE.write().unwrap();
    state.map.resize(NonZeroUsize::new(max_items).unwrap());
}

#[no_mangle]
pub extern "C" fn cache_get_max_items() -> usize {
    MAX_ITEMS.load(Ordering::Relaxed)
}

#[no_mangle]
pub extern "C" fn cache_len() -> usize {
    let state = CACHE.read().unwrap();
    state.map.len()
}

#[no_mangle]
pub extern "C" fn cache_set_with_ttl(key: *const c_char, value: *const c_uchar, len: usize, ttl_ms: u64) {
    let key_str = unsafe { to_string(key) };
    let val_vec = unsafe { to_bytes(value, len) };
    let expires_at = now_ms().saturating_add(ttl_ms);

    let mut state = CACHE.write().unwrap();
    put_entry_with_lru(
        &mut state,
        key_str.clone(),
        Entry {
            value: Value::Bytes(val_vec.clone()),
            expires_at_ms: Some(expires_at),
        },
    );

    aof_write_set(&key_str, &val_vec);
    aof_write_expire(&key_str, ttl_ms);
}

#[no_mangle]
pub extern "C" fn cache_expire(key: *const c_char, ttl_ms: u64) -> i32 {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();
    let ok = apply_expire_internal(&mut state, &key_str, ttl_ms);
    if ok {
        aof_write_expire(&key_str, ttl_ms);
        1
    } else {
        0
    }
}

#[no_mangle]
pub extern "C" fn cache_ttl(key: *const c_char) -> i64 {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();

    if maybe_remove_if_expired(&mut state, &key_str) {
        return -2;
    }

    let Some(entry) = state.map.get(&key_str) else { return -2; };
    match entry.expires_at_ms {
        None => -1,
        Some(t) => {
            let now = now_ms();
            if t <= now { 0 } else { (t - now) as i64 }
        }
    }
}

#[no_mangle]
pub extern "C" fn cache_aof_enable(path: *const c_char) -> i32 {
    let path_str = unsafe { to_string(path) };
    if path_str.is_empty() {
        return 0;
    }
    match std::fs::OpenOptions::new().create(true).append(true).open(&path_str) {
        Ok(f) => {
            let mut guard = AOF_FILE.lock().unwrap();
            *guard = Some(f);
            1
        }
        Err(_) => 0,
    }
}

#[no_mangle]
pub extern "C" fn cache_aof_disable() {
    let mut guard = AOF_FILE.lock().unwrap();
    *guard = None;
}

#[no_mangle]
pub extern "C" fn cache_aof_load(path: *const c_char) -> i32 {
    let path_str = unsafe { to_string(path) };
    if path_str.is_empty() {
        return 0;
    }
    let mut file = match std::fs::File::open(&path_str) {
        Ok(f) => f,
        Err(_) => return 0,
    };

    let mut state = CACHE.write().unwrap();

    loop {
        let Some(op) = read_exact_u8(&mut file) else { break; };
        match op {
            AOF_OP_SET => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };
                apply_set_internal(&mut state, key, val);
            }
            AOF_OP_REMOVE => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                apply_remove_internal(&mut state, &key);
            }
            AOF_OP_CLEAR => {
                apply_clear_internal(&mut state);
            }
            AOF_OP_EXPIRE => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                let ttl_ms = match read_exact_u64(&mut file) { Some(v) => v, None => break };
                let _ = apply_expire_internal(&mut state, &key, ttl_ms);
            }
            AOF_OP_HSET => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                let flen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let field = match read_exact_string(&mut file, flen) { Some(v) => v, None => break };
                let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };

                let mut entry = state
                    .map
                    .pop(&key)
                    .unwrap_or(Entry { value: Value::Hash(HashMap::new()), expires_at_ms: None });
                match &mut entry.value {
                    Value::Hash(hmap) => {
                        hmap.insert(field, val);
                    }
                    _ => {
                        let mut h = HashMap::new();
                        h.insert(field, val);
                        entry.value = Value::Hash(h);
                    }
                }
                state.map.put(key, entry);
            }
            AOF_OP_LPUSH => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };

                let mut entry = state
                    .map
                    .pop(&key)
                    .unwrap_or(Entry { value: Value::List(Vec::new()), expires_at_ms: None });
                match &mut entry.value {
                    Value::List(list) => list.insert(0, val),
                    _ => entry.value = Value::List(vec![val]),
                }
                state.map.put(key, entry);
            }
            AOF_OP_SADD => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };

                let mut entry = state
                    .map
                    .pop(&key)
                    .unwrap_or(Entry { value: Value::Set(HashSet::new()), expires_at_ms: None });
                match &mut entry.value {
                    Value::Set(set) => {
                        let _ = set.insert(val);
                    }
                    _ => {
                        let mut s = HashSet::new();
                        let _ = s.insert(val);
                        entry.value = Value::Set(s);
                    }
                }
                state.map.put(key, entry);
            }
            AOF_OP_ZADD => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                let score = match read_exact_f64(&mut file) { Some(v) => v, None => break };
                let mlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let member = match read_exact_string(&mut file, mlen) { Some(v) => v, None => break };

                let mut entry = state
                    .map
                    .pop(&key)
                    .unwrap_or(Entry { value: Value::SortedSet(HashMap::new()), expires_at_ms: None });
                match &mut entry.value {
                    Value::SortedSet(ss) => {
                        ss.insert(member, score);
                    }
                    _ => {
                        let mut ss = HashMap::new();
                        ss.insert(member, score);
                        entry.value = Value::SortedSet(ss);
                    }
                }
                state.map.put(key, entry);
            }
            AOF_OP_XADD => {
                let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                let id = match read_exact_u64(&mut file) { Some(v) => v, None => break };
                let plen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                let payload = match read_exact_vec(&mut file, plen) { Some(v) => v, None => break };

                let mut entry = state
                    .map
                    .pop(&key)
                    .unwrap_or(Entry { value: Value::Stream(StreamData { entries: Vec::new() }), expires_at_ms: None });
                match &mut entry.value {
                    Value::Stream(stream) => {
                        stream.entries.push(StreamEntry { id, payload });
                    }
                    _ => {
                        entry.value = Value::Stream(StreamData { entries: vec![StreamEntry { id, payload }] });
                    }
                }
                put_entry_with_lru(&mut state, key, entry);
            }
            _ => break,
        }
    }

    1
}

// Binary-safe keys: key bytes are encoded to a stable internal string key.

#[no_mangle]
pub extern "C" fn cache_set_b(key: *const c_uchar, key_len: usize, value: *const c_uchar, len: usize) {
    let key_str = bytes_key_to_string(key, key_len);
    let val_vec = unsafe { to_bytes(value, len) };
    let mut state = CACHE.write().unwrap();
    apply_set_internal(&mut state, key_str.clone(), val_vec.clone());
    aof_write_set(&key_str, &val_vec);
}

#[no_mangle]
pub extern "C" fn cache_get_b(key: *const c_uchar, key_len: usize, out_len: *mut usize) -> *mut c_uchar {
    let key_str = bytes_key_to_string(key, key_len);
    cache_get(std::ffi::CString::new(key_str).unwrap().as_ptr(), out_len)
}

#[no_mangle]
pub extern "C" fn cache_remove_b(key: *const c_uchar, key_len: usize) {
    let key_str = bytes_key_to_string(key, key_len);
    let mut state = CACHE.write().unwrap();
    apply_remove_internal(&mut state, &key_str);
    aof_write_remove(&key_str);
}

// --- Phase3: Pub/Sub ---

#[no_mangle]
pub extern "C" fn cache_pubsub_subscribe(channel: *const c_char) -> u64 {
    let channel_str = unsafe { to_string(channel) };
    if channel_str.is_empty() {
        return 0;
    }

    let mut ps = PUBSUB.lock().unwrap();
    let id = ps.next_id;
    ps.next_id = ps.next_id.saturating_add(1);
    ps.subs.insert(id, channel_str.clone());
    ps.channels.entry(channel_str).or_default().push(id);
    ps.queues.insert(id, VecDeque::new());
    id
}

#[no_mangle]
pub extern "C" fn cache_pubsub_unsubscribe(sub_id: u64) {
    if sub_id == 0 {
        return;
    }

    let mut ps = PUBSUB.lock().unwrap();
    let Some(channel) = ps.subs.remove(&sub_id) else {
        return;
    };
    if let Some(list) = ps.channels.get_mut(&channel) {
        list.retain(|id| *id != sub_id);
        if list.is_empty() {
            ps.channels.remove(&channel);
        }
    }
    ps.queues.remove(&sub_id);
}

#[no_mangle]
pub extern "C" fn cache_pubsub_publish(channel: *const c_char, payload: *const c_uchar, len: usize) -> u64 {
    let channel_str = unsafe { to_string(channel) };
    if channel_str.is_empty() {
        return 0;
    }
    let payload_vec = unsafe { to_bytes(payload, len) };

    let mut ps = PUBSUB.lock().unwrap();
    let Some(subs) = ps.channels.get(&channel_str) else {
        return 0;
    };
    let subs = subs.clone();

    let mut delivered = 0u64;
    for id in subs.into_iter() {
        if let Some(q) = ps.queues.get_mut(&id) {
            q.push_back(PubMessage {
                channel: channel_str.clone(),
                payload: payload_vec.clone(),
            });
            delivered += 1;
        }
    }
    delivered
}

#[no_mangle]
pub extern "C" fn cache_pubsub_poll(sub_id: u64, out_len: *mut usize) -> *mut c_uchar {
    if sub_id == 0 {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    let mut ps = PUBSUB.lock().unwrap();
    let Some(q) = ps.queues.get_mut(&sub_id) else {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    };
    let Some(msg) = q.pop_front() else {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    };

    let mut buf = Vec::new();
    buf.extend_from_slice(&(msg.channel.as_bytes().len() as u32).to_le_bytes());
    buf.extend_from_slice(msg.channel.as_bytes());
    buf.extend_from_slice(&(msg.payload.len() as u32).to_le_bytes());
    buf.extend_from_slice(&msg.payload);
    prepare_return(buf, out_len)
}

// --- Phase3: Keyspace notifications polling ---

#[no_mangle]
pub extern "C" fn cache_notifications_poll(out_len: *mut usize) -> *mut c_uchar {
    let mut q = NOTIFY_QUEUE.lock().unwrap();
    let Some(ev) = q.pop_front() else {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    };
    let mut buf = Vec::new();
    buf.push(ev.kind);
    buf.extend_from_slice(&(ev.key.as_bytes().len() as u32).to_le_bytes());
    buf.extend_from_slice(ev.key.as_bytes());
    buf.extend_from_slice(&ev.at_ms.to_le_bytes());
    prepare_return(buf, out_len)
}

#[no_mangle]
pub extern "C" fn cache_notifications_clear() {
    let mut q = NOTIFY_QUEUE.lock().unwrap();
    q.clear();
}

// --- Phase3: Streams ---

#[no_mangle]
pub extern "C" fn cache_xadd(key: *const c_char, payload: *const c_uchar, len: usize) -> u64 {
    let key_str = unsafe { to_string(key) };
    if key_str.is_empty() {
        return 0;
    }
    let payload_vec = unsafe { to_bytes(payload, len) };

    let id = STREAM_ID.fetch_add(1, Ordering::Relaxed);

    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        // create fresh
    }

    let mut entry = state
        .map
        .pop(&key_str)
        .unwrap_or(Entry { value: Value::Stream(StreamData { entries: Vec::new() }), expires_at_ms: None });

    match &mut entry.value {
        Value::Stream(stream) => {
            stream.entries.push(StreamEntry { id, payload: payload_vec.clone() });
        }
        _ => {
            entry.value = Value::Stream(StreamData { entries: vec![StreamEntry { id, payload: payload_vec.clone() }] });
        }
    }

    put_entry_with_lru(&mut state, key_str.clone(), entry);
    aof_write_xadd(&key_str, id, &payload_vec);
    id
}

#[no_mangle]
pub extern "C" fn cache_xrange(key: *const c_char, start_id: u64, end_id: u64, out_len: *mut usize) -> *mut c_uchar {
    let key_str = unsafe { to_string(key) };
    let mut state = CACHE.write().unwrap();
    if maybe_remove_if_expired(&mut state, &key_str) {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    }

    let Some(entry) = state.map.get(&key_str) else {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    };

    let Value::Stream(stream) = &entry.value else {
        unsafe { *out_len = 0 };
        return std::ptr::null_mut();
    };

    let mut items: Vec<&StreamEntry> = stream
        .entries
        .iter()
        .filter(|e| e.id >= start_id && e.id <= end_id)
        .collect();
    items.sort_by_key(|e| e.id);

    let mut flat = Vec::new();
    flat.extend_from_slice(&(items.len() as u32).to_le_bytes());
    for e in items {
        flat.extend_from_slice(&e.id.to_le_bytes());
        flat.extend_from_slice(&(e.payload.len() as u32).to_le_bytes());
        flat.extend_from_slice(&e.payload);
    }
    prepare_return(flat, out_len)
}
