# UANodeSetSerializer

> **This is PROTOTYPE code for a draft encoding. It is not a published OPC Foundation
> specification, and the JSON schema WILL change before release.** Do not treat the format
> emitted here as stable, and do not build a production interchange on it.

`Opc.Ua.JsonNodeSet` is a .NET class library that reads and writes OPC UA NodeSets in XML and
JSON, including the normative property ordering the JSON encoding requires. It is the shared
serialization component: everything the OPC UA NodeSet Editor needs to load, edit and emit
NodeSets lives here.

## Scope

| In | Out |
|----|-----|
| XML NodeSet read/write (`UANodeSet.xsd`) | Line-delimited JSON (JSONL) |
| JSON NodeSet read/write | RDF / JSON-LD export |
| Normative property ordering and order validation | Compressed `.uanodeset` archives |
| Address space model, variant conversion, SPDX headers | The `Opc.Ua.NodeSetTool` command-line tool |

The formats in the right-hand column are prototypes of draft encodings and live in the internal
[UA-NodeSetTool](https://github.com/OPCF-Members/UA-NodeSetTool) repository, which consumes this
library and extends `NodeSetSerializer` to add them.

## Documents

* [`canonical-nodeid-encoding.md`](canonical-nodeid-encoding.md) — how NodeIds are encoded in the JSON form.
* [`uri-percent-encoding.md`](uri-percent-encoding.md) — percent-encoding rules for URIs appearing in NodeIds.
* [`Opc.Ua.JsonNodeSet/json-nodeset-schema.json`](Opc.Ua.JsonNodeSet/json-nodeset-schema.json) — the JSON Schema for the encoding.

## Licence

MIT. See the repository `LICENSE`.
