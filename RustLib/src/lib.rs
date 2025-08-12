use std::collections::HashMap;
use std::ffi::{CStr};
use std::os::raw::{c_char, c_uchar};
use std::sync::RwLock;
use once_cell::sync::Lazy;


static CACHE: Lazy<RwLock<HashMap<String, Vec<u8>>>> = Lazy::new(|| {
    RwLock::new(HashMap::new())
});

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
        CStr::from_ptr(key).to_string_lossy().into_owned()
    };

    let val_slice = unsafe { std::slice::from_raw_parts(value, len) };

    let mut map = CACHE.write().unwrap();
    map.insert(key_str, val_slice.to_vec());
}

#[no_mangle]
pub extern "C" fn cache_get(key: *const c_char, out_len: *mut usize) -> *const c_uchar {
    if key.is_null() {
        return std::ptr::null();
    }

    let key_str = unsafe {
        CStr::from_ptr(key).to_string_lossy().into_owned()
    };

    let map = CACHE.read().unwrap();
    if let Some(val) = map.get(&key_str) {
        unsafe {
            *out_len = val.len();
        }

        let ptr = val.as_ptr();
        // ⚠️ Diqqat: bu pointer faqat o‘qish uchun!
        ptr
    } else {
        unsafe {
            *out_len = 0;
        }
        std::ptr::null()
    }
}

#[no_mangle]
pub extern "C" fn cache_remove(key: *const c_char) {
    if key.is_null() {
        return;
    }

    let key_str = unsafe {
        CStr::from_ptr(key).to_string_lossy().into_owned()
    };

    let mut map = CACHE.write().unwrap();
    map.remove(&key_str);
    flush_memory();
}

#[no_mangle]
pub extern "C" fn cache_clear_all() {
    let mut map = CACHE.write().unwrap();
    *map = HashMap::new();
    flush_memory();
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
