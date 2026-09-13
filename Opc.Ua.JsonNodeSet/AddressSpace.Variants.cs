using Newtonsoft.Json.Linq;
using Opc.Ua.JsonNodeSet.Model;

namespace Opc.Ua.JsonNodeSet;

/// <summary>
/// Variant canonicalization for <see cref="AddressSpace"/>.
///
/// <para>Responsibilities:</para>
/// <list type="bullet">
///   <item>Maintain indexes that allow schema-aware Variant emission:
///     <c>_encodingToDataType</c>, <c>_dataTypeToXmlEncoding</c>, <c>_modelXmlNamespace</c>.</item>
///   <item>Strip redundant "Default JSON" encoding nodes on request.</item>
///   <item><see cref="ResolveVariants"/>: rewrite every <see cref="UAVariable"/>'s Part 6 JSON
///     Variant DOM so that structure <see cref="JObject"/> keys are in <see cref="DataTypeDefinition"/>
///     order, retype heuristically-typed primitives based on field DataType, normalize XML
///     encoding <c>TypeId</c>s to DataType NodeIds, and validate unions. Idempotent.</item>
/// </list>
/// </summary>
public partial class AddressSpace
{
    private const string HasEncodingRefId = "i=38";
    private const string DefaultXmlBrowseName = "Default XML";
    private const string DefaultJsonBrowseName = "Default JSON";

    private Dictionary<string, string>? _encodingToDataType;
    private Dictionary<string, string>? _dataTypeToXmlEncoding;
    private Dictionary<string, string>? _modelXmlNamespace;
    private Dictionary<string, string>? _xmlNamespaceToModel;
    private Dictionary<string, string>? _dataTypeBrowseNameIndex;

    public void BuildVariantIndexes()
    {
        _encodingToDataType = new Dictionary<string, string>(StringComparer.Ordinal);
        _dataTypeToXmlEncoding = new Dictionary<string, string>(StringComparer.Ordinal);
        _modelXmlNamespace = new Dictionary<string, string>(StringComparer.Ordinal);
        _xmlNamespaceToModel = new Dictionary<string, string>(StringComparer.Ordinal);
        _dataTypeBrowseNameIndex = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var kvp in _models)
        {
            var uri = kvp.Key;
            var xmlNs = !string.IsNullOrEmpty(kvp.Value.XmlSchemaUri)
                ? kvp.Value.XmlSchemaUri!
                : uri.TrimEnd('/') + "/Types.xsd";
            _modelXmlNamespace[uri] = xmlNs;
            _xmlNamespaceToModel[xmlNs] = uri;
        }

        foreach (var node in _sequence)
        {
            if (node is UADataType && node.NodeId != null)
            {
                var bn = StripNamespacePrefix(node.BrowseName);
                if (!string.IsNullOrEmpty(bn) && !_dataTypeBrowseNameIndex.ContainsKey(bn!))
                    _dataTypeBrowseNameIndex[bn!] = node.NodeId;
            }

            if (!_forwardRefs.TryGetValue(node.NodeId ?? "", out var fwds)) continue;
            foreach (var entry in fwds)
            {
                if (entry.ReferenceTypeId != HasEncodingRefId) continue;
                if (!_nodes.TryGetValue(entry.TargetNodeId, out var encodingNode)) continue;
                var encBn = StripNamespacePrefix(encodingNode.BrowseName);
                if (encBn == null) continue;

                _encodingToDataType[entry.TargetNodeId] = node.NodeId!;
                if (string.Equals(encBn, DefaultXmlBrowseName, StringComparison.Ordinal))
                    _dataTypeToXmlEncoding[node.NodeId!] = entry.TargetNodeId;
            }
        }
    }

    private static string? StripNamespacePrefix(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.StartsWith("nsu=", StringComparison.Ordinal)) return value;
        var semi = value.IndexOf(';');
        return semi > 0 ? value.Substring(semi + 1) : value;
    }

    private void EnsureVariantIndexes()
    {
        if (_encodingToDataType == null) BuildVariantIndexes();
    }

    public string? NormalizeEncodingNodeIdToDataType(string? nodeId)
    {
        if (nodeId == null) return null;
        EnsureVariantIndexes();
        if (_nodes.TryGetValue(nodeId, out var node) && node is UADataType) return nodeId;
        if (_encodingToDataType!.TryGetValue(nodeId, out var dt)) return dt;
        return nodeId;
    }

    internal string? TryGetXmlEncodingNodeId(string dataTypeNodeId)
    {
        EnsureVariantIndexes();
        return _dataTypeToXmlEncoding!.TryGetValue(dataTypeNodeId, out var enc) ? enc : null;
    }

    internal string? TryGetXmlSchemaUriForNodeId(string dataTypeNodeId)
    {
        EnsureVariantIndexes();
        var ns = ExtractNsu(dataTypeNodeId);
        if (ns == null) return null;
        return _modelXmlNamespace!.TryGetValue(ns, out var xmlNs) ? xmlNs : null;
    }

    internal string? TryGetXmlSchemaUriForBrowseName(string browseName)
    {
        EnsureVariantIndexes();
        if (!_dataTypeBrowseNameIndex!.TryGetValue(browseName, out var nodeId)) return null;
        return TryGetXmlSchemaUriForNodeId(nodeId);
    }

    internal string? TryGetDataTypeBrowseName(string dataTypeNodeId)
    {
        if (!_nodes.TryGetValue(dataTypeNodeId, out var node)) return null;
        return StripNamespacePrefix(node.BrowseName);
    }

    /// <summary>Returns the immediate supertype NodeId, or null at the top of the chain.</summary>
    internal string? TryGetSuperTypeId(string nodeId)
    {
        EnsureSupertypeCache();
        return _supertypeCache!.TryGetValue(nodeId, out var parent) ? parent : null;
    }

    /// <summary>
    /// Returns the inherited+own field list in base-to-derived order (matches Part 6 XML
    /// emit order). Returns null when the DataType has no Definition/Fields.
    /// </summary>
    public List<DataTypeField>? TryGetAllStructureFields(string dataTypeNodeId)
    {
        var dt = ResolveDataType(dataTypeNodeId);
        if (dt?.Definition?.Fields == null) return null;
        return CollectAllFields(dt);
    }

    internal UADataType? ResolveDataType(string? typeId)
    {
        if (typeId == null) return null;
        EnsureVariantIndexes();
        if (_nodes.TryGetValue(typeId, out var n) && n is UADataType dt) return dt;
        if (_encodingToDataType!.TryGetValue(typeId, out var real)
            && _nodes.TryGetValue(real, out var n2) && n2 is UADataType dt2) return dt2;
        return null;
    }

    public int StripDefaultJsonEncodings()
    {
        var toRemove = new List<string>();
        foreach (var node in _sequence)
        {
            if (node.NodeClass != NodeClass.UAObject) continue;
            var bn = StripNamespacePrefix(node.BrowseName);
            if (!string.Equals(bn, DefaultJsonBrowseName, StringComparison.Ordinal)) continue;
            if (node.NodeId != null &&
                _inverseRefs.TryGetValue(node.NodeId, out var inv) &&
                inv.Any(e => e.ReferenceTypeId == HasEncodingRefId))
            {
                toRemove.Add(node.NodeId);
            }
        }
        foreach (var id in toRemove) RemoveNode(id);
        if (toRemove.Count > 0) BuildVariantIndexes();
        return toRemove.Count;
    }

    /// <summary>
    /// Canonicalize every <see cref="UAVariable"/>'s Part 6 JSON Variant DOM:
    /// reorder struct keys to match <see cref="DataTypeDefinition"/> field order,
    /// retype heuristically-typed primitives based on the field's DataType, normalize any
    /// XML-encoding <c>TypeId</c>s to their underlying DataType NodeIds. Idempotent.
    /// </summary>
    public void ResolveVariants()
    {
        BuildVariantIndexes();

        foreach (var node in _sequence)
        {
            if (node is not UAVariable v) continue;
            if (v.Value?.Value == null) continue;

            var dtId = NormalizeEncodingNodeIdToDataType(v.DataType);
            v.Value.Value = CanonicalizeVariantValue(v.Value.Value, dtId);
        }
    }

    private object? CanonicalizeVariantValue(object? value, string? dataTypeNodeId)
    {
        if (value is null) return null;

        if (value is JArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
                arr[i] = CanonicalizeJsonToken(arr[i], dataTypeNodeId) ?? JValue.CreateNull();
            return arr;
        }
        if (value is List<object?> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is JToken tok) list[i] = CanonicalizeJsonToken(tok, dataTypeNodeId);
                else if (list[i] is ExtensionObject eoItem) list[i] = CanonicalizeExtensionObject(eoItem);
            }
            return list;
        }

        if (value is ExtensionObject eo)
            return CanonicalizeExtensionObject(eo);

        if (value is JObject jo)
            return CanonicalizeJsonToken(jo, dataTypeNodeId);

        return value;
    }

    private ExtensionObject CanonicalizeExtensionObject(ExtensionObject eo)
    {
        var canonicalTypeId = NormalizeEncodingNodeIdToDataType(eo.TypeId);
        eo.TypeId = canonicalTypeId;
        if (eo.Body is JObject jo)
            eo.Body = CanonicalizeStructureBody(jo, canonicalTypeId);
        return eo;
    }

    private JToken? CanonicalizeJsonToken(JToken token, string? dataTypeNodeId)
    {
        if (token == null || token.Type == JTokenType.Null) return token;
        if (token is JArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
                arr[i] = CanonicalizeJsonToken(arr[i], dataTypeNodeId) ?? JValue.CreateNull();
            return arr;
        }
        if (token is JObject jo)
        {
            // Part 6 ExtensionObject wrapper: body fields inlined alongside UaTypeId.
            if (jo["UaTypeId"] != null)
            {
                var canonTid = NormalizeEncodingNodeIdToDataType(jo["UaTypeId"]!.ToString());

                // Pull out non-reserved fields, canonicalize, and re-spread into the wrapper.
                var bodyProps = jo.Properties()
                    .Where(p => p.Name != "UaTypeId" && p.Name != "UaEncoding" && p.Name != "UaBody")
                    .ToList();
                var body = new JObject();
                foreach (var p in bodyProps)
                {
                    body.Add(p.Name, p.Value);
                }
                foreach (var p in bodyProps) jo.Remove(p.Name);

                jo["UaTypeId"] = canonTid;
                var canonBody = CanonicalizeStructureBody(body, canonTid);
                foreach (var prop in canonBody.Properties())
                {
                    jo[prop.Name] = prop.Value;
                }
                return jo;
            }
            return CanonicalizeStructureBody(jo, dataTypeNodeId);
        }
        return token;
    }

    private JObject CanonicalizeStructureBody(JObject body, string? dataTypeNodeId)
    {
        var dt = ResolveDataType(dataTypeNodeId);
        if (dt?.Definition?.Fields == null || dt.Definition.Fields.Count == 0)
            return body;

        var orderedFields = CollectAllFields(dt);

        // ASP.NET's default web JSON options camelCase property names on outbound
        // serialization, which means struct field bodies arriving via the REST PUT
        // path can have lowercase keys (`age`, `name`) instead of the canonical
        // PascalCase from the DataTypeDefinition (`Age`, `Name`). Build a case-
        // insensitive lookup over the actual body keys so we hit fields regardless
        // of casing, then emit them under the canonical name.
        var bodyKeyLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in body.Properties())
            bodyKeyLookup[prop.Name] = prop.Name;

        JToken? GetBodyValue(string fieldName)
            => bodyKeyLookup.TryGetValue(fieldName, out var actualKey) ? body[actualKey] : null;

        var canon = new JObject();

        // Unions: SwitchField first, then the selected arm.
        if (dt.Definition.IsUnion == true || dt.DataTypeForm == "Union")
        {
            var sf = GetBodyValue("SwitchField");
            if (sf != null) canon["SwitchField"] = sf;
            foreach (var field in orderedFields)
            {
                if (field.Name == null) continue;
                var v = GetBodyValue(field.Name);
                if (v != null)
                {
                    canon[field.Name] = CanonicalizeField(v, field);
                    break;
                }
            }
            return canon;
        }

        // StructureWithOptionalFields: EncodingMask first.
        var em = GetBodyValue("EncodingMask");
        if (em != null) canon["EncodingMask"] = em;

        foreach (var field in orderedFields)
        {
            if (field.Name == null) continue;
            var v = GetBodyValue(field.Name);
            if (v == null) continue;
            canon[field.Name] = CanonicalizeField(v, field);
        }

        return canon;
    }

    private JToken CanonicalizeField(JToken value, DataTypeField field)
    {
        var rank = field.ValueRank ?? -1;
        var fieldDt = field.DataType;

        // Field array (ValueRank ≥ 1).
        if (rank >= 1)
        {
            // Promote a non-array scalar into a 1-element array — the schema-free reader
            // can't tell from XML alone that <FieldName>x</FieldName> with one occurrence
            // is meant to be an array of one element vs a scalar. With schema we know.
            JArray arr;
            if (value is JArray existingArr)
            {
                arr = existingArr;
            }
            else if (value.Type == JTokenType.Null || IsEmptyScalarPlaceholder(value))
            {
                // Empty XML element <Field></Field> — the schema-free reader emits "" for
                // a leaf with no children and empty inner text; for an array-typed field
                // that means "no items", not a 1-element [""] array.
                return new JArray();
            }
            else
            {
                arr = new JArray { value };
            }

            var outArr = new JArray();
            foreach (var item in arr)
            {
                var canon = CanonicalizeJsonToken(item, fieldDt);
                if (canon is JValue || canon is JArray)
                {
                    canon = RetypePrimitive(canon, fieldDt);
                }
                outArr.Add(canon ?? JValue.CreateNull());
            }
            return outArr;
        }

        // Scalar (rank ≤ -1): coerce primitive to match the field's DataType.
        if (value is JObject jo)
        {
            return CanonicalizeJsonToken(jo, fieldDt) ?? jo;
        }
        return RetypePrimitive(value, fieldDt) ?? value;
    }

    /// <summary>
    /// True for the schema-free reader's "empty XML leaf" placeholder — an empty/whitespace
    /// string. Used to distinguish "no items" from "one empty-string item" when promoting
    /// a scalar into an array-typed field.
    /// </summary>
    private static bool IsEmptyScalarPlaceholder(JToken value)
    {
        return value is JValue jv
            && jv.Value is string s
            && string.IsNullOrWhiteSpace(s);
    }

    /// <summary>
    /// Coerces a JSON value's CLR type to match the field's declared DataType. Examples:
    /// string <c>"true"</c> → bool for Boolean fields; string <c>"42"</c> → integer for
    /// Int32 fields; bare string → <c>{Text: "..."}</c> for LocalizedText fields.
    /// Returns the original value when no coercion is needed or possible.
    /// </summary>
    private JToken? RetypePrimitive(JToken? value, string? fieldDataTypeNodeId)
    {
        if (value == null || value.Type == JTokenType.Null) return value;
        if (string.IsNullOrEmpty(fieldDataTypeNodeId)) return value;

        var bt = ResolveBuiltInType(fieldDataTypeNodeId);
        if (bt == null) return value;

        // Already-correct shapes pass through.
        switch (bt.Value)
        {
            case BuiltInType.Boolean:
                if (value is JValue jvB)
                {
                    if (jvB.Value is bool) return value;
                    var s = jvB.Value?.ToString();
                    if (bool.TryParse(s, out var b)) return new JValue(b);
                }
                break;

            case BuiltInType.SByte:
            case BuiltInType.Byte:
            case BuiltInType.Int16:
            case BuiltInType.UInt16:
            case BuiltInType.Int32:
            case BuiltInType.UInt32:
                if (value is JValue jvI)
                {
                    if (jvI.Value is long || jvI.Value is int) return value;
                    if (jvI.Value is string s
                        && long.TryParse(s, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var l))
                        return new JValue(l);
                }
                break;

            case BuiltInType.Int64:
            case BuiltInType.UInt64:
                // Part 6 §5.4 — encoded as JSON string. Already a string → leave; number → stringify.
                if (value is JValue jvL)
                {
                    if (jvL.Value is string) return value;
                    return new JValue(jvL.Value?.ToString() ?? "0");
                }
                break;

            case BuiltInType.Float:
            case BuiltInType.Double:
                if (value is JValue jvF)
                {
                    if (jvF.Value is double || jvF.Value is float) return value;
                    if (jvF.Value is long l) return new JValue((double)l);
                    if (jvF.Value is string s
                        && double.TryParse(s, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var d))
                        return new JValue(d);
                }
                break;

            case BuiltInType.LocalizedText:
                // A bare-string LocalizedText becomes {Text: "..."}.
                if (value is JValue jvT && jvT.Value is string txt)
                {
                    return new JObject { ["Text"] = txt };
                }
                break;

            case BuiltInType.NodeId:
            case BuiltInType.ExpandedNodeId:
            case BuiltInType.QualifiedName:
            case BuiltInType.String:
            case BuiltInType.DateTime:
            case BuiltInType.Guid:
            case BuiltInType.ByteString:
            case BuiltInType.XmlElement:
                // String-shaped — leave as-is (number → stringify if needed).
                if (value is JValue jvS)
                {
                    if (jvS.Value is string) return value;
                    return new JValue(jvS.Value?.ToString() ?? "");
                }
                break;
        }

        return value;
    }

    /// <summary>
    /// Walks the supertype chain of <paramref name="dataTypeNodeId"/> until a built-in
    /// (i=1..i=24) is reached. Enumerations resolve to Int32; OptionSets to UInt32.
    /// Returns null when the type can't be resolved.
    /// </summary>
    private BuiltInType? ResolveBuiltInType(string dataTypeNodeId)
    {
        // Direct built-in.
        if (dataTypeNodeId.StartsWith("i=", StringComparison.Ordinal)
            && int.TryParse(dataTypeNodeId.AsSpan(2), out var n)
            && n >= 1 && n <= 24)
        {
            return (BuiltInType)n;
        }

        // Walk supertype chain.
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var current = NormalizeEncodingNodeIdToDataType(dataTypeNodeId);
        while (current != null && visited.Add(current))
        {
            if (current.StartsWith("i=", StringComparison.Ordinal)
                && int.TryParse(current.AsSpan(2), out var nn) && nn >= 1 && nn <= 24)
            {
                return (BuiltInType)nn;
            }
            // Enumeration root → Int32; Structure root → ExtensionObject (return Structure encoding).
            if (current == "i=29") return BuiltInType.Int32;
            if (current == "i=22") return BuiltInType.ExtensionObject;
            current = TryGetSuperTypeId(current);
        }
        return null;
    }

    private List<DataTypeField> CollectAllFields(UADataType dt)
    {
        var chain = new List<UADataType> { dt };
        EnsureSupertypeCache();
        var current = dt.NodeId;
        while (current != null && _supertypeCache!.TryGetValue(current, out var parent) && parent != null)
        {
            if (!_nodes.TryGetValue(parent, out var parentNode)) break;
            if (parentNode is not UADataType parentDt) break;
            if (parentDt.Definition?.Fields == null || parentDt.Definition.Fields.Count == 0) break;
            chain.Add(parentDt);
            current = parent;
        }
        chain.Reverse();
        var ordered = new List<DataTypeField>();
        foreach (var t in chain)
        {
            if (t.Definition?.Fields != null)
                ordered.AddRange(t.Definition.Fields);
        }
        return ordered;
    }
}
