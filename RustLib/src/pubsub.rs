//! Pub/Sub channel multiplexer.
//!
//! Subscribers receive a numeric id and an in-memory queue. Publishers
//! fan out a payload to every queue mapped to that channel. No
//! durability — messages live only in memory until polled.

use std::collections::{HashMap, VecDeque};
use std::sync::Mutex;

use once_cell::sync::Lazy;

#[derive(Clone)]
pub(crate) struct PubMessage {
    pub(crate) channel: String,
    pub(crate) payload: Vec<u8>,
}

pub(crate) struct PubSubState {
    pub(crate) next_id: u64,
    pub(crate) subs: HashMap<u64, String>,
    pub(crate) channels: HashMap<String, Vec<u64>>,
    pub(crate) queues: HashMap<u64, VecDeque<PubMessage>>,
}

pub(crate) static PUBSUB: Lazy<Mutex<PubSubState>> = Lazy::new(|| {
    Mutex::new(PubSubState {
        next_id: 1,
        subs: HashMap::new(),
        channels: HashMap::new(),
        queues: HashMap::new(),
    })
});
