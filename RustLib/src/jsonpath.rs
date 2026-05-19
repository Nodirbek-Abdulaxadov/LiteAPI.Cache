//! Minimal JSONPath support for `cache_json_get` / `cache_json_set`.
//!
//! Accepts dotted paths (`a.b.c`), optional leading `$`, and `[N]`
//! array indices. Not a full JSONPath implementation (no `*`, no `..`,
//! no filters). Enough for the value-shaping ops the C# wrappers offer.

use serde_json::Value as JsonValue;

#[derive(Debug)]
pub(crate) enum JsonPathToken {
    Field(String),
    Index(usize),
}

pub(crate) fn parse_json_path(path: &str) -> Option<Vec<JsonPathToken>> {
    let p = path.trim();
    if p.is_empty() {
        return None;
    }

    let mut i = 0usize;
    let chars: Vec<char> = p.chars().collect();

    if chars.first() == Some(&'$') {
        i += 1;
    }

    let mut tokens = Vec::new();

    while i < chars.len() {
        match chars[i] {
            '.' => {
                i += 1;
                let start = i;
                while i < chars.len() {
                    let c = chars[i];
                    if c == '.' || c == '[' {
                        break;
                    }
                    i += 1;
                }
                if i == start {
                    return None;
                }
                let field: String = chars[start..i].iter().collect();
                tokens.push(JsonPathToken::Field(field));
            }
            '[' => {
                i += 1;
                let start = i;
                while i < chars.len() && chars[i].is_ascii_digit() {
                    i += 1;
                }
                if i == start || i >= chars.len() || chars[i] != ']' {
                    return None;
                }
                let num_str: String = chars[start..i].iter().collect();
                i += 1; // consume ']'
                let idx = num_str.parse::<usize>().ok()?;
                tokens.push(JsonPathToken::Index(idx));
            }
            _ => {
                // allow path starting with field name without leading dot
                let start = i;
                while i < chars.len() {
                    let c = chars[i];
                    if c == '.' || c == '[' {
                        break;
                    }
                    i += 1;
                }
                if i == start {
                    return None;
                }
                let field: String = chars[start..i].iter().collect();
                tokens.push(JsonPathToken::Field(field));
            }
        }
    }

    Some(tokens)
}

pub(crate) fn json_get_at_path<'a>(
    root: &'a JsonValue,
    path: &[JsonPathToken],
) -> Option<&'a JsonValue> {
    let mut cur = root;
    for t in path {
        match t {
            JsonPathToken::Field(f) => {
                cur = cur.get(f)?;
            }
            JsonPathToken::Index(idx) => {
                cur = cur.get(*idx)?;
            }
        }
    }
    Some(cur)
}

pub(crate) fn json_set_at_path(
    root: &mut JsonValue,
    path: &[JsonPathToken],
    new_val: JsonValue,
) -> bool {
    if path.is_empty() {
        *root = new_val;
        return true;
    }

    let mut cur = root;
    for (pos, t) in path.iter().enumerate() {
        let last = pos == path.len() - 1;
        match t {
            JsonPathToken::Field(f) => {
                if last {
                    if !cur.is_object() {
                        *cur = JsonValue::Object(Default::default());
                    }
                    if let Some(obj) = cur.as_object_mut() {
                        obj.insert(f.clone(), new_val);
                        return true;
                    }
                    return false;
                }

                if !cur.is_object() {
                    *cur = JsonValue::Object(Default::default());
                }
                let obj = cur.as_object_mut().unwrap();
                cur = obj
                    .entry(f.clone())
                    .or_insert(JsonValue::Object(Default::default()));
            }
            JsonPathToken::Index(idx) => {
                if !cur.is_array() {
                    *cur = JsonValue::Array(Vec::new());
                }
                let arr = cur.as_array_mut().unwrap();
                while arr.len() <= *idx {
                    arr.push(JsonValue::Null);
                }
                if last {
                    arr[*idx] = new_val;
                    return true;
                }
                cur = &mut arr[*idx];
            }
        }
    }
    false
}
