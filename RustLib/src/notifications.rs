//! Keyspace notifications — expired / evicted events.
//!
//! Producers (`notify_expired`, `notify_evicted`) push into a global
//! FIFO; consumers drain it via `cache_notifications_poll`. Cheap
//! enough to keep inline so it doesn't need its own thread.

use std::collections::VecDeque;
use std::sync::Mutex;

use once_cell::sync::Lazy;

use crate::now_ms;

pub(crate) const NOTIFY_KIND_EXPIRED: u8 = 1;
pub(crate) const NOTIFY_KIND_EVICTED: u8 = 2;

#[derive(Clone)]
pub(crate) struct NotifyEvent {
    pub(crate) kind: u8,
    pub(crate) key: String,
    pub(crate) at_ms: u64,
}

pub(crate) static NOTIFY_QUEUE: Lazy<Mutex<VecDeque<NotifyEvent>>> =
    Lazy::new(|| Mutex::new(VecDeque::new()));

pub(crate) fn notify_expired(key: &str) {
    let mut q = NOTIFY_QUEUE.lock().unwrap();
    q.push_back(NotifyEvent {
        kind: NOTIFY_KIND_EXPIRED,
        key: key.to_string(),
        at_ms: now_ms(),
    });
}

pub(crate) fn notify_evicted(key: &str) {
    let mut q = NOTIFY_QUEUE.lock().unwrap();
    q.push_back(NotifyEvent {
        kind: NOTIFY_KIND_EVICTED,
        key: key.to_string(),
        at_ms: now_ms(),
    });
}
