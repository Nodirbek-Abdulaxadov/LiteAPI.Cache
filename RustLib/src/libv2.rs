use std::ffi::CStr;
use std::num::NonZeroUsize;
use std::os::raw::{c_char, c_uchar};
use std::sync::{Arc, RwLock};

use lru::LruCache;
use once_cell::sync::Lazy;

// Max 10,000 entry bilan cheklangan kesh (LRU avtomatik ishlaydi)
static CACHE: Lazy<RwLock<LruCache<String, Arc<[u8]>>>> = Lazy::new(|| {
    RwLock::new(LruCache::new(NonZeroUsize::new(10_000).unwrap()))
});

#[no_mangle]
pub extern "C" fn cache_init() {
    let _ = CACHE.read().ok();
}

#[no_mangle]
pub extern "C" fn cache_set(key: *const c_char, value: *const c_uchar, len: usize) {
    if key.is_null() || value.is_null() || len == 0 {
        return;
    }

    let key = unsafe {
        match CStr::from_ptr(key).to_str() {
            Ok(s) => s.to_string(),
            Err(_) => return,
        }
    };

    let val_slice = unsafe { std::slice::from_raw_parts(value, len) };
    let val_arc: Arc<[u8]> = Arc::from(val_slice);

    let mut cache = CACHE.write().unwrap();
    cache.put(key, val_arc);
}

#[no_mangle]
pub extern "C" fn cache_get(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    if key.is_null() || out_len.is_null() {
        return std::ptr::null_mut();
    }

    let key = unsafe {
        match CStr::from_ptr(key).to_str() {
            Ok(s) => s,
            Err(_) => return std::ptr::null(),
        }
    };

    let mut cache = CACHE.write().unwrap();
    if let Some(val) = cache.get(key) {
        let len = val.len();
        let mut buf = Vec::with_capacity(len);
        buf.extend_from_slice(val);
        let ptr = buf.as_mut_ptr();
        std::mem::forget(buf);
        unsafe { *out_len = len; }
        ptr
    } else {
        unsafe {
            *out_len = 0;
        }
        std::ptr::null_mut()
    }
}

#[no_mangle]
pub extern "C" fn cache_remove(key: *const c_char) {
    if key.is_null() {
        return;
    }

    let key = unsafe {
        match CStr::from_ptr(key).to_str() {
            Ok(s) => s.to_string(),
            Err(_) => return,
        }
    };

    let mut cache = CACHE.write().unwrap();
    cache.pop(&key);
}

#[no_mangle]
pub extern "C" fn cache_clear_all() {
    let mut cache = CACHE.write().unwrap();
    cache.clear();
}

#[no_mangle]
pub extern "C" fn cache_free(ptr: *mut c_uchar, len: usize) {
    if ptr.is_null() || len == 0 {
        return;
    }
    unsafe {
        let _ = Vec::from_raw_parts(ptr, len, len);
    }
}
