# URI Percent-Encoding Rules (consolidated)

A single-page reference for percent-encoding the URI types this project handles:
`http`, `https`, and `urn`. The rules below are gathered from RFC 3986 (generic
URI syntax) and RFC 8141 (URN syntax) so you don't have to cross-reference
multiple RFCs to answer one practical question:

> **Does my platform's `URLEncode` / `quote` / `EscapeDataString` produce the
> bytes the spec requires?**

Run your encoder on a test string, compare against the per-component tables in
§3 and §4, and check the four invariants in §5. The platform cheat sheet in §6
lists the common gotchas.

> **Note on this project's loader.** The loader pre-filters reject any URI that
> contains `%` or `;` or any non-ASCII byte (see `canonical-nodeid-encoding.md`).
> Those are *fast guards*, not the encoding spec — OPC UA NamespaceUris are opaque
> equality strings that in practice never need encoding, and the guard keeps the
> `nsu=URI;identifier` boundary unambiguous. This document is the full rule set you
> apply *before* a URI reaches that guard, and the reference for any implementation
> that must produce spec-correct encoding in another language or platform.

---

## 1. Character classes (RFC 3986 §2.2, §2.3)

| Class | Members |
|---|---|
| **unreserved** | `A`–`Z` `a`–`z` `0`–`9` `-` `.` `_` `~` |
| **gen-delims** | `:` `/` `?` `#` `[` `]` `@` |
| **sub-delims** | `!` `$` `&` `'` `(` `)` `*` `+` `,` `;` `=` |
| **reserved** | gen-delims ∪ sub-delims |
| **pct-encoded** | `%` HEXDIG HEXDIG |

**`pchar`** (the "path character" set, reused by URN) =
`unreserved / pct-encoded / sub-delims / ":" / "@"`.

Two rules that hold in **every** component:

1. **unreserved characters MUST NOT be percent-encoded.** If your encoder emits
   `%7E` for `~` or `%2D` for `-`, it is over-encoding. Decoders are required to
   normalize such escapes back to the literal character (RFC 3986 §6.2.2.2), so
   over-encoding also breaks byte-for-byte comparison.
2. **`%` is only ever legal as the start of a `%HH` triplet.** A literal percent
   sign in data MUST be encoded as `%25`.

---

## 2. The encoding operation (RFC 3986 §2.1, §2.4, §2.5)

* A percent-encoded octet is `%` followed by **two uppercase** hex digits
  representing **one byte**: `%2F`, not `%2f`.
  Uppercase is the normal form (RFC 3986 §6.2.2.1); for deterministic comparison
  this project treats uppercase as **required**.
* **Non-ASCII characters:** encode the character to **UTF-8 first**, then
  percent-encode each resulting byte (RFC 3986 §2.5). Example: `é` (U+00E9) →
  UTF-8 `C3 A9` → `%C3%A9`. (IRIs per RFC 3987 may carry raw non-ASCII; plain
  URIs may not — and this project requires URIs, i.e. pure ASCII.)
* Encode a character when it is **data** but the component grammar would
  otherwise read it as a delimiter, or when it is outside the component's
  allowed set. Do **not** encode a reserved character that you intend *as* a
  delimiter (e.g. the `/` between path segments).

---

## 3. `http` / `https` (RFC 3986 §3, RFC 7230 §2.7)

```
http-URI = scheme "://" [ userinfo "@" ] host [ ":" port ]
                  path [ "?" query ] [ "#" fragment ]
```

| Component | Allowed **un**encoded | MUST percent-encode (when data) |
|---|---|---|
| **scheme** | fixed `http` / `https` (lowercase) | — (no other chars permitted) |
| **userinfo** | unreserved, sub-delims, `:` | `@` → `%40`, `/` `?` `#` `[` `]`, space, non-ASCII, controls |
| **host** (reg-name) | unreserved, sub-delims | gen-delims, space, non-ASCII, controls (IPv6 uses the `[...]` IP-literal form) |
| **port** | `0`–`9` | everything else |
| **path** segment | unreserved, sub-delims, `:` `@` | `/` *as data* → `%2F`, `?` `#` `[` `]`, space → `%20`, non-ASCII, controls |
| **query** | unreserved, sub-delims, `:` `@` `/` `?` | `#` `[` `]`, space, non-ASCII, controls |
| **fragment** | unreserved, sub-delims, `:` `@` `/` `?` | `#` (only one starts the fragment), `[` `]`, space, non-ASCII |

**Watch-outs:**

* **`+` and space.** RFC 3986 has no special meaning for `+`; space is `%20`.
  The `application/x-www-form-urlencoded` convention (HTML form bodies, many
  query strings) instead uses `+` for space and `%2B` for a literal `+`. These
  are *different* encodings. If you mean a literal `+` in a path or query, emit
  `%2B`; if you mean a space, emit `%20` for RFC 3986 contexts.
* **Case normalization.** Scheme and host are case-insensitive and customarily
  lowercased; path, query, and fragment are case-**sensitive**.
* **sub-delims are pass-through.** `!$&'()*+,;=` are legal unencoded in most
  components. Encode them only if your application needs them as data with a
  meaning distinct from their delimiter role.

---

## 4. `urn` (RFC 8141 §2)

```
namestring    = "urn" ":" NID ":" NSS
                [ "?+" r-component ] [ "?=" q-component ] [ "#" f-component ]
NID           = alphanum 0*30(alphanum / "-") alphanum      ; 2–32 chars
NSS           = pchar *( pchar / "/" )
```

| Component | Allowed **un**encoded | MUST percent-encode (when data) |
|---|---|---|
| **`urn`** | fixed literal | — |
| **NID** | `A`–`Z` `a`–`z` `0`–`9` `-` (not leading/trailing `-`) | nothing else is permitted at all |
| **NSS** | unreserved, sub-delims, `:` `@` `/` | space → `%20`, non-ASCII (UTF-8→`%HH`), controls, literal `%` → `%25`, `#` → `%23`, and any `?` that would form `?+`/`?=` → `%3F` (safest: encode every literal `?` as `%3F`) |
| **r-/q-component** | pchar, `/`, `?` | `#`, space, non-ASCII, controls |
| **f-component** | same set as a URI fragment (§3) | as for fragment |

**URN equivalence differs from http** (RFC 8141 §3.1) — important when you compare
or dedupe URNs:

* `urn` and **NID** are **case-insensitive** (compare lowercased).
* Hex digits inside a `%HH` triplet are case-insensitive → normalize to uppercase.
* **The NSS is case-sensitive, and percent-encoded octets are NOT decoded for
  comparison.** Unlike http (RFC 3986 §6.2.2.2), a URN does **not** treat
  `%41` as equal to `A`. So: leave unreserved characters unencoded (rule §1.1),
  but do **not** rely on a decoder folding `%`-escaped unreserved characters back
  — in URN space they are distinct.

---

## 5. The four invariants a conformant encoder must satisfy

1. **Uppercase hex** in every `%HH` (`%3B`, never `%3b`).
2. **unreserved never encoded** (`A`–`Z` `a`–`z` `0`–`9` `-` `.` `_` `~`).
3. **Non-ASCII → UTF-8 → `%HH` per byte.**
4. **`%` literal → `%25`**, and (for the OPC UA NodeId text form specifically)
   **`;` → `%3B`** inside the namespace/server URI so the `nsu=URI;id` boundary
   stays unambiguous.

If all four hold and the per-component column in §3/§4 is respected, the output
is spec-correct.

---

## 6. Platform encoder cheat sheet

These are the common standard-library functions and where they diverge from the
RFC 3986 component rules. **Verify, don't assume** — behavior shifts between
versions.

| Platform / function | Space | Keeps unencoded | Caveats |
|---|---|---|---|
| **.NET** `Uri.EscapeDataString` | `%20` | unreserved (`-._~`) | RFC 3986-correct for a single component; uppercase hex. Prefer this. `EscapeUriString` is deprecated. |
| **JS** `encodeURIComponent` | `%20` | `A–Za–z0–9 - _ . ! ~ * ' ( )` | Leaves a few sub-delims (`!*'()`) unencoded; encodes all gen-delims. Good for one component. Uppercase hex. |
| **JS** `encodeURI` | `%20` | unreserved **and** reserved | For a whole URI, not a component — won't encode `/ ? # : @` etc. |
| **Java** `URLEncoder.encode` | **`+`** | `A–Za–z0–9 - _ . *` | `application/x-www-form-urlencoded`, **not** RFC 3986. Post-process: `+`→`%20`, `*`→`%2A`. Uppercase hex. |
| **Python** `urllib.parse.quote` | `%20` | unreserved + `safe` (default `/`) | Set `safe=''` to encode `/` too. Uppercase hex. |
| **Python** `urllib.parse.quote_plus` | **`+`** | unreserved | Form encoding; same `+` caveat as Java. |
| **Go** `url.QueryEscape` | **`+`** | unreserved | Form encoding. For path components use `url.PathEscape` (space→`%20`). |

**The two failure modes to test for first:**

1. **Space as `+`** instead of `%20` (Java `URLEncoder`, Python `quote_plus`,
   Go `QueryEscape`). Wrong for RFC 3986 path/query-as-data.
2. **Over-encoding `~`** (and historically `!*'()`). Some older encoders emit
   `%7E`; the spec forbids encoding unreserved characters.

A quick conformance probe: encode the string `a b~c/d` as *component data*
(i.e. `/` is data, not a separator). RFC 3986-correct output is
`a%20b~c%2Fd` — space→`%20`, `~` left alone, `/` as data→`%2F`, uppercase hex.
Failure signatures: `a+b...` (space encoded as `+` — form encoding), `...%7Ec...`
(`~` over-encoded — unreserved must be left alone), or lowercase `%2f`.

The sub-delims (`!$&'()*+,;=`) are deliberately **not** in this probe: RFC 3986
permits them either way inside a path/query, so encoders legitimately differ
(`encodeURIComponent` and `EscapeDataString` encode `;`→`%3B`; the raw grammar
allows a bare `;`). When you need a specific sub-delim behavior — such as this
project's `;`→`%3B` invariant (§5.4) for the `nsu=URI;id` boundary — test that
character explicitly rather than assuming the encoder's default.
