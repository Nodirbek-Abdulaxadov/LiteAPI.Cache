use std::collections::HashMap;
use std::ffi::CStr;
use std::os::raw::{c_char, c_uchar};
use std::sync::RwLock;
use once_cell::sync::Lazy;


static CACHE: Lazy<RwLock<HashMap<String, Vec<u8>>>> = Lazy::new(|| {
    RwLock::new(HashMap::new())
});

// Run trimming off the hot path to avoid blocking under lock
fn trim_memory_async() {
    #[cfg(any(target_os = "windows", target_os = "linux"))]
    {
        std::thread::spawn(|| {
            flush_memory();
        });
    }
}

#[no_mangle]
pub extern "C" fn cache_init() {
    let _unused = CACHE.read().unwrap();
}

#[no_mangle]
pub extern "C" fn cache_set(key: *const c_char, value: *const c_uchar, len: usize) {
    if key.is_null() || value.is_null() || len == 0 {
        return;
    }

    let key_str = unsafe {
        match CStr::from_ptr(key).to_str() {
            Ok(s) => s.to_owned(),
            Err(_) => return,
        }
    };

    let val_slice = unsafe { std::slice::from_raw_parts(value, len) };

    let mut map = CACHE.write().unwrap();
    map.insert(key_str, val_slice.to_vec());
}

#[no_mangle]
pub extern "C" fn cache_get(key: *const c_char, out_len: *mut usize) -> *mut c_uchar {
    if key.is_null() || out_len.is_null() {
        return std::ptr::null_mut();
    }

    let key_str = unsafe {
        match CStr::from_ptr(key).to_str() {
            Ok(s) => s.to_owned(),
            Err(_) => return std::ptr::null_mut(),
        }
    };

    let map = CACHE.read().unwrap();
    if let Some(val) = map.get(&key_str) {
        let mut buf = val.clone();
        let len = buf.len();
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

    let key_str = unsafe {
        match CStr::from_ptr(key).to_str() {
            Ok(s) => s.to_owned(),
            Err(_) => return,
        }
    };

    let mut map = CACHE.write().unwrap();
    map.remove(&key_str);
    // Avoid trimming on every remove; too expensive
}

#[no_mangle]
pub extern "C" fn cache_clear_all() {
    // Swap out the map in O(1) and drop outside the lock
    let old_map = {
        let mut guard = CACHE.write().unwrap();
        std::mem::take(&mut *guard)
    };
    drop(old_map);
    // Optionally trim asynchronously
    trim_memory_async();
}

#[no_mangle]
pub extern "C" fn cache_free(ptr: *mut c_uchar, len: usize) {
    if ptr.is_null() || len == 0 {
        return;
    }
    unsafe {
        let _ = Vec::from_raw_parts(ptr, len, len);
        // drop here to free
    }
}

#[cfg(target_os = "windows")]
pub fn flush_memory() {
    use std::os::raw::c_void;

    #[link(name = "kernel32")]
    extern "system" {
        fn GetCurrentProcess() -> *mut c_void;
        fn SetProcessWorkingSetSize(
            hProcess: *mut c_void,
            dwMinimumWorkingSetSize: usize,
            dwMaximumWorkingSetSize: usize,
        ) -> i32;
    }

    unsafe {
        let handle = GetCurrentProcess();
        SetProcessWorkingSetSize(handle, usize::MAX, usize::MAX);
    }
}

#[cfg(target_os = "linux")]
pub fn flush_memory() {
    use libc;
    unsafe {
        libc::malloc_trim(0);
    }
}
