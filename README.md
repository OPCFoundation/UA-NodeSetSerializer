# OPC UA NodeSet Serializer

`Opc.Ua.NodeSetSerializer` is a .NET class library that reads and writes OPC UA NodeSets in XML and JSON. It is the shared
serialization component. It provides everything the OPC UA NodeSet Editor needs to load, edit and emit NodeSets lives here.

> The JSON serialization in this library is not a published OPC Foundation serialization format.
> It was developed to meet the requirements for the OPC UA NodeSetEditor and is only meant for use in that context.
> **Do not treat the JSON format emitted here as stable.**
> The OPC Foundation will publish an official JSON NodeSet serialization and this library will be updated to match.

## Documents

* [`canonical-nodeid-encoding.md`](canonical-nodeid-encoding.md) — how NodeIds are encoded in the JSON form.
* [`uri-percent-encoding.md`](uri-percent-encoding.md) — percent-encoding rules for URIs appearing in NodeIds.
* [`Opc.Ua.NodeSetSerializer/json-nodeset-schema.json`](Opc.Ua.NodeSetSerializer/json-nodeset-schema.json) — the JSON Schema for the encoding.

## Licence

MIT. See the repository `LICENSE`.
