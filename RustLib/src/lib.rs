mod aof;
mod expiry;
mod jsonpath;
mod notifications;
mod pubsub;

use std::collections::{BTreeMap, HashMap, HashSet, VecDeque};
use std::ffi::CStr;
use std::num::NonZeroUsize;
use std::os::raw::{c_char, c_uchar};
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, OnceLock, RwLock};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use lru::LruCache;
use once_cell::sync::Lazy;
use serde_json::Value as JsonValue;

use aof::{
    aof_write_clear, aof_write_expire_at, aof_write_hset, aof_write_lpush,
    aof_write_remove, aof_write_remove_b, aof_write_sadd, aof_write_set,
    aof_write_set_b, aof_write_xadd, aof_write_zadd, read_exact_f64,
    read_exact_string, read_exact_u32, read_exact_u64, read_exact_u8,
    read_exact_vec, AOF_FILE, AOF_OP_CLEAR, AOF_OP_EXPIRE, AOF_OP_EXPIRE_AT,
    AOF_OP_HSET, AOF_OP_LPUSH, AOF_OP_REMOVE, AOF_OP_REMOVE_B, AOF_OP_SADD,
    AOF_OP_SET, AOF_OP_SET_B, AOF_OP_XADD, AOF_OP_ZADD,
};
use expiry::{schedule_expiry, ExpiryEntry, ExpiryKey, EXPIRY_HEAP};
use jsonpath::{json_get_at_path, json_set_at_path, parse_json_path};
use notifications::{notify_evicted, notify_expired, NOTIFY_QUEUE};
use pubsub::{PubMessage, PUBSUB};

/// Catch panics at the FFI boundary so they never unwind into managed code
/// (which would be undefined behavior). Each `pub extern "C" fn` runs its
/// body inside `catch_unwind`; on panic we return the supplied default value
/// and log to stderr.
fn ffi_guard<R>(label: &'static str, default: R, f: impl FnOnce() -> R) -> R {
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(v) => v,
        Err(payload) => {
            let msg = if let Some(s) = payload.downcast_ref::<&str>() {
                (*s).to_string()
            } else if let Some(s) = payload.downcast_ref::<String>() {
                s.clone()
            } else {
                "non-string panic payload".to_string()
            };
            eprintln!("[rust_cache] panic in {label}: {msg}");
            default
        }
    }
}

// Define the Value enum to support multiple data structures.
//
// `List(VecDeque<Vec<u8>>)` instead of `Vec<Vec<u8>>`: LPUSH pushes
// onto the head, which would be O(n) on Vec (Vec::insert(0, _) shifts
// every element). VecDeque is a ring buffer with O(1) push_front /
// push_back / pop_front / pop_back. LRANGE / get-by-index works the
// same way (VecDeque implements Index<usize>).
#[derive(Clone)]
enum Value {
    Bytes(Arc<Vec<u8>>),
    Hash(HashMap<String, Vec<u8>>),
    List(VecDeque<Vec<u8>>),
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

/// One slice of the sharded cache. Holds its own LRU of string-keyed
/// and binary-keyed entries. Shards are independent — concurrent
/// operations on different shards do not block each other.
struct CacheState {
    map: LruCache<String, Entry>,
    map_b: LruCache<Vec<u8>, Entry>,
}

/// Phase4: numeric secondary index. Global (cross-shard) because the
/// query language matches keys regardless of which shard they live on.
type Indexes = HashMap<String, BTreeMap<i64, HashSet<String>>>;

/// Lock-acquisition order is **always** shard → indexes. Any code that
/// holds an indexes lock must NOT then try to acquire a shard lock, or
/// the two could deadlock under contention.
struct ShardedCache {
    shards: Vec<RwLock<CacheState>>,
    indexes: RwLock<Indexes>,
}

const DEFAULT_MAX_ITEMS: usize = 100_000;
const NUM_SHARDS: usize = 16;
static MAX_ITEMS: AtomicUsize = AtomicUsize::new(DEFAULT_MAX_ITEMS);

fn per_shard_cap(total: usize) -> NonZeroUsize {
    NonZeroUsize::new((total / NUM_SHARDS).max(1)).unwrap()
}

/// Pick a shard for a string key. We use `DefaultHasher` for fast,
/// uniform distribution; FNV / xxhash would also work — anything
/// stable across the program's lifetime is fine.
fn shard_for_str(key: &str) -> usize {
    use std::hash::{Hash, Hasher};
    let mut h = std::collections::hash_map::DefaultHasher::new();
    key.hash(&mut h);
    (h.finish() as usize) % NUM_SHARDS
}

fn shard_for_b(key: &[u8]) -> usize {
    use std::hash::{Hash, Hasher};
    let mut h = std::collections::hash_map::DefaultHasher::new();
    key.hash(&mut h);
    (h.finish() as usize) % NUM_SHARDS
}

// Global Cache Storage — `NUM_SHARDS` independent LRUs plus one global
// indexes table.
static CACHE: Lazy<ShardedCache> = Lazy::new(|| {
    let cap = per_shard_cap(DEFAULT_MAX_ITEMS);
    let mut shards = Vec::with_capacity(NUM_SHARDS);
    for _ in 0..NUM_SHARDS {
        shards.push(RwLock::new(CacheState {
            map: LruCache::new(cap),
            map_b: LruCache::new(cap),
        }));
    }
    ShardedCache {
        shards,
        indexes: RwLock::new(HashMap::new()),
    }
});

static EXPIRY_THREAD_STARTED: OnceLock<()> = OnceLock::new();

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

pub(crate) fn now_ms() -> u64 {
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

fn bytes_to_hex_key(key_bytes: &[u8]) -> String {
    if key_bytes.is_empty() {
        return String::new();
    }
    let mut s = String::with_capacity(2 + key_bytes.len() * 2);
    s.push_str("b:");
    for b in key_bytes {
        use std::fmt::Write as _;
        let _ = write!(&mut s, "{:02x}", b);
    }
    s
}

/// Drop the entry if its TTL has passed. Caller already holds the
/// shard write-lock; we additionally grab the indexes lock if the
/// evicted entry needs to be un-indexed. Returns true if anything was
/// dropped.
fn maybe_remove_if_expired(shard: &mut CacheState, key: &String) -> bool {
    if let Some(entry) = shard.map.peek(key) {
        if is_expired(entry) {
            if let Some(evicted) = shard.map.pop(key) {
                let mut idx = CACHE.indexes.write().unwrap();
                index_remove_for_entry(&mut idx, key, &evicted);
            }
            notify_expired(key);
            return true;
        }
    }
    false
}

fn maybe_remove_if_expired_b(shard: &mut CacheState, key: &Vec<u8>) -> bool {
    if let Some(entry) = shard.map_b.peek(key) {
        if is_expired(entry) {
            let _ = shard.map_b.pop(key);
            // Binary entries are not numerically indexed; nothing to clean up
            // in the indexes table.
            let key_str = bytes_to_hex_key(key);
            notify_expired(&key_str);
            return true;
        }
    }
    false
}

fn try_parse_json_from_entry(entry: &Entry) -> Option<JsonValue> {
    let Value::Bytes(b) = &entry.value else { return None; };
    serde_json::from_slice::<JsonValue>(b.as_slice()).ok()
}

fn extract_numeric_field(json: &JsonValue, field: &str) -> Option<i64> {
    let obj = json.as_object()?;
    let v = obj.get(field)?;
    if let Some(i) = v.as_i64() {
        return Some(i);
    }
    if let Some(u) = v.as_u64() {
        return i64::try_from(u).ok();
    }
    None
}

fn index_remove_for_entry(indexes: &mut Indexes, key: &str, entry: &Entry) {
    if indexes.is_empty() {
        return;
    }
    let Some(json) = try_parse_json_from_entry(entry) else { return; };

    for (field, idx) in indexes.iter_mut() {
        if let Some(num) = extract_numeric_field(&json, field) {
            if let Some(keys) = idx.get_mut(&num) {
                keys.remove(key);
                if keys.is_empty() {
                    idx.remove(&num);
                }
            }
        }
    }
}

fn index_add_for_entry(indexes: &mut Indexes, key: &str, entry: &Entry) {
    if indexes.is_empty() {
        return;
    }
    let Some(json) = try_parse_json_from_entry(entry) else { return; };

    for (field, idx) in indexes.iter_mut() {
        if let Some(num) = extract_numeric_field(&json, field) {
            idx.entry(num).or_default().insert(key.to_string());
        }
    }
}

/// Insert (or overwrite) a string-keyed entry. Acquires the indexes lock
/// internally when there's anything to un-index or re-index — callers
/// must already hold the shard write-lock (shard → indexes ordering).
fn put_entry_with_lru(shard: &mut CacheState, key: String, entry: Entry) {
    // Capture eviction for keyspace notifications.
    let cap = shard.map.cap().get();
    let mut evicted_for_index: Option<(String, Entry)> = None;
    if !shard.map.contains(&key) && shard.map.len() >= cap {
        if let Some((evicted_key, evicted_entry)) = shard.map.pop_lru() {
            evicted_for_index = Some((evicted_key, evicted_entry));
        }
    }
    // If overwriting an existing key, drop the old entry so its index
    // contribution can be cleaned up below.
    let overwritten = shard.map.pop(&key);

    {
        let mut idx = CACHE.indexes.write().unwrap();
        if let Some((evicted_key, evicted_entry)) = &evicted_for_index {
            index_remove_for_entry(&mut idx, evicted_key, evicted_entry);
        }
        if let Some(old) = &overwritten {
            index_remove_for_entry(&mut idx, &key, old);
        }
        if !idx.is_empty() {
            index_add_for_entry(&mut idx, &key, &entry);
        }
    }
    if let Some((evicted_key, _)) = evicted_for_index {
        notify_evicted(&evicted_key);
    }
    // Schedule expiry on the timing wheel if this entry has a TTL.
    if let Some(exp) = entry.expires_at_ms {
        schedule_expiry(ExpiryKey::Str(key.clone()), exp);
    }
    shard.map.put(key, entry);
}

fn put_entry_with_lru_b(shard: &mut CacheState, key: Vec<u8>, entry: Entry) {
    let cap = shard.map_b.cap().get();
    if !shard.map_b.contains(&key) && shard.map_b.len() >= cap {
        if let Some((evicted_key, _evicted_entry)) = shard.map_b.pop_lru() {
            let evicted_key_str = bytes_to_hex_key(&evicted_key);
            notify_evicted(&evicted_key_str);
        }
    }
    let _ = shard.map_b.pop(&key);
    if let Some(exp) = entry.expires_at_ms {
        schedule_expiry(ExpiryKey::Bytes(key.clone()), exp);
    }
    shard.map_b.put(key, entry);
}

fn start_expiry_thread_once() {
    let _ = EXPIRY_THREAD_STARTED.get_or_init(|| {
        std::thread::spawn(|| loop {
            // Decide how long to sleep based on the heap's earliest entry.
            // No TTL'd entries → idle 1 second (cheap, no cache iteration).
            // Otherwise sleep until that timestamp (capped at 1s so new
            // earlier entries get noticed within bounded latency).
            let sleep_ms = {
                let heap = EXPIRY_HEAP.lock().unwrap();
                match heap.peek() {
                    Some(top) => {
                        let now = now_ms();
                        if top.expires_at_ms <= now { 0 } else {
                            let delta = top.expires_at_ms - now;
                            delta.min(1000)
                        }
                    }
                    None => 1000,
                }
            };
            if sleep_ms > 0 {
                std::thread::sleep(Duration::from_millis(sleep_ms));
            }

            // Drain everything that has come due. Stale entries (where the
            // cache has since updated, removed, or refreshed the key) are
            // skipped without touching the cache write lock — the heap
            // check is itself lockless w.r.t. the cache.
            let now = now_ms();
            let mut due: Vec<ExpiryEntry> = Vec::new();
            {
                let mut heap = EXPIRY_HEAP.lock().unwrap();
                while let Some(top) = heap.peek() {
                    if top.expires_at_ms > now { break; }
                    if let Some(e) = heap.pop() {
                        due.push(e);
                    }
                }
            }
            if due.is_empty() {
                continue;
            }

            for ExpiryEntry { expires_at_ms, key } in due {
                match key {
                    ExpiryKey::Str(k) => {
                        let shard_idx = shard_for_str(&k);
                        let mut shard = CACHE.shards[shard_idx].write().unwrap();
                        // Validate: the cache entry still has this exact
                        // expires_at_ms. Otherwise the heap record is stale
                        // (key was updated or removed) and we skip.
                        let still_valid = matches!(
                            shard.map.peek(&k),
                            Some(e) if e.expires_at_ms == Some(expires_at_ms)
                        );
                        if still_valid {
                            if let Some(evicted) = shard.map.pop(&k) {
                                let mut idx = CACHE.indexes.write().unwrap();
                                index_remove_for_entry(&mut idx, &k, &evicted);
                            }
                            notify_expired(&k);
                        }
                    }
                    ExpiryKey::Bytes(k) => {
                        let shard_idx = shard_for_b(&k);
                        let mut shard = CACHE.shards[shard_idx].write().unwrap();
                        let still_valid = matches!(
                            shard.map_b.peek(&k),
                            Some(e) if e.expires_at_ms == Some(expires_at_ms)
                        );
                        if still_valid {
                            let _ = shard.map_b.pop(&k);
                            let key_str = bytes_to_hex_key(&k);
                            notify_expired(&key_str);
                        }
                    }
                }
            }
        });
    });
}

fn apply_set_internal(shard: &mut CacheState, key: String, val: Vec<u8>) {
    put_entry_with_lru(
        shard,
        key,
        Entry {
            value: Value::Bytes(Arc::new(val)),
            expires_at_ms: None,
        },
    );
}

fn apply_set_internal_b(shard: &mut CacheState, key: Vec<u8>, val: Vec<u8>) {
    put_entry_with_lru_b(
        shard,
        key,
        Entry {
            value: Value::Bytes(Arc::new(val)),
            expires_at_ms: None,
        },
    );
}

fn apply_remove_internal(shard: &mut CacheState, key: &String) {
    if let Some(old) = shard.map.pop(key) {
        let mut idx = CACHE.indexes.write().unwrap();
        index_remove_for_entry(&mut idx, key, &old);
    }
}

fn apply_remove_internal_b(shard: &mut CacheState, key: &Vec<u8>) {
    let _ = shard.map_b.pop(key);
}

fn apply_clear_internal(shard: &mut CacheState) {
    shard.map.clear();
    shard.map_b.clear();
}

/// Clears every shard + the global indexes. Acquires locks in
/// deterministic order (shards 0..N, then indexes) to avoid surprising
/// any future code that adopts the same convention.
fn clear_all_shards() {
    for shard_lock in &CACHE.shards {
        let mut shard = shard_lock.write().unwrap();
        apply_clear_internal(&mut shard);
    }
    CACHE.indexes.write().unwrap().clear();
}

fn apply_expire_internal(shard: &mut CacheState, key: &String, ttl_ms: u64) -> bool {
    if maybe_remove_if_expired(shard, key) {
        return false;
    }
    let Some(mut entry) = shard.map.pop(key) else { return false; };
    let expires_at = now_ms().saturating_add(ttl_ms);
    entry.expires_at_ms = Some(expires_at);
    put_entry_with_lru(shard, key.clone(), entry);
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
    ffi_guard("cache_init", (), || {
        Lazy::force(&CACHE);
        start_expiry_thread_once();
    })
}

#[no_mangle]
pub extern "C" fn cache_remove(key: *const c_char) {
    ffi_guard("cache_remove", (), || {
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        apply_remove_internal(&mut state, &key_str);
        aof_write_remove(&key_str);
    })
}

#[no_mangle]
pub extern "C" fn cache_clear_all() {
    ffi_guard("cache_clear_all", (), || {
        clear_all_shards();
        aof_write_clear();
    })
}

// --- Core / String (Value::Bytes) ---

#[no_mangle]
pub extern "C" fn cache_set(key: *const c_char, value: *const c_uchar, len: usize) {
    ffi_guard("cache_set", (), || {
        let key_str = unsafe { to_string(key) };
        let val_vec = unsafe { to_bytes(value, len) };
        // Write AOF without holding the cache lock.
        aof_write_set(&key_str, &val_vec);
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        apply_set_internal(&mut state, key_str, val_vec);
    })
}

#[no_mangle]
pub extern "C" fn cache_get(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_get", std::ptr::null_mut(), || {
        if out_len.is_null() {
            return std::ptr::null_mut();
        }
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();

        if maybe_remove_if_expired(&mut state, &key_str) {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        }

        if let Some(entry) = state.map.get(&key_str) {
            if let Value::Bytes(val) = &entry.value {
                return prepare_return((**val).clone(), out_len);
            }
        }

        unsafe { *out_len = 0 };
        std::ptr::null_mut()
    })
}

// Copy value bytes into a caller-provided buffer.
// Return semantics:
//  -1  => key missing (or expired)
//  <0  => buffer too small; required length is -ret
//  >0  => bytes written
//   0  => value exists but is empty
#[no_mangle]
pub extern "C" fn cache_get_into(key: *const c_char, dst: *mut c_uchar, dst_len: usize) -> i64 {
    ffi_guard("cache_get_into", -1, || {
        let key_str = unsafe { to_string(key) };
        if key_str.is_empty() {
            return -1;
        }

        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        if maybe_remove_if_expired(&mut state, &key_str) {
            return -1;
        }

        let Some(entry) = state.map.get(&key_str) else {
            return -1;
        };

        let Value::Bytes(val) = &entry.value else {
            return -1;
        };

        let value_len = val.len();
        if value_len == 0 {
            return 0;
        }

        if dst.is_null() || dst_len < value_len {
            return -(value_len as i64);
        }

        unsafe {
            std::ptr::copy_nonoverlapping(val.as_ptr(), dst, value_len);
        }
        value_len as i64
    })
}

// --- Hashes ---

#[no_mangle]
pub extern "C" fn cache_hset(key: *const c_char, field: *const c_char, value: *const c_uchar, len: usize) {
    ffi_guard("cache_hset", (), || {
        let key_str = unsafe { to_string(key) };
        let field_str = unsafe { to_string(field) };
        let val_vec = unsafe { to_bytes(value, len) };
    
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
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
    })
}

#[no_mangle]
pub extern "C" fn cache_hget(key: *const c_char, field: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_hget", std::ptr::null_mut(), || {
        let key_str = unsafe { to_string(key) };
        let field_str = unsafe { to_string(field) };
    
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
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
    })
}

#[no_mangle]
pub extern "C" fn cache_hgetall(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_hgetall", std::ptr::null_mut(), || {
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();

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
    })
}

// --- Lists ---

#[no_mangle]
pub extern "C" fn cache_lpush(key: *const c_char, value: *const c_uchar, len: usize) {
    ffi_guard("cache_lpush", (), || {
        let key_str = unsafe { to_string(key) };
        let val_vec = unsafe { to_bytes(value, len) };
    
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        if maybe_remove_if_expired(&mut state, &key_str) {
            // create fresh
        }

        let mut entry = state
            .map
            .pop(&key_str)
            .unwrap_or(Entry { value: Value::List(VecDeque::new()), expires_at_ms: None });

        match &mut entry.value {
            Value::List(list) => list.push_front(val_vec.clone()),
            _ => {
                let mut d = VecDeque::with_capacity(1);
                d.push_front(val_vec.clone());
                entry.value = Value::List(d);
            }
        }

        put_entry_with_lru(&mut state, key_str.clone(), entry);
        aof_write_lpush(&key_str, &val_vec);
    })
}

#[no_mangle]
pub extern "C" fn cache_rpop(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_rpop", std::ptr::null_mut(), || {
        let key_str = unsafe { to_string(key) };

        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
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
            // RPOP — Redis semantics: take the *tail* element.
            popped = list.pop_back();
        }

        // Keep key if list still exists (even empty) to match current behavior
        put_entry_with_lru(&mut state, key_str, entry);

        if let Some(val) = popped {
            return prepare_return(val, out_len);
        }
        unsafe { *out_len = 0 };
        std::ptr::null_mut()
    })
}

#[no_mangle]
pub extern "C" fn cache_lrange(key: *const c_char, start: i32, end: i32, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_lrange", std::ptr::null_mut(), || {
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();

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
    })
}

// --- Sets ---

#[no_mangle]
pub extern "C" fn cache_sadd(key: *const c_char, value: *const c_uchar, len: usize) -> i32 {
    ffi_guard("cache_sadd", 0, || {
        let key_str = unsafe { to_string(key) };
        let val_vec = unsafe { to_bytes(value, len) };
    
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
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
    })
}

#[no_mangle]
pub extern "C" fn cache_sismember(key: *const c_char, value: *const c_uchar, len: usize) -> i32 {
    ffi_guard("cache_sismember", 0, || {
        let key_str = unsafe { to_string(key) };
        let val_vec = unsafe { to_bytes(value, len) };
    
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        if maybe_remove_if_expired(&mut state, &key_str) {
            return 0;
        }

        if let Some(entry) = state.map.get(&key_str) {
            if let Value::Set(set) = &entry.value {
                return if set.contains(&val_vec) { 1 } else { 0 };
            }
        }
        0
    })
}

// --- Sorted Sets ---

#[no_mangle]
pub extern "C" fn cache_zadd(key: *const c_char, score: f64, member: *const c_char) {
    ffi_guard("cache_zadd", (), || {
        let key_str = unsafe { to_string(key) };
        let member_str = unsafe { to_string(member) };
    
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
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
    })
}

#[no_mangle]
pub extern "C" fn cache_zrange(key: *const c_char, start: i32, end: i32, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_zrange", std::ptr::null_mut(), || {
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();

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
    })
}

#[no_mangle]
pub extern "C" fn cache_free(ptr: *mut c_uchar, len: usize) {
    ffi_guard("cache_free", (), || {
        if ptr.is_null() || len == 0 {
            return;
        }
        unsafe {
            // Correctly free vector assuming cap == len
            let _ = Vec::from_raw_parts(ptr, len, len);
        }
    })
}

// --- Phase2: LRU + TTL + AOF + Binary-Safe Keys ---

#[no_mangle]
pub extern "C" fn cache_set_max_items(max_items: usize) {
    ffi_guard("cache_set_max_items", (), || {
        let max_items = max_items.max(1);
        MAX_ITEMS.store(max_items, Ordering::Relaxed);
        // Divide capacity across shards. Each shard gets at least 1
        // slot so `LruCache::new` / `resize` doesn't panic on small
        // totals.
        let cap = per_shard_cap(max_items);
        for shard_lock in &CACHE.shards {
            let mut shard = shard_lock.write().unwrap();
            shard.map.resize(cap);
            shard.map_b.resize(cap);
        }
    })
}

#[no_mangle]
pub extern "C" fn cache_get_max_items() -> usize {
    ffi_guard("cache_get_max_items", 0, || {
        MAX_ITEMS.load(Ordering::Relaxed)
    })
}

#[no_mangle]
pub extern "C" fn cache_len() -> usize {
    ffi_guard("cache_len", 0, || {
        let mut total = 0usize;
        for shard_lock in &CACHE.shards {
            let shard = shard_lock.read().unwrap();
            total = total
                .saturating_add(shard.map.len())
                .saturating_add(shard.map_b.len());
        }
        total
    })
}

#[no_mangle]
pub extern "C" fn cache_set_with_ttl(key: *const c_char, value: *const c_uchar, len: usize, ttl_ms: u64) {
    ffi_guard("cache_set_with_ttl", (), || {
        let key_str = unsafe { to_string(key) };
        let val_vec = unsafe { to_bytes(value, len) };
        let expires_at = now_ms().saturating_add(ttl_ms);

        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        put_entry_with_lru(
            &mut state,
            key_str.clone(),
            Entry {
                value: Value::Bytes(Arc::new(val_vec.clone())),
                expires_at_ms: Some(expires_at),
            },
        );

        aof_write_set(&key_str, &val_vec);
        aof_write_expire_at(&key_str, expires_at);
    })
}

#[no_mangle]
pub extern "C" fn cache_expire(key: *const c_char, ttl_ms: u64) -> i32 {
    ffi_guard("cache_expire", 0, || {
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        let ok = apply_expire_internal(&mut state, &key_str, ttl_ms);
        if ok {
            let expires_at = now_ms().saturating_add(ttl_ms);
            aof_write_expire_at(&key_str, expires_at);
            1
        } else {
            0
        }
    })
}

#[no_mangle]
pub extern "C" fn cache_ttl(key: *const c_char) -> i64 {
    ffi_guard("cache_ttl", -1, || {
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();

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
    })
}

#[no_mangle]
pub extern "C" fn cache_aof_enable(path: *const c_char) -> i32 {
    ffi_guard("cache_aof_enable", 0, || {
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
    })
}

#[no_mangle]
pub extern "C" fn cache_aof_disable() {
    ffi_guard("cache_aof_disable", (), || {
        let mut guard = AOF_FILE.lock().unwrap();
        *guard = None;
    })
}

#[no_mangle]
pub extern "C" fn cache_aof_load(path: *const c_char) -> i32 {
    ffi_guard("cache_aof_load", 0, || {
        let path_str = unsafe { to_string(path) };
        if path_str.is_empty() {
            return 0;
        }
        let mut file = match std::fs::File::open(&path_str) {
            Ok(f) => f,
            Err(_) => return 0,
        };

        // Replay re-acquires per-shard locks per op. Replay is cold-path
        // (only at startup), so the extra lock churn is acceptable in
        // exchange for not blocking every other shard while we replay.
        loop {
            let Some(op) = read_exact_u8(&mut file) else { break; };
            match op {
                AOF_OP_SET => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();
                    apply_set_internal(&mut shard, key, val);
                }
                AOF_OP_SET_B => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_vec(&mut file, klen) { Some(v) => v, None => break };
                    let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_b(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();
                    apply_set_internal_b(&mut shard, key, val);
                }
                AOF_OP_REMOVE => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();
                    apply_remove_internal(&mut shard, &key);
                }
                AOF_OP_REMOVE_B => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_vec(&mut file, klen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_b(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();
                    apply_remove_internal_b(&mut shard, &key);
                }
                AOF_OP_CLEAR => {
                    clear_all_shards();
                }
                AOF_OP_EXPIRE => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let ttl_ms = match read_exact_u64(&mut file) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();
                    let _ = apply_expire_internal(&mut shard, &key, ttl_ms);
                }
                AOF_OP_EXPIRE_AT => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let expires_at_ms = match read_exact_u64(&mut file) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();
                    if expires_at_ms <= now_ms() {
                        apply_remove_internal(&mut shard, &key);
                    } else if let Some(mut entry) = shard.map.pop(&key) {
                        entry.expires_at_ms = Some(expires_at_ms);
                        put_entry_with_lru(&mut shard, key, entry);
                    }
                }
                AOF_OP_HSET => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let flen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let field = match read_exact_string(&mut file, flen) { Some(v) => v, None => break };
                    let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();

                    let mut entry = shard
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
                    put_entry_with_lru(&mut shard, key, entry);
                }
                AOF_OP_LPUSH => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();

                    let mut entry = shard
                        .map
                        .pop(&key)
                        .unwrap_or(Entry { value: Value::List(VecDeque::new()), expires_at_ms: None });
                    match &mut entry.value {
                        Value::List(list) => list.push_front(val),
                        _ => {
                            let mut d = VecDeque::with_capacity(1);
                            d.push_front(val);
                            entry.value = Value::List(d);
                        }
                    }
                    put_entry_with_lru(&mut shard, key, entry);
                }
                AOF_OP_SADD => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let vlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let val = match read_exact_vec(&mut file, vlen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();

                    let mut entry = shard
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
                    put_entry_with_lru(&mut shard, key, entry);
                }
                AOF_OP_ZADD => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let score = match read_exact_f64(&mut file) { Some(v) => v, None => break };
                    let mlen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let member = match read_exact_string(&mut file, mlen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();

                    let mut entry = shard
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
                    put_entry_with_lru(&mut shard, key, entry);
                }
                AOF_OP_XADD => {
                    let klen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let key = match read_exact_string(&mut file, klen) { Some(v) => v, None => break };
                    let id = match read_exact_u64(&mut file) { Some(v) => v, None => break };
                    let plen = match read_exact_u32(&mut file) { Some(v) => v as usize, None => break };
                    let payload = match read_exact_vec(&mut file, plen) { Some(v) => v, None => break };
                    let shard_idx = shard_for_str(&key);
                    let mut shard = CACHE.shards[shard_idx].write().unwrap();

                    let mut entry = shard
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
                    put_entry_with_lru(&mut shard, key, entry);
                }
                _ => break,
            }
        }

        1
    })
}

// Binary keys (byte-for-byte): stored separately to avoid key encoding overhead.

#[no_mangle]
pub extern "C" fn cache_set_b(key: *const c_uchar, key_len: usize, value: *const c_uchar, len: usize) {
    ffi_guard("cache_set_b", (), || {
        let key_vec = unsafe { to_bytes(key, key_len) };
        let val_vec = unsafe { to_bytes(value, len) };
        // Write AOF without holding the cache lock.
        aof_write_set_b(&key_vec, &val_vec);
        let shard_idx = shard_for_b(&key_vec);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        apply_set_internal_b(&mut state, key_vec, val_vec);
    })
}

// --- Phase4: JSON Path Support (basic) ---

#[no_mangle]
pub extern "C" fn cache_json_get(key: *const c_char, path: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_json_get", std::ptr::null_mut(), || {
        let key_str = unsafe { to_string(key) };
        let path_str = unsafe { to_string(path) };
        let Some(tokens) = parse_json_path(&path_str) else {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        };

        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        if maybe_remove_if_expired(&mut state, &key_str) {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        }

        let Some(entry) = state.map.get(&key_str) else {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        };

        let Some(json) = try_parse_json_from_entry(entry) else {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        };

        let Some(v) = json_get_at_path(&json, &tokens) else {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        };

        let bytes = match serde_json::to_vec(v) {
            Ok(b) => b,
            Err(_) => {
                unsafe { *out_len = 0 };
                return std::ptr::null_mut();
            }
        };

        prepare_return(bytes, out_len)
    })
}

#[no_mangle]
pub extern "C" fn cache_json_set(key: *const c_char, path: *const c_char, json_value: *const c_uchar, len: usize) -> i32 {
    ffi_guard("cache_json_set", 0, || {
        let key_str = unsafe { to_string(key) };
        let path_str = unsafe { to_string(path) };
        let Some(tokens) = parse_json_path(&path_str) else {
            return 0;
        };

        let new_bytes = unsafe { to_bytes(json_value, len) };
        let new_val: JsonValue = match serde_json::from_slice(&new_bytes) {
            Ok(v) => v,
            Err(_) => return 0,
        };

        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        if maybe_remove_if_expired(&mut state, &key_str) {
            // create fresh
        }

        let mut entry = state
            .map
            .pop(&key_str)
            .unwrap_or(Entry { value: Value::Bytes(Arc::new(b"{}".to_vec())), expires_at_ms: None });

        let mut json = try_parse_json_from_entry(&entry).unwrap_or(JsonValue::Object(Default::default()));
        let ok = json_set_at_path(&mut json, &tokens, new_val);
        if !ok {
            // restore old entry
            put_entry_with_lru(&mut state, key_str, entry);
            return 0;
        }

        let updated_bytes = match serde_json::to_vec(&json) {
            Ok(b) => b,
            Err(_) => {
                put_entry_with_lru(&mut state, key_str, entry);
                return 0;
            }
        };

        entry.value = Value::Bytes(Arc::new(updated_bytes.clone()));
        put_entry_with_lru(&mut state, key_str.clone(), entry);

        // AOF logs as SET of the full updated JSON document.
        aof_write_set(&key_str, &updated_bytes);
        1
    })
}

// --- Phase4: Secondary indexing + Find ---

#[no_mangle]
pub extern "C" fn cache_index_create_numeric(field: *const c_char) -> i32 {
    ffi_guard("cache_index_create_numeric", 0, || {
        let field_str = unsafe { to_string(field) };
        if field_str.is_empty() {
            return 0;
        }

        // Walk every shard (read-only) and collect the index entries.
        // We hold one shard lock at a time to avoid blocking the world.
        let mut idx_map = BTreeMap::<i64, HashSet<String>>::new();
        for shard_lock in &CACHE.shards {
            let shard = shard_lock.read().unwrap();
            for (k, v) in shard.map.iter() {
                if let Some(json) = try_parse_json_from_entry(v) {
                    if let Some(num) = extract_numeric_field(&json, &field_str) {
                        idx_map.entry(num).or_default().insert(k.clone());
                    }
                }
            }
        }
        let mut indexes = CACHE.indexes.write().unwrap();
        indexes.insert(field_str, idx_map);
        1
    })
}

fn parse_find_query(q: &str) -> Option<(String, String, String)> {
    // very small grammar: <field> <op> <value>
    // op: > >= < <= ==
    let s = q.trim();
    if s.is_empty() {
        return None;
    }
    let parts: Vec<&str> = s.split_whitespace().collect();
    if parts.len() < 3 {
        return None;
    }
    Some((parts[0].to_string(), parts[1].to_string(), parts[2..].join(" ")))
}

#[no_mangle]
pub extern "C" fn cache_find(query: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_find", std::ptr::null_mut(), || {
        let query_str = unsafe { to_string(query) };
        let Some((field, op, value_str)) = parse_find_query(&query_str) else {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        };

        let value_num: Option<i64> = value_str.parse::<i64>().ok();
        let mut keys: Vec<String> = Vec::new();

        let indexes = CACHE.indexes.read().unwrap();
        let indexed_hit = value_num.zip(indexes.get(&field));

        if let Some((vnum, idx)) = indexed_hit {
            match op.as_str() {
                ">" => {
                    for (_k, set) in idx.range((vnum + 1)..) {
                        keys.extend(set.iter().cloned());
                    }
                }
                ">=" => {
                    for (_k, set) in idx.range(vnum..) {
                        keys.extend(set.iter().cloned());
                    }
                }
                "<" => {
                    for (_k, set) in idx.range(..vnum) {
                        keys.extend(set.iter().cloned());
                    }
                }
                "<=" => {
                    for (_k, set) in idx.range(..=vnum) {
                        keys.extend(set.iter().cloned());
                    }
                }
                "==" => {
                    if let Some(set) = idx.get(&vnum) {
                        keys.extend(set.iter().cloned());
                    }
                }
                _ => {}
            }
            drop(indexes);
        } else {
            drop(indexes);
            // Fallback scan walks every shard.
            for shard_lock in &CACHE.shards {
                let shard = shard_lock.read().unwrap();
                for (k, entry) in shard.map.iter() {
                    if is_expired(entry) {
                        continue;
                    }
                    let Some(json) = try_parse_json_from_entry(entry) else { continue; };
                    let Some(num) = extract_numeric_field(&json, &field) else { continue; };
                    let ok = match (op.as_str(), value_num) {
                        (">", Some(v)) => num > v,
                        (">=", Some(v)) => num >= v,
                        ("<", Some(v)) => num < v,
                        ("<=", Some(v)) => num <= v,
                        ("==", Some(v)) => num == v,
                        _ => false,
                    };
                    if ok {
                        keys.push(k.clone());
                    }
                }
            }
        }

        // Serialize keys: [Count u32][KeyLen u32][Key bytes]...
        let mut flat = Vec::new();
        flat.extend_from_slice(&(keys.len() as u32).to_le_bytes());
        for k in keys {
            let b = k.as_bytes();
            flat.extend_from_slice(&(b.len() as u32).to_le_bytes());
            flat.extend_from_slice(b);
        }
        prepare_return(flat, out_len)
    })
}

// --- Phase4: Lightweight scripting (very small command set) ---

#[no_mangle]
pub extern "C" fn cache_eval(script: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_eval", std::ptr::null_mut(), || {
        let s = unsafe { to_string(script) };
        let s = s.trim();
        if s.is_empty() {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        }

        // Commands:
        // GET <key>
        // SET <key> <value...> (value is treated as UTF-8 bytes)
        // DEL <key>
        // JSON.GET <key> <path>
        // JSON.SET <key> <path> <jsonValue>
        let mut parts = s.split_whitespace();
        let cmd = parts.next().unwrap_or("").to_uppercase();

        match cmd.as_str() {
            "GET" => {
                let key = parts.next().unwrap_or("");
                if key.is_empty() {
                    unsafe { *out_len = 0 };
                    return std::ptr::null_mut();
                }
                // reuse cache_get via CString
                let ckey = std::ffi::CString::new(key).ok();
                if let Some(ck) = ckey {
                    return cache_get(ck.as_ptr(), out_len);
                }
                unsafe { *out_len = 0 };
                std::ptr::null_mut()
            }
            "SET" => {
                let key = parts.next().unwrap_or("");
                if key.is_empty() {
                    unsafe { *out_len = 0 };
                    return std::ptr::null_mut();
                }
                // remaining bytes after the key
                let value_pos = s.find(key).unwrap_or(0) + key.len();
                let value_str = s[value_pos..].trim();
                let bytes = value_str.as_bytes().to_vec();
                let key_owned = key.to_string();
                let shard_idx = shard_for_str(&key_owned);
                let mut state = CACHE.shards[shard_idx].write().unwrap();
                put_entry_with_lru(
                    &mut state,
                    key_owned,
                    Entry { value: Value::Bytes(Arc::new(bytes.clone())), expires_at_ms: None },
                );
                aof_write_set(key, &bytes);
                prepare_return(b"OK".to_vec(), out_len)
            }
            "DEL" => {
                let key = parts.next().unwrap_or("");
                if key.is_empty() {
                    unsafe { *out_len = 0 };
                    return std::ptr::null_mut();
                }
                let key_owned = key.to_string();
                let shard_idx = shard_for_str(&key_owned);
                let mut state = CACHE.shards[shard_idx].write().unwrap();
                let existed = state.map.contains(&key_owned);
                apply_remove_internal(&mut state, &key_owned);
                aof_write_remove(key);
                let out = if existed { b"1" } else { b"0" };
                prepare_return(out.to_vec(), out_len)
            }
            "JSON.GET" => {
                let key = parts.next().unwrap_or("");
                let path = parts.next().unwrap_or("");
                if key.is_empty() || path.is_empty() {
                    unsafe { *out_len = 0 };
                    return std::ptr::null_mut();
                }
                let ckey = std::ffi::CString::new(key).ok();
                let cpath = std::ffi::CString::new(path).ok();
                if let (Some(ck), Some(cp)) = (ckey, cpath) {
                    return cache_json_get(ck.as_ptr(), cp.as_ptr(), out_len);
                }
                unsafe { *out_len = 0 };
                std::ptr::null_mut()
            }
            "JSON.SET" => {
                let key = parts.next().unwrap_or("");
                let path = parts.next().unwrap_or("");
                if key.is_empty() || path.is_empty() {
                    unsafe { *out_len = 0 };
                    return std::ptr::null_mut();
                }
                // remaining after path
                let path_pos = s.find(path).unwrap_or(0) + path.len();
                let json_str = s[path_pos..].trim();
                let ckey = std::ffi::CString::new(key).ok();
                let cpath = std::ffi::CString::new(path).ok();
                if let (Some(ck), Some(cp)) = (ckey, cpath) {
                    let ok = cache_json_set(ck.as_ptr(), cp.as_ptr(), json_str.as_ptr(), json_str.len());
                    let out = if ok != 0 { b"1" } else { b"0" };
                    return prepare_return(out.to_vec(), out_len);
                }
                prepare_return(b"0".to_vec(), out_len)
            }
            _ => {
                unsafe { *out_len = 0 };
                std::ptr::null_mut()
            }
        }
    })
}

#[no_mangle]
pub extern "C" fn cache_get_b(key: *const c_uchar, key_len: usize, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_get_b", std::ptr::null_mut(), || {
        if out_len.is_null() {
            return std::ptr::null_mut();
        }
        let key_vec = unsafe { to_bytes(key, key_len) };
        let shard_idx = shard_for_b(&key_vec);
        let mut state = CACHE.shards[shard_idx].write().unwrap();

        if maybe_remove_if_expired_b(&mut state, &key_vec) {
            unsafe { *out_len = 0 };
            return std::ptr::null_mut();
        }

        if let Some(entry) = state.map_b.get(&key_vec) {
            if let Value::Bytes(val) = &entry.value {
                return prepare_return((**val).clone(), out_len);
            }
        }

        unsafe { *out_len = 0 };
        std::ptr::null_mut()
    })
}

#[no_mangle]
pub extern "C" fn cache_get_into_b(key: *const c_uchar, key_len: usize, dst: *mut c_uchar, dst_len: usize) -> i64 {
    ffi_guard("cache_get_into_b", -1, || {
        let key_vec = unsafe { to_bytes(key, key_len) };
        if key_vec.is_empty() {
            return -1;
        }

        let shard_idx = shard_for_b(&key_vec);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        if maybe_remove_if_expired_b(&mut state, &key_vec) {
            return -1;
        }

        let Some(entry) = state.map_b.get(&key_vec) else {
            return -1;
        };
        let Value::Bytes(val) = &entry.value else {
            return -1;
        };

        let value_len = val.len();
        if value_len == 0 {
            return 0;
        }
        if dst.is_null() || dst_len < value_len {
            return -(value_len as i64);
        }

        unsafe {
            std::ptr::copy_nonoverlapping(val.as_ptr(), dst, value_len);
        }
        value_len as i64
    })
}

/// Read-only variant of `cache_get_into_b`. Takes the shard *read* lock
/// instead of the write lock, so any number of concurrent peeks against
/// the same shard run in parallel. Does NOT update the LRU recency
/// position — useful when callers explicitly don't care about LRU
/// promotion on reads, but do care about read throughput.
///
/// Same return contract as `cache_get_into_b`:
///   -1 → key missing or expired or not a Bytes value
///   <0 → buffer too small; required length is -ret
///   >0 → bytes written
///    0 → value exists but is empty
#[no_mangle]
pub extern "C" fn cache_peek_into_b(
    key: *const c_uchar,
    key_len: usize,
    dst: *mut c_uchar,
    dst_len: usize,
) -> i64 {
    ffi_guard("cache_peek_into_b", -1, || {
        let key_vec = unsafe { to_bytes(key, key_len) };
        if key_vec.is_empty() {
            return -1;
        }

        let shard_idx = shard_for_b(&key_vec);
        let state = CACHE.shards[shard_idx].read().unwrap();

        let Some(entry) = state.map_b.peek(&key_vec) else {
            return -1;
        };
        // Skip expired entries lazily — we can't evict under a read
        // lock; the expiry reaper or the next mutating call will tidy
        // it up. Treat as miss.
        if is_expired(entry) {
            return -1;
        }
        let Value::Bytes(val) = &entry.value else {
            return -1;
        };

        let value_len = val.len();
        if value_len == 0 {
            return 0;
        }
        if dst.is_null() || dst_len < value_len {
            return -(value_len as i64);
        }

        unsafe {
            std::ptr::copy_nonoverlapping(val.as_ptr(), dst, value_len);
        }
        value_len as i64
    })
}

// Zero-copy value lease for binary keys.
//
// Returns: opaque handle (must be freed via cache_bytes_lease_free), or null if missing/expired.
// Out params: (*out_ptr, *out_len) are set to the value bytes.
#[no_mangle]
pub extern "C" fn cache_get_lease_b(
    key: *const c_uchar,
    key_len: usize,
    out_ptr: *mut *const c_uchar,
    out_len: *mut usize,
) -> *const Vec<u8> {
    ffi_guard("cache_get_lease_b", std::ptr::null(), || {
        if out_ptr.is_null() || out_len.is_null() {
            return std::ptr::null();
        }

        let key_vec = unsafe { to_bytes(key, key_len) };
        if key_vec.is_empty() {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        }

        let shard_idx = shard_for_b(&key_vec);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        if maybe_remove_if_expired_b(&mut state, &key_vec) {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        }

        let Some(entry) = state.map_b.get(&key_vec) else {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        };

        let Value::Bytes(val) = &entry.value else {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        };

        let handle = Arc::into_raw(val.clone());
        unsafe {
            *out_ptr = (*handle).as_ptr();
            *out_len = (*handle).len();
        }
        handle
    })
}

/// Read-only zero-copy variant of `cache_get_lease_b`. Holds the shard
/// *read* lock for the duration of the lookup; releases it before
/// returning (the lease itself is just an Arc bump, not a lock).
/// Like `cache_peek_into_b`, this skips LRU promotion in exchange for
/// scalable concurrent reads.
#[no_mangle]
pub extern "C" fn cache_peek_lease_b(
    key: *const c_uchar,
    key_len: usize,
    out_ptr: *mut *const c_uchar,
    out_len: *mut usize,
) -> *const Vec<u8> {
    ffi_guard("cache_peek_lease_b", std::ptr::null(), || {
        if out_ptr.is_null() || out_len.is_null() {
            return std::ptr::null();
        }

        let key_vec = unsafe { to_bytes(key, key_len) };
        if key_vec.is_empty() {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        }

        let shard_idx = shard_for_b(&key_vec);
        let state = CACHE.shards[shard_idx].read().unwrap();
        let Some(entry) = state.map_b.peek(&key_vec) else {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        };
        if is_expired(entry) {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        }
        let Value::Bytes(val) = &entry.value else {
            unsafe {
                *out_ptr = std::ptr::null();
                *out_len = 0;
            }
            return std::ptr::null();
        };

        let handle = Arc::into_raw(val.clone());
        unsafe {
            *out_ptr = (*handle).as_ptr();
            *out_len = (*handle).len();
        }
        handle
    })
}

#[no_mangle]
pub extern "C" fn cache_bytes_lease_free(handle: *const Vec<u8>) {
    ffi_guard("cache_bytes_lease_free", (), || {
        if handle.is_null() {
            return;
        }
        unsafe {
            // Drop one Arc refcount.
            let _ = Arc::from_raw(handle);
        }
    })
}

#[no_mangle]
pub extern "C" fn cache_remove_b(key: *const c_uchar, key_len: usize) {
    ffi_guard("cache_remove_b", (), || {
        let key_vec = unsafe { to_bytes(key, key_len) };
        let shard_idx = shard_for_b(&key_vec);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
        apply_remove_internal_b(&mut state, &key_vec);
        aof_write_remove_b(&key_vec);
    })
}

// --- Phase3: Pub/Sub ---

#[no_mangle]
pub extern "C" fn cache_pubsub_subscribe(channel: *const c_char) -> u64 {
    ffi_guard("cache_pubsub_subscribe", 0, || {
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
    })
}

#[no_mangle]
pub extern "C" fn cache_pubsub_unsubscribe(sub_id: u64) {
    ffi_guard("cache_pubsub_unsubscribe", (), || {
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
    })
}

#[no_mangle]
pub extern "C" fn cache_pubsub_publish(channel: *const c_char, payload: *const c_uchar, len: usize) -> u64 {
    ffi_guard("cache_pubsub_publish", 0, || {
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
    })
}

#[no_mangle]
pub extern "C" fn cache_pubsub_poll(sub_id: u64, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_pubsub_poll", std::ptr::null_mut(), || {
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
    })
}

// --- Phase3: Keyspace notifications polling ---

#[no_mangle]
pub extern "C" fn cache_notifications_poll(out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_notifications_poll", std::ptr::null_mut(), || {
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
    })
}

#[no_mangle]
pub extern "C" fn cache_notifications_clear() {
    ffi_guard("cache_notifications_clear", (), || {
        let mut q = NOTIFY_QUEUE.lock().unwrap();
        q.clear();
    })
}

// --- Phase3: Streams ---

#[no_mangle]
pub extern "C" fn cache_xadd(key: *const c_char, payload: *const c_uchar, len: usize) -> u64 {
    ffi_guard("cache_xadd", 0, || {
        let key_str = unsafe { to_string(key) };
        if key_str.is_empty() {
            return 0;
        }
        let payload_vec = unsafe { to_bytes(payload, len) };

        let id = STREAM_ID.fetch_add(1, Ordering::Relaxed);

        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
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
    })
}

#[no_mangle]
pub extern "C" fn cache_xrange(key: *const c_char, start_id: u64, end_id: u64, out_len: *mut usize) -> *mut c_uchar {
    ffi_guard("cache_xrange", std::ptr::null_mut(), || {
        let key_str = unsafe { to_string(key) };
        let shard_idx = shard_for_str(&key_str);
        let mut state = CACHE.shards[shard_idx].write().unwrap();
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
    })
}
