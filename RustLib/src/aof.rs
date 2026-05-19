//! Append-Only File (AOF) — durable log of mutations.
//!
//! Format (little-endian throughout):
//!
//! ```text
//! [u8 opcode][u32 key_len][key bytes][u32 val_len][val bytes]...
//! ```
//!
//! Opcodes 1–11 are the original wire format. Opcode 12
//! (`AOF_OP_EXPIRE_AT`) was added when we realized opcode 4
//! (`AOF_OP_EXPIRE`) stored a *relative* `ttl_ms` and replay would
//! re-anchor it to now, effectively resetting every TTL on every
//! restart. Going forward we only emit opcode 12 (absolute
//! `expires_at_ms`); opcode 4 is still read for backward compatibility
//! with files written by older builds.

use std::io::{Read, Write};
use std::sync::Mutex;

use once_cell::sync::Lazy;

pub(crate) const AOF_OP_SET: u8 = 1;
pub(crate) const AOF_OP_REMOVE: u8 = 2;
pub(crate) const AOF_OP_CLEAR: u8 = 3;
pub(crate) const AOF_OP_EXPIRE: u8 = 4;
pub(crate) const AOF_OP_HSET: u8 = 5;
pub(crate) const AOF_OP_LPUSH: u8 = 6;
pub(crate) const AOF_OP_SADD: u8 = 7;
pub(crate) const AOF_OP_ZADD: u8 = 8;
pub(crate) const AOF_OP_XADD: u8 = 9;
pub(crate) const AOF_OP_SET_B: u8 = 10;
pub(crate) const AOF_OP_REMOVE_B: u8 = 11;
pub(crate) const AOF_OP_EXPIRE_AT: u8 = 12;

pub(crate) static AOF_FILE: Lazy<Mutex<Option<std::fs::File>>> =
    Lazy::new(|| Mutex::new(None));

fn aof_write(buf: &[u8]) {
    let mut guard = AOF_FILE.lock().unwrap();
    let Some(file) = guard.as_mut() else { return; };
    let _ = file.write_all(buf);
    let _ = file.flush();
}

pub(crate) fn aof_write_set(key: &str, val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + val.len());
    buf.push(AOF_OP_SET);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

pub(crate) fn aof_write_set_b(key: &[u8], val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + val.len());
    buf.push(AOF_OP_SET_B);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key);
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

pub(crate) fn aof_write_remove(key: &str) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len());
    buf.push(AOF_OP_REMOVE);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    aof_write(&buf);
}

pub(crate) fn aof_write_remove_b(key: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len());
    buf.push(AOF_OP_REMOVE_B);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key);
    aof_write(&buf);
}

pub(crate) fn aof_write_clear() {
    aof_write(&[AOF_OP_CLEAR]);
}

pub(crate) fn aof_write_expire_at(key: &str, expires_at_ms: u64) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 8);
    buf.push(AOF_OP_EXPIRE_AT);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&expires_at_ms.to_le_bytes());
    aof_write(&buf);
}

pub(crate) fn aof_write_hset(key: &str, field: &str, val: &[u8]) {
    let mut buf =
        Vec::with_capacity(1 + 4 + key.len() + 4 + field.len() + 4 + val.len());
    buf.push(AOF_OP_HSET);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(field.len() as u32).to_le_bytes());
    buf.extend_from_slice(field.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

pub(crate) fn aof_write_lpush(key: &str, val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + val.len());
    buf.push(AOF_OP_LPUSH);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

pub(crate) fn aof_write_sadd(key: &str, val: &[u8]) {
    let mut buf = Vec::with_capacity(1 + 4 + key.len() + 4 + val.len());
    buf.push(AOF_OP_SADD);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&(val.len() as u32).to_le_bytes());
    buf.extend_from_slice(val);
    aof_write(&buf);
}

pub(crate) fn aof_write_zadd(key: &str, score: f64, member: &str) {
    let mut buf =
        Vec::with_capacity(1 + 4 + key.len() + 8 + 4 + member.len());
    buf.push(AOF_OP_ZADD);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&score.to_le_bytes());
    buf.extend_from_slice(&(member.len() as u32).to_le_bytes());
    buf.extend_from_slice(member.as_bytes());
    aof_write(&buf);
}

pub(crate) fn aof_write_xadd(key: &str, id: u64, payload: &[u8]) {
    let mut buf =
        Vec::with_capacity(1 + 4 + key.len() + 8 + 4 + payload.len());
    buf.push(AOF_OP_XADD);
    buf.extend_from_slice(&(key.len() as u32).to_le_bytes());
    buf.extend_from_slice(key.as_bytes());
    buf.extend_from_slice(&id.to_le_bytes());
    buf.extend_from_slice(&(payload.len() as u32).to_le_bytes());
    buf.extend_from_slice(payload);
    aof_write(&buf);
}

// Reading primitives — used by the replay loop in lib.rs.

pub(crate) fn read_exact_u8(r: &mut impl Read) -> Option<u8> {
    let mut b = [0u8; 1];
    r.read_exact(&mut b).ok()?;
    Some(b[0])
}

pub(crate) fn read_exact_u32(r: &mut impl Read) -> Option<u32> {
    let mut b = [0u8; 4];
    r.read_exact(&mut b).ok()?;
    Some(u32::from_le_bytes(b))
}

pub(crate) fn read_exact_u64(r: &mut impl Read) -> Option<u64> {
    let mut b = [0u8; 8];
    r.read_exact(&mut b).ok()?;
    Some(u64::from_le_bytes(b))
}

pub(crate) fn read_exact_f64(r: &mut impl Read) -> Option<f64> {
    let mut b = [0u8; 8];
    r.read_exact(&mut b).ok()?;
    Some(f64::from_le_bytes(b))
}

pub(crate) fn read_exact_vec(r: &mut impl Read, len: usize) -> Option<Vec<u8>> {
    let mut b = vec![0u8; len];
    r.read_exact(&mut b).ok()?;
    Some(b)
}

pub(crate) fn read_exact_string(r: &mut impl Read, len: usize) -> Option<String> {
    let bytes = read_exact_vec(r, len)?;
    Some(String::from_utf8_lossy(&bytes).into_owned())
}
