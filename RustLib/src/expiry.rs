//! Timing-wheel-style expiry: a min-heap keyed by `expires_at_ms`.
//!
//! Each TTL'd entry is also pushed to the heap. The expiry thread peeks
//! the earliest record, sleeps until that timestamp (capped at 1s so
//! later-scheduled earlier expiries surface promptly), then pops
//! everything that has come due. The cache write-lock is taken only
//! while reaping the validated due records.
//!
//! Heap records can become stale (key updated with a new TTL, removed,
//! or overwritten without TTL); the reaper validates each pop against
//! the actual cache state and drops mismatches. Worst-case the heap
//! holds ~2 records per TTL'd key.

use std::collections::BinaryHeap;
use std::sync::Mutex;

use once_cell::sync::Lazy;

#[derive(Eq, PartialEq)]
pub(crate) enum ExpiryKey {
    Str(String),
    Bytes(Vec<u8>),
}

#[derive(Eq, PartialEq)]
pub(crate) struct ExpiryEntry {
    pub(crate) expires_at_ms: u64,
    pub(crate) key: ExpiryKey,
}

impl Ord for ExpiryEntry {
    fn cmp(&self, other: &Self) -> std::cmp::Ordering {
        // BinaryHeap is a max-heap; reverse the comparison so the
        // *earliest* expiry sits at the top.
        other.expires_at_ms.cmp(&self.expires_at_ms)
    }
}
impl PartialOrd for ExpiryEntry {
    fn partial_cmp(&self, other: &Self) -> Option<std::cmp::Ordering> {
        Some(self.cmp(other))
    }
}

pub(crate) static EXPIRY_HEAP: Lazy<Mutex<BinaryHeap<ExpiryEntry>>> =
    Lazy::new(|| Mutex::new(BinaryHeap::new()));

pub(crate) fn schedule_expiry(key: ExpiryKey, expires_at_ms: u64) {
    if let Ok(mut heap) = EXPIRY_HEAP.lock() {
        heap.push(ExpiryEntry { expires_at_ms, key });
    }
}
