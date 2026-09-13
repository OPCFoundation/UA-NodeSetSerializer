# Canonical NodeId Encoding

This document specifies the deterministic text encoding used by this project
for OPC UA `NodeId` and `ExpandedNodeId` values when they appear as IRIs
(for example in JSON-LD `@id` fields) or anywhere else two textual NodeIds
must compare byte-equal iff they denote the same node.

It is a strict subset of the encoding described in OPC UA Part 6
§5.1.12 ("QualifiedName, NodeId and ExpandedNodeId String Encoding").
Part 6 says "URI percent-encoding as defined in IETF RFC 3986" but does not
pin down the encoding completely; this document fills the gap by constraining
the input character set so that the canonical text form of a NodeId is always
unambiguous and byte-identical for byte-identical inputs.

## 1. Definitions

**Valid URI.** A string that:

1. Is a syntactically valid URI per IETF RFC 3986, AND
2. Has scheme `http`, `https`, or `urn`, AND
3. Has only well-formed percent-encoding — every `%` (U+0025) is followed by two
   hex digits (`%HH`); a bare or truncated `%` is rejected, AND
4. Contains no `;` (U+003B) character, AND
5. Is pure ASCII (no control characters, no bytes above U+007E).

The `;` ban makes the canonical NodeId syntax `nsu=URI;identifier`
unambiguous without escaping: the first `;` after `nsu=` (or `svu=`)
is always the identifier boundary. A literal `;` inside a NamespaceUri is
normalized to `__%3B` at the import boundary so it does not collide with the
delimiter (deferred; see the checkout/checkin work).

The editor never *adds* percent-encoding: URIs are expected to arrive already
correctly encoded, and they are stored and compared verbatim (opaque equality).
Validation only rejects *under-encoding* — a `%` that is not a valid `%HH` escape.

**Identifier.** The value portion of `i=`, `s=`, `g=`, or `b=` in a NodeId.
Identifiers are emitted verbatim and are *not* subject to URI encoding under
this spec. (Their internal grammar is governed by OPC UA Part 6 §5.1.12.)

## 2. Canonical NodeId and ExpandedNodeId

The canonical text form of a `NodeId` is the form defined by OPC UA Part 6
§5.1.12, with the URI constraint above:

* `<namespace-uri>` and `<server-uri>` productions (`nsu=URI`, `svu=URI`)
  MUST contain a Valid URI per §1. No percent-encoding is applied —
  the URI is written verbatim.

* If the NamespaceIndex is 0 or the NamespaceUri is
  `http://opcfoundation.org/UA/`, the NodeId MUST use the bare
  `<identifier>` form (no `ns=`, no `nsu=`).

* An ExpandedNodeId that is not specific to a Server MUST use the bare
  `<node-id>` form (no `svu=`, no `svr=`).

* Numeric identifiers use `i=<digits>`. String identifiers use
  `s=<unicode>` (no control characters). GUIDs use `g=<rfc-4122-form>`.
  Opaque identifiers use `b=<base64>` with standard RFC 4648 §4 base64
  (with `=` padding, no whitespace, no line breaks).

## 3. Properties Guaranteed

* **Determinism.** Identical input bytes produce identical canonical output.
* **Comparability.** Two canonical NodeIds compare byte-for-byte.
* **No hex-case ambiguity.** No `%` escapes are ever produced.
* **No per-component RFC 3986 reasoning required.** No characters are
  transformed; the URI is written exactly as authored.
* **No URL normalization.** Scheme case, host case, default ports, trailing
  slashes, and dot segments are preserved exactly as written.

## 4. Validation Contract

Every URI-typed string read from a NodeSet source (XML, JSON, JSON-LD,
tar.gz) MUST be validated against §1 before the loader performs any
indexing. Fields covered:

* `UANodeSet/NamespaceUris/Uri[*]`
* `UANodeSet/ServerUris/Uri[*]`
* `Models/Model/@ModelUri`, `Models/Model/@XmlSchemaUri`
* `RequiredModel/@ModelUri`, `RequiredModel/@XmlSchemaUri`
* The `nsu=…` and `svu=…` portions of every NodeId string appearing
  anywhere in the document (aliases, BrowseName prefixes, reference
  targets, parent IDs, NodeId-typed Variant values).

The same validation MUST run on save, so that an AddressSpace populated
programmatically with an invalid URI cannot silently produce a malformed
NodeSet on the way out.

A failure throws `InvalidNodeSetUriException` with:

* The exact rejected URI string, quoted byte-for-byte (no trimming or
  normalization).
* A uniform reason: `URI "<exact>" is not a valid URI.`
* A location string identifying where in the source the bad URI appeared
  (file + structural path + line number when available).

The loader MUST fail before any in-memory state is mutated; partial loads
are not permitted.

## 5. Out of Scope

* Identifier-internal encoding (`s=`, `b=`, `g=`, `i=`). Use Part 6's grammar
  unchanged; this spec does not transform identifier values.
* URI schemes other than `http`, `https`, `urn`. If a new scheme becomes
  necessary in the future, extend §1 explicitly — silent acceptance is not
  permitted.
* IRI ↔ URI conversion. Non-ASCII NamespaceUris are out of scope; if a real
  use case appears, it will be addressed by a future revision.
* IRI form of canonical NodeIds (for JSON-LD `@id`). The canonical NodeId
  text per this spec is the *input* to the IRI encoding; the IRI form
  (base64url per RFC 4648 §5) is defined in code.
