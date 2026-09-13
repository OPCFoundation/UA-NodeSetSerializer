using System.Globalization;
using System.Text;
using System.Xml;
using Newtonsoft.Json.Linq;
using Json = Opc.Ua.JsonNodeSet.Model;

namespace Opc.Ua.JsonNodeSet;

/// <summary>
/// Converts OPC UA Variant / ExtensionObject values between XML (Part 6 §5.2) and
/// JSON (Part 6 §5.4 reversible form) representations.
///
/// <para>JSON shapes (matching opc.ua.jsonschema.json):</para>
/// <list type="bullet">
///   <item>Built-in scalars: native JSON. Boolean→bool, Int*≤32→number, Int64/UInt64→string,
///     Float/Double→number, String/DateTime/Guid/ByteString/XmlElement/NodeId/ExpandedNodeId/QualifiedName→string.</item>
///   <item>StatusCode → <c>{Code, Symbol?}</c>.</item>
///   <item>LocalizedText → <c>{Locale?, Text?}</c>.</item>
///   <item>Structure body → flat object with field names as keys.</item>
///   <item>Union body → <c>{SwitchField, &lt;arm&gt;}</c>.</item>
///   <item>StructureWithOptionalFields body → <c>{EncodingMask, &lt;present fields&gt;}</c>.</item>
///   <item>ExtensionObject (abstract / variant-typed slot) — JSON inline (default):
///     <c>{UaTypeId, &lt;field1&gt;, &lt;field2&gt;, ...}</c> with body fields at the same level
///     as <c>UaTypeId</c>. <c>UaBody</c> is reserved for Binary (base64) / XML encodings
///     and only appears when <c>UaEncoding != 0</c>.</item>
///   <item>Variant slot → <c>{UaType, Value, Dimensions?}</c>.</item>
///   <item>Matrix → <c>{Array, Dimensions}</c>.</item>
///   <item>Array (1-D) → JSON array.</item>
/// </list>
///
/// <para>Schema-free fallback: the reader uses XML pattern matching (e.g. <c>&lt;Identifier&gt;</c>
/// child → NodeId) to recognise the well-known compound types when no DataTypeDefinition
/// is available. For un-typed primitives inside a struct body (just text content), the
/// reader heuristically parses to bool / integer / floating / string. <see cref="AddressSpace.ResolveVariants"/>
/// re-types them when a definition becomes available.</para>
///
/// <para>The XML writer emits Part 6 XML faithful to the JSON shape. When an
/// <see cref="AddressSpace"/> is attached, struct fields are wrapped with the correct
/// concrete-type element name (e.g. <c>&lt;X&gt;&lt;TestConcreteStructure&gt;...&lt;/&gt;&lt;/&gt;</c>).
/// Schema-free, the writer emits the body inline without the inner type-name wrapper —
/// good enough to JSON-round-trip but not byte-equal to a wrapped Part 6 XML.</para>
/// </summary>
internal static class VariantConverter
{
    private const string UaTypesXmlNamespace = "http://opcfoundation.org/UA/2008/02/Types.xsd";
    private const string UaPrefix = "uax";
    private const string XsiNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Encodes a DataType BrowseName or Structure field name into a valid XML NCName per
    /// Part 6 §5.1.13: any character not permitted by the encoding is replaced with '_',
    /// and an additional '_' is prepended if the resulting first character is not a valid
    /// NCName start character (e.g. a digit).
    /// </summary>
    internal static string EncodeNameForXml(string name)
    {
        if (string.IsNullOrEmpty(name)) return "_";

        StringBuilder? sb = null;
        for (int i = 0; i < name.Length; i++)
        {
            if (XmlConvert.IsNCNameChar(name[i]))
            {
                sb?.Append(name[i]);
            }
            else
            {
                sb ??= new StringBuilder(name.Length + 1).Append(name, 0, i);
                sb.Append('_');
            }
        }

        var result = sb?.ToString() ?? name;
        if (!XmlConvert.IsStartNCNameChar(result[0]))
            result = "_" + result;
        return result;
    }

    /// <summary>
    /// Builds an encoded→original lookup for fields whose Name is not a valid NCName.
    /// Returns null if no field needs decoding (the common case).
    /// </summary>
    private static Dictionary<string, string>? BuildFieldNameDecodeMap(List<Json.DataTypeField>? fields)
    {
        if (fields == null) return null;
        Dictionary<string, string>? map = null;
        foreach (var f in fields)
        {
            if (f.Name == null) continue;
            var encoded = EncodeNameForXml(f.Name);
            if (encoded != f.Name)
            {
                map ??= new Dictionary<string, string>(StringComparer.Ordinal);
                map[encoded] = f.Name;
            }
        }
        return map;
    }

    internal static Json.ExtensionObject ToJsonExtensionObject(XmlElement xml, VariantXmlContext ctx)
        => ReadExtensionObjectFromXml(xml, ctx);

    internal static XmlElement? ToXmlExtensionObject(Json.ExtensionObject eo, VariantXmlContext ctx)
    {
        var doc = new XmlDocument();
        var el = WriteExtensionObjectToXml(doc, eo, ctx);
        if (el != null) doc.AppendChild(el);
        return doc.DocumentElement;
    }

    #region XML -> JSON (Part 6)

    /// <summary>
    /// Parses an XML Variant element (the <c>&lt;Value&gt;</c> wrapper, or the typed
    /// element directly) into a Part 6 JSON-shaped <see cref="Json.Variant"/>.
    /// </summary>
    public static Json.Variant? ReadVariantFromXml(XmlElement? input, VariantXmlContext ctx)
    {
        if (input == null) return null;

        XmlElement? typed = input;
        if (string.Equals(input.LocalName, "Value", StringComparison.Ordinal))
        {
            typed = input.ChildNodes.OfType<XmlElement>().FirstOrDefault();
            if (typed == null) return null;
        }

        return ReadTypedElement(typed, ctx);
    }

    private static Json.Variant ReadTypedElement(XmlElement typed, VariantXmlContext ctx)
    {
        var name = typed.LocalName;

        if (string.Equals(name, "Matrix", StringComparison.Ordinal))
            return ReadMatrix(typed, ctx);

        if (name.StartsWith("ListOf", StringComparison.Ordinal))
            return ReadList(typed, name.Substring("ListOf".Length), ctx);

        if (string.Equals(name, nameof(Json.BuiltInType.ExtensionObject), StringComparison.Ordinal))
        {
            var eo = ReadExtensionObjectFromXml(typed, ctx);
            return new Json.Variant((int)Json.BuiltInType.ExtensionObject, eo);
        }

        if (string.Equals(name, nameof(Json.BuiltInType.Variant), StringComparison.Ordinal))
        {
            // <Variant>< inner typed element /></Variant>
            var inner = typed.ChildNodes.OfType<XmlElement>().FirstOrDefault();
            return inner != null ? ReadTypedElement(inner, ctx)
                : new Json.Variant((int)Json.BuiltInType.Variant, null);
        }

        if (IsBuiltInTypeName(name, out var bt))
        {
            var value = ReadBuiltInScalar(bt, typed, ctx);
            return new Json.Variant((int)bt, value);
        }

        // Unknown name → treat as a structure body directly.
        var body = ReadStructureBody(typed, ctx);
        return new Json.Variant((int)Json.BuiltInType.ExtensionObject,
            new Json.ExtensionObject { TypeId = null, Body = body });
    }

    private static Json.Variant ReadList(XmlElement listEl, string itemTypeName, VariantXmlContext ctx)
    {
        var items = new List<object?>();
        bool itemIsBuiltIn = IsBuiltInTypeName(itemTypeName, out var bt);

        foreach (var child in listEl.ChildNodes.OfType<XmlElement>())
        {
            if (itemIsBuiltIn)
            {
                items.Add(ReadBuiltInScalar(bt, child, ctx));
            }
            else if (string.Equals(child.LocalName, nameof(Json.BuiltInType.ExtensionObject), StringComparison.Ordinal))
            {
                items.Add(ReadExtensionObjectFromXml(child, ctx));
            }
            else
            {
                items.Add(ReadStructureBody(child, ctx));
            }
        }

        if (!itemIsBuiltIn) bt = Json.BuiltInType.ExtensionObject;
        return new Json.Variant((int)bt, items);
    }

    private static Json.Variant ReadMatrix(XmlElement matrix, VariantXmlContext ctx)
    {
        var dimsEl = matrix.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Dimensions");
        var elemsEl = matrix.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Elements");
        var dims = dimsEl?.ChildNodes.OfType<XmlElement>()
            .Select(e => int.TryParse(e.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0)
            .ToList() ?? new List<int>();

        var items = new List<object?>();
        var bt = Json.BuiltInType.String;
        if (elemsEl != null)
        {
            var first = elemsEl.ChildNodes.OfType<XmlElement>().FirstOrDefault();
            if (first != null)
            {
                if (!IsBuiltInTypeName(first.LocalName, out bt))
                    bt = Json.BuiltInType.ExtensionObject;

                foreach (var e in elemsEl.ChildNodes.OfType<XmlElement>())
                {
                    if (bt == Json.BuiltInType.ExtensionObject)
                    {
                        if (string.Equals(e.LocalName, nameof(Json.BuiltInType.ExtensionObject), StringComparison.Ordinal))
                            items.Add(ReadExtensionObjectFromXml(e, ctx));
                        else
                            items.Add(ReadStructureBody(e, ctx));
                    }
                    else
                    {
                        items.Add(ReadBuiltInScalar(bt, e, ctx));
                    }
                }
            }
        }

        return new Json.Variant((int)bt, items, dims);
    }

    private static Json.ExtensionObject ReadExtensionObjectFromXml(XmlElement eoEl, VariantXmlContext ctx)
    {
        string? typeId = null;
        JObject? body = null;

        foreach (var child in eoEl.ChildNodes.OfType<XmlElement>())
        {
            if (string.Equals(child.LocalName, "TypeId", StringComparison.Ordinal))
            {
                var idEl = child.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Identifier");
                var raw = idEl?.InnerText ?? child.InnerText;
                typeId = ctx.NsIndexToNsu(raw);
            }
            else if (string.Equals(child.LocalName, "Body", StringComparison.Ordinal))
            {
                var structEl = child.ChildNodes.OfType<XmlElement>().FirstOrDefault();
                if (structEl != null)
                {
                    body = ReadStructureBody(structEl, ctx, typeId);
                }
            }
        }

        return new Json.ExtensionObject { TypeId = typeId, Body = body };
    }

    /// <summary>
    /// Reads an XML structure-body element (e.g. <c>&lt;TestScalarStructure&gt;</c>) into a
    /// Part 6 JSON object. Each child element becomes a property; the value is read
    /// recursively as a Part 6 field value.
    /// </summary>
    private static JObject ReadStructureBody(XmlElement structEl, VariantXmlContext ctx, string? dataTypeId = null)
    {
        var decodeMap = BuildFieldNameDecodeMap(ctx.ResolveStructureFields(dataTypeId));

        var obj = new JObject();

        // Group children by local name preserving first-seen order. When the
        // schema is known, decode encoded field names back to their originals
        // (per Part 6 §5.1.13 — names with invalid XML chars are encoded as '_').
        var firstOrder = new List<string>();
        var grouped = new Dictionary<string, List<XmlElement>>(StringComparer.Ordinal);
        foreach (var c in structEl.ChildNodes.OfType<XmlElement>())
        {
            var name = decodeMap != null && decodeMap.TryGetValue(c.LocalName, out var orig)
                ? orig : c.LocalName;
            if (!grouped.TryGetValue(name, out var list))
            {
                list = new List<XmlElement>();
                grouped[name] = list;
                firstOrder.Add(name);
            }
            list.Add(c);
        }

        foreach (var name in firstOrder)
        {
            var group = grouped[name];
            if (group.Count == 1)
            {
                obj[name] = ReadFieldValue(group[0], ctx);
            }
            else
            {
                var arr = new JArray();
                foreach (var item in group) arr.Add(ReadFieldValue(item, ctx));
                obj[name] = arr;
            }
        }

        return obj;
    }

    /// <summary>
    /// Reads the *value* of a struct field element. The element wraps the value; the
    /// value's shape determines the Part 6 JSON form. Returns <see cref="JValue"/>(null)
    /// for <c>xsi:nil="true"</c> elements.
    /// </summary>
    private static JToken ReadFieldValue(XmlElement field, VariantXmlContext ctx)
    {
        // xsi:nil → null
        var nil = field.GetAttributeNode("nil", XsiNamespace);
        if (nil != null && string.Equals(nil.Value, "true", StringComparison.OrdinalIgnoreCase))
            return JValue.CreateNull();

        var children = field.ChildNodes.OfType<XmlElement>().ToList();

        if (children.Count == 0)
        {
            // Pure-text leaf — heuristically type the JSON value.
            return ParsePrimitiveText(field.InnerText);
        }

        // Recognise OPC UA compound built-in shapes by their unique child pattern.
        if (TryReadCompoundBuiltIn(field, children, ctx, out var compound)) return compound!;

        // Single-child: ExtensionObject / Variant / nested struct.
        if (children.Count == 1)
        {
            var only = children[0];
            if (string.Equals(only.LocalName, nameof(Json.BuiltInType.ExtensionObject), StringComparison.Ordinal))
            {
                var eo = ReadExtensionObjectFromXml(only, ctx);
                return BuildExtensionObjectJson(eo);
            }
            if (string.Equals(only.LocalName, nameof(Json.BuiltInType.Variant), StringComparison.Ordinal))
            {
                var v = ReadTypedElement(only, ctx);
                return BuildVariantJson(v);
            }
            if (IsBuiltInTypeName(only.LocalName, out var bt))
            {
                // Unwrap a built-in inside a field (e.g. <Field><Int32>1</Int32></Field>
                // appears for Variant-typed fields holding a scalar).
                var primitive = ReadBuiltInScalar(bt, only, ctx);
                return ToJToken(primitive);
            }

            // Concrete inner struct wrapper: <Field><InnerStruct>...</InnerStruct></Field>.
            // Inline the inner body — Part 6 JSON for typed struct fields drops the wrapper.
            return ReadStructureBody(only, ctx);
        }

        // Multiple children with the same local name → array.
        var firstName = children[0].LocalName;
        if (children.All(c => c.LocalName == firstName))
        {
            var arr = new JArray();
            foreach (var item in children) arr.Add(ReadFieldValue(item, ctx));
            return arr;
        }

        // Mixed children — treat as inlined struct body.
        return ReadStructureBody(field, ctx);
    }

    /// <summary>
    /// Detects and converts the well-known OPC UA compound built-in shapes that appear
    /// inline inside a struct field: NodeId, ExpandedNodeId, StatusCode, QualifiedName,
    /// LocalizedText. Returns true (and sets <paramref name="result"/>) when matched.
    /// </summary>
    private static bool TryReadCompoundBuiltIn(
        XmlElement field, List<XmlElement> children, VariantXmlContext ctx, out JToken? result)
    {
        result = null;
        var names = children.Select(c => c.LocalName).ToList();

        // NodeId / ExpandedNodeId — single Identifier child.
        if (names.Count == 1 && names[0] == "Identifier")
        {
            var raw = children[0].InnerText;
            result = new JValue(ctx.NsIndexToNsu(raw));
            return true;
        }

        // StatusCode — single Code child (optionally with Symbol).
        if (names.Contains("Code") && names.All(n => n == "Code" || n == "Symbol"))
        {
            var codeEl = children.First(c => c.LocalName == "Code");
            var symEl = children.FirstOrDefault(c => c.LocalName == "Symbol");
            uint code = uint.TryParse(codeEl.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0u;
            var obj = new JObject { ["Code"] = code };
            if (symEl != null) obj["Symbol"] = symEl.InnerText;
            result = obj;
            return true;
        }

        // LocalizedText — Locale and/or Text children.
        if (names.All(n => n == "Locale" || n == "Text") && (names.Contains("Locale") || names.Contains("Text")))
        {
            var localeEl = children.FirstOrDefault(c => c.LocalName == "Locale");
            var textEl = children.FirstOrDefault(c => c.LocalName == "Text");
            var obj = new JObject();
            if (localeEl != null) obj["Locale"] = localeEl.InnerText;
            if (textEl != null) obj["Text"] = textEl.InnerText;
            result = obj;
            return true;
        }

        // QualifiedName — NamespaceIndex and Name children.
        if (names.Count == 2 && names.Contains("NamespaceIndex") && names.Contains("Name"))
        {
            var nsEl = children.First(c => c.LocalName == "NamespaceIndex");
            var nameEl = children.First(c => c.LocalName == "Name");
            int nsIdx = int.TryParse(nsEl.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : 0;
            string name = nameEl.InnerText;
            string composed = nsIdx == 0 ? name
                : (ctx.GetNamespaceUri(nsIdx) is string uri && !string.IsNullOrEmpty(uri))
                    ? $"nsu={uri};{name}"
                    : $"{nsIdx}:{name}";
            result = new JValue(composed);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the Part 6 JSON form of an ExtensionObject. For JSON-inline encoding the
    /// body fields are emitted at the same level as <c>UaTypeId</c>. For Binary/XML
    /// encodings <c>UaBody</c> carries the encoded body string.
    /// </summary>
    private static JToken BuildExtensionObjectJson(Json.ExtensionObject eo)
    {
        var obj = new JObject();
        if (eo.TypeId != null) obj["UaTypeId"] = eo.TypeId;

        var enc = eo.Encoding;
        if (enc is byte e && e != 0)
        {
            obj["UaEncoding"] = e;
            obj["UaBody"] = eo.Body?.ToString();
            return obj;
        }

        // JSON inline — flatten body fields into the wrapper.
        if (eo.Body is JObject body)
        {
            foreach (var prop in body.Properties())
            {
                if (prop.Name is "UaTypeId" or "UaEncoding" or "UaBody") continue;
                obj[prop.Name] = prop.Value;
            }
        }
        return obj;
    }

    /// <summary>True for the Part 6 JSON ExtensionObject inline-body wrapper shape.</summary>
    private static bool IsExtensionObjectWrapper(JObject jo) =>
        jo["UaTypeId"] != null;

    /// <summary>
    /// Splits a Part 6 ExtensionObject wrapper JObject into (typeId, encoding, body).
    /// For JSON-inline (default), <paramref name="body"/> is a fresh JObject holding the
    /// non-reserved fields. For Binary/XML, <paramref name="body"/> is null and callers
    /// take the value from the original <c>UaBody</c> property.
    /// </summary>
    private static void DecomposeExtensionObjectWrapper(JObject jo, out string? typeId, out byte? encoding, out JObject? body)
    {
        typeId = jo["UaTypeId"]?.ToString();
        encoding = null;
        if (jo["UaEncoding"] is JValue encVal && encVal.Value != null
            && byte.TryParse(encVal.Value.ToString(), out var encByte) && encByte != 0)
        {
            encoding = encByte;
        }
        if (encoding is null or 0)
        {
            body = new JObject();
            foreach (var prop in jo.Properties())
            {
                if (prop.Name is "UaTypeId" or "UaEncoding" or "UaBody") continue;
                body.Add(prop.Name, prop.Value);
            }
        }
        else
        {
            body = null;
        }
    }

    private static JToken BuildVariantJson(Json.Variant v)
    {
        var obj = new JObject();
        if (v.UaType.HasValue) obj["UaType"] = v.UaType.Value;
        if (v.Value != null) obj["Value"] = ToJToken(v.Value);
        if (v.Dimensions is { Count: > 0 })
        {
            var dims = new JArray();
            foreach (var d in v.Dimensions) dims.Add(d);
            obj["Dimensions"] = dims;
        }
        return obj;
    }

    /// <summary>
    /// Reads a built-in scalar XML element (whose local name matches a <see cref="Json.BuiltInType"/>
    /// member) into the Part 6 JSON value type for that built-in.
    /// </summary>
    private static object? ReadBuiltInScalar(Json.BuiltInType type, XmlElement el, VariantXmlContext ctx)
    {
        var ci = CultureInfo.InvariantCulture;
        switch (type)
        {
            case Json.BuiltInType.Boolean: return bool.TryParse(el.InnerText, out var b) && b;
            case Json.BuiltInType.SByte: return sbyte.TryParse(el.InnerText, NumberStyles.Integer, ci, out var sb) ? sb : (sbyte)0;
            case Json.BuiltInType.Byte: return byte.TryParse(el.InnerText, NumberStyles.Integer, ci, out var by) ? by : (byte)0;
            case Json.BuiltInType.Int16: return short.TryParse(el.InnerText, NumberStyles.Integer, ci, out var s) ? s : (short)0;
            case Json.BuiltInType.UInt16: return ushort.TryParse(el.InnerText, NumberStyles.Integer, ci, out var us) ? us : (ushort)0;
            case Json.BuiltInType.Int32: return int.TryParse(el.InnerText, NumberStyles.Integer, ci, out var i) ? i : 0;
            case Json.BuiltInType.UInt32: return uint.TryParse(el.InnerText, NumberStyles.Integer, ci, out var ui) ? ui : 0u;
            case Json.BuiltInType.Int64:
                // Part 6 §5.4: Int64 / UInt64 encoded as JSON STRING (precision).
                return el.InnerText;
            case Json.BuiltInType.UInt64: return el.InnerText;
            case Json.BuiltInType.Float: return float.TryParse(el.InnerText, NumberStyles.Float, ci, out var f) ? f : 0f;
            case Json.BuiltInType.Double: return double.TryParse(el.InnerText, NumberStyles.Float, ci, out var d) ? d : 0d;
            case Json.BuiltInType.String: return el.InnerText;
            case Json.BuiltInType.DateTime: return el.InnerText;
            case Json.BuiltInType.Guid:
                {
                    // <Guid><String>uuid</String></Guid>
                    var gs = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "String");
                    return gs?.InnerText ?? el.InnerText;
                }
            case Json.BuiltInType.ByteString: return el.InnerText;
            case Json.BuiltInType.XmlElement:
                {
                    var inner = el.ChildNodes.OfType<XmlElement>().FirstOrDefault();
                    return inner?.OuterXml ?? el.InnerXml;
                }
            case Json.BuiltInType.NodeId:
            case Json.BuiltInType.ExpandedNodeId:
                {
                    var idEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Identifier");
                    var raw = idEl?.InnerText ?? el.InnerText;
                    return ctx.NsIndexToNsu(raw);
                }
            case Json.BuiltInType.StatusCode:
                {
                    var codeEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Code");
                    var symEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Symbol");
                    uint code = uint.TryParse(codeEl?.InnerText ?? el.InnerText, NumberStyles.Integer, ci, out var c) ? c : 0u;
                    var obj = new JObject { ["Code"] = code };
                    if (symEl != null) obj["Symbol"] = symEl.InnerText;
                    return obj;
                }
            case Json.BuiltInType.QualifiedName:
                {
                    var nsEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "NamespaceIndex");
                    var nameEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Name");
                    if (nameEl == null) return el.InnerText;
                    int nsIdx = nsEl != null && int.TryParse(nsEl.InnerText, out var idx) ? idx : 0;
                    if (nsIdx == 0) return nameEl.InnerText;
                    if (ctx.GetNamespaceUri(nsIdx) is string uri && !string.IsNullOrEmpty(uri))
                        return $"nsu={uri};{nameEl.InnerText}";
                    return $"{nsIdx}:{nameEl.InnerText}";
                }
            case Json.BuiltInType.LocalizedText:
                {
                    var localeEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Locale");
                    var textEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Text");
                    var obj = new JObject();
                    if (localeEl != null) obj["Locale"] = localeEl.InnerText;
                    if (textEl != null) obj["Text"] = textEl.InnerText;
                    if (obj.Count == 0 && !string.IsNullOrEmpty(el.InnerText))
                        obj["Text"] = el.InnerText;
                    return obj;
                }
            case Json.BuiltInType.ExtensionObject:
                return ReadExtensionObjectFromXml(el, ctx);
            case Json.BuiltInType.Variant:
                {
                    var inner = el.ChildNodes.OfType<XmlElement>().FirstOrDefault();
                    if (inner != null) return ReadTypedElement(inner, ctx);
                    return el.InnerText;
                }
            default:
                return el.InnerText;
        }
    }

    /// <summary>
    /// Heuristically converts a piece of XML inner text to a typed JValue: bool / long /
    /// double / string. Used for un-typed primitive struct fields when no schema is yet
    /// known. <see cref="AddressSpace.ResolveVariants"/> retypes these once a definition
    /// becomes available.
    /// </summary>
    private static JValue ParsePrimitiveText(string text)
    {
        // Schema-aware pass-2 (AddressSpace.ResolveVariants → RetypePrimitive)
        // is the source of truth for typing — it knows the field's declared
        // DataType. We only do *unambiguous* typing here so that schema-free
        // contexts (e.g., a struct whose DataType isn't loaded) don't degrade
        // numbers and booleans to strings:
        //
        //   * "true" / "false"   → bool
        //   * fits in long        → long  (covers 1..7: SByte..UInt32)
        //   * has '.' / 'e' / 'E' → double (Float / Double in scientific or
        //                           decimal notation — these can't be Part 6
        //                           string-encoded Int64/UInt64)
        //
        // Anything else stays a string. Crucially this includes:
        //   * UInt64 in (Int64.MaxValue, UInt64.MaxValue] — Part 6 §5.4 wants
        //     these as JSON strings.
        //   * Float / Double written in fixed-point form ("340282350000…") —
        //     pass-2 retypes these to double when the field is Float / Double.
        if (string.IsNullOrEmpty(text)) return new JValue(string.Empty);
        var trimmed = text.Trim();
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)) return new JValue(true);
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase)) return new JValue(false);
        var ci = CultureInfo.InvariantCulture;
        if (long.TryParse(trimmed, NumberStyles.Integer, ci, out var l)) return new JValue(l);
        if (trimmed.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0
            && double.TryParse(trimmed, NumberStyles.Float, ci, out var d))
            return new JValue(d);
        return new JValue(text);
    }

    private static JToken ToJToken(object? value)
    {
        if (value == null) return JValue.CreateNull();
        if (value is JToken jt) return jt;
        if (value is Json.ExtensionObject eo) return BuildExtensionObjectJson(eo);
        if (value is System.Collections.IEnumerable e && value is not string)
        {
            var arr = new JArray();
            foreach (var item in e) arr.Add(ToJToken(item));
            return arr;
        }
        return new JValue(value);
    }

    #endregion

    #region JSON (Part 6) -> XML

    /// <summary>
    /// Emits a Part 6 JSON-shaped <see cref="Json.Variant"/> back to its XML typed
    /// element.
    /// </summary>
    public static XmlElement? WriteVariantToXml(Json.Variant? input, VariantXmlContext ctx)
    {
        if (input?.Value == null) return null;

        var doc = new XmlDocument();
        var bt = (Json.BuiltInType)(input.UaType ?? (int)Json.BuiltInType.String);

        // Matrix
        if (input.Dimensions is { Count: > 1 } && input.Value is System.Collections.IEnumerable matrixItems)
        {
            var result = doc.CreateElement(UaPrefix, "Matrix", UaTypesXmlNamespace);
            var dimsEl = doc.CreateElement(UaPrefix, "Dimensions", UaTypesXmlNamespace);
            foreach (var dim in input.Dimensions)
            {
                var de = doc.CreateElement(UaPrefix, "Int32", UaTypesXmlNamespace);
                de.InnerText = dim.ToString(CultureInfo.InvariantCulture);
                dimsEl.AppendChild(de);
            }
            result.AppendChild(dimsEl);

            var elemsEl = doc.CreateElement(UaPrefix, "Elements", UaTypesXmlNamespace);
            var typeName = bt.ToString();
            foreach (var item in matrixItems)
            {
                var itemEl = WriteBuiltInOrStruct(doc, typeName, bt, item, ctx);
                if (itemEl != null) elemsEl.AppendChild(itemEl);
            }
            result.AppendChild(elemsEl);
            doc.AppendChild(result);
            return doc.DocumentElement;
        }

        // 1-D array
        if (IsArrayValue(input.Value, out var listItems))
        {
            var typeName = bt.ToString();
            var listEl = doc.CreateElement(UaPrefix, $"ListOf{typeName}", UaTypesXmlNamespace);
            foreach (var item in listItems!)
            {
                var itemEl = WriteBuiltInOrStruct(doc, typeName, bt, item, ctx);
                if (itemEl != null) listEl.AppendChild(itemEl);
            }
            doc.AppendChild(listEl);
            return doc.DocumentElement;
        }

        // Scalar
        var scalarEl = WriteBuiltInOrStruct(doc, bt.ToString(), bt, input.Value, ctx);
        if (scalarEl != null && scalarEl.OwnerDocument == doc && scalarEl.ParentNode == null)
            doc.AppendChild(scalarEl);
        return doc.DocumentElement;
    }

    private static bool IsArrayValue(object value, out System.Collections.IEnumerable? items)
    {
        if (value is JArray ja) { items = ja; return true; }
        if (value is string) { items = null; return false; }
        if (value is JObject) { items = null; return false; }
        if (value is JValue) { items = null; return false; }
        if (value is Json.ExtensionObject) { items = null; return false; }
        if (value is byte[]) { items = null; return false; }
        if (value is System.Collections.IList list) { items = list; return true; }
        items = null;
        return false;
    }

    private static XmlElement? WriteBuiltInOrStruct(
        XmlDocument doc, string typeName, Json.BuiltInType bt, object? value, VariantXmlContext ctx)
    {
        if (bt == Json.BuiltInType.ExtensionObject)
            return WriteExtensionObjectToXml(doc, value, ctx);
        return WriteBuiltInScalar(doc, typeName, bt, value, ctx);
    }

    private static XmlElement WriteBuiltInScalar(
        XmlDocument doc, string typeName, Json.BuiltInType bt, object? value, VariantXmlContext ctx)
    {
        var el = doc.CreateElement(UaPrefix, typeName, UaTypesXmlNamespace);
        var ci = CultureInfo.InvariantCulture;

        if (value is JValue jv) value = jv.Value;

        switch (bt)
        {
            case Json.BuiltInType.Boolean:
                el.InnerText = (value is bool b ? b : Convert.ToBoolean(value ?? false, ci)) ? "true" : "false";
                break;
            case Json.BuiltInType.DateTime:
                {
                    var dt = value is DateTime dd
                        ? dd
                        : (DateTime.TryParse(Convert.ToString(value, ci), CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : default);
                    el.InnerText = XmlConvert.ToString(dt, XmlDateTimeSerializationMode.Utc);
                    break;
                }
            case Json.BuiltInType.ByteString:
                {
                    byte[] bytes = value switch
                    {
                        byte[] bs => bs,
                        string s64 => TryFromBase64(s64),
                        _ => Array.Empty<byte>()
                    };
                    el.InnerText = Convert.ToBase64String(bytes);
                    break;
                }
            case Json.BuiltInType.Guid:
                {
                    var gs = value?.ToString() ?? "";
                    var inner = doc.CreateElement(UaPrefix, "String", UaTypesXmlNamespace);
                    inner.InnerText = gs;
                    el.AppendChild(inner);
                    break;
                }
            case Json.BuiltInType.NodeId:
            case Json.BuiltInType.ExpandedNodeId:
                {
                    var idEl = doc.CreateElement(UaPrefix, "Identifier", UaTypesXmlNamespace);
                    idEl.InnerText = ctx.NsuToNsIndex(Convert.ToString(value, ci) ?? "");
                    el.AppendChild(idEl);
                    break;
                }
            case Json.BuiltInType.StatusCode:
                {
                    uint code = 0;
                    string? sym = null;
                    if (value is JObject jo)
                    {
                        if (jo["Code"] is JValue codeVal && codeVal.Value != null)
                            uint.TryParse(codeVal.Value.ToString(), NumberStyles.Integer, ci, out code);
                        if (jo["Symbol"] is JValue symVal) sym = symVal.Value?.ToString();
                    }
                    else
                    {
                        uint.TryParse(Convert.ToString(value, ci), NumberStyles.Integer, ci, out code);
                    }
                    var codeEl = doc.CreateElement(UaPrefix, "Code", UaTypesXmlNamespace);
                    codeEl.InnerText = code.ToString(ci);
                    el.AppendChild(codeEl);
                    if (!string.IsNullOrEmpty(sym))
                    {
                        var symEl = doc.CreateElement(UaPrefix, "Symbol", UaTypesXmlNamespace);
                        symEl.InnerText = sym;
                        el.AppendChild(symEl);
                    }
                    break;
                }
            case Json.BuiltInType.QualifiedName:
                {
                    ParseQualifiedName(Convert.ToString(value, ci) ?? "", ctx, out var nsIdx, out var qnName);
                    var nsEl = doc.CreateElement(UaPrefix, "NamespaceIndex", UaTypesXmlNamespace);
                    nsEl.InnerText = nsIdx.ToString(ci);
                    el.AppendChild(nsEl);
                    var nameEl = doc.CreateElement(UaPrefix, "Name", UaTypesXmlNamespace);
                    nameEl.InnerText = qnName;
                    el.AppendChild(nameEl);
                    break;
                }
            case Json.BuiltInType.LocalizedText:
                {
                    // Per Part 6 §5.4 LocalizedText is {Locale?, Text?} — both optional.
                    // Track presence separately so a JSON `{Text:"..."}` doesn't round-trip
                    // to `{Locale:"", Text:"..."}` after reload.
                    string? locale = null;
                    string? text = null;
                    if (value is JObject jo)
                    {
                        if (jo["Locale"] is JToken localeTok && localeTok.Type != JTokenType.Null)
                            locale = localeTok.ToString();
                        if (jo["Text"] is JToken textTok && textTok.Type != JTokenType.Null)
                            text = textTok.ToString();
                    }
                    else if (value is Json.LocalizedText lt && lt.T is { Count: > 0 } && lt.T[0].Count > 1)
                    {
                        locale = lt.T[0][0];
                        text = lt.T[0][1];
                    }
                    else
                    {
                        text = Convert.ToString(value, ci);
                    }
                    if (locale != null)
                    {
                        var localeEl = doc.CreateElement(UaPrefix, "Locale", UaTypesXmlNamespace);
                        localeEl.InnerText = locale;
                        el.AppendChild(localeEl);
                    }
                    if (text != null)
                    {
                        var textEl = doc.CreateElement(UaPrefix, "Text", UaTypesXmlNamespace);
                        textEl.InnerText = text;
                        el.AppendChild(textEl);
                    }
                    break;
                }
            default:
                el.InnerText = Convert.ToString(value, ci) ?? "";
                break;
        }
        return el;
    }

    private static byte[] TryFromBase64(string s)
    {
        try { return Convert.FromBase64String(s); }
        catch { return Array.Empty<byte>(); }
    }

    private static void ParseQualifiedName(string text, VariantXmlContext ctx, out int nsIdx, out string name)
    {
        nsIdx = 0;
        name = text;
        if (text.StartsWith("nsu=", StringComparison.Ordinal))
        {
            var semi = text.IndexOf(';');
            if (semi > 4)
            {
                var uri = text.Substring(4, semi - 4);
                nsIdx = ctx.GetOrAppendNamespaceIndex(uri);
                name = text.Substring(semi + 1);
            }
            return;
        }
        var colon = text.IndexOf(':');
        if (colon > 0 && int.TryParse(text.Substring(0, colon), out var idx))
        {
            nsIdx = idx;
            name = text.Substring(colon + 1);
        }
    }

    /// <summary>
    /// Emits a <see cref="Json.ExtensionObject"/> (or its Part 6 JSON form
    /// <c>{UaTypeId, UaBody}</c> as a JObject) as a Part 6 <c>&lt;ExtensionObject&gt;</c>
    /// XML element.
    /// </summary>
    private static XmlElement? WriteExtensionObjectToXml(XmlDocument doc, object? value, VariantXmlContext ctx)
    {
        string? typeId;
        JObject? body;

        if (value is Json.ExtensionObject eoObj)
        {
            typeId = eoObj.TypeId;
            body = eoObj.Body as JObject;
        }
        else if (value is JObject jo)
        {
            // Part 6 JSON ExtensionObject wrapper: body fields inlined alongside UaTypeId.
            if (IsExtensionObjectWrapper(jo))
            {
                DecomposeExtensionObjectWrapper(jo, out typeId, out _, out body);
            }
            else
            {
                // Bare struct body.
                typeId = null;
                body = jo;
            }
        }
        else
        {
            return null;
        }

        var eoEl = doc.CreateElement(UaPrefix, nameof(Json.BuiltInType.ExtensionObject), UaTypesXmlNamespace);

        var xmlTypeId = ctx.ResolveXmlEncodingNodeId(typeId) ?? typeId;
        if (xmlTypeId != null)
        {
            var typeIdEl = doc.CreateElement(UaPrefix, "TypeId", UaTypesXmlNamespace);
            var idEl = doc.CreateElement(UaPrefix, "Identifier", UaTypesXmlNamespace);
            idEl.InnerText = ctx.NsuToNsIndex(xmlTypeId);
            typeIdEl.AppendChild(idEl);
            eoEl.AppendChild(typeIdEl);
        }

        if (body != null)
        {
            var bodyEl = doc.CreateElement(UaPrefix, "Body", UaTypesXmlNamespace);
            // For struct wrapper element naming, look up the DATA type's BrowseName,
            // not the encoding's. ExtensionObject TypeIds in stored Part 6 JSON can
            // reference the DataType directly OR the "Default XML"/"Default Binary"
            // encoding NodeId — normalize before the browse-name lookup so we pick
            // up "Argument" rather than "Default Binary" (which would also blow up
            // with an XmlException because the name has a space).
            var dataTypeIdForName = ctx.NormalizeEncodingNodeIdToDataType(typeId) ?? typeId;
            var modelXmlNs = ctx.ResolveXmlSchemaUri(dataTypeIdForName) ?? UaTypesXmlNamespace;
            var structName = ctx.ResolveDataTypeBrowseName(dataTypeIdForName) ?? "Body";
            var structEl = doc.CreateElement("t", EncodeNameForXml(structName), modelXmlNs);
            WriteFieldsFromJObject(doc, structEl, body, modelXmlNs, dataTypeIdForName, ctx);
            bodyEl.AppendChild(structEl);
            eoEl.AppendChild(bodyEl);
        }

        return eoEl;
    }

    /// <summary>
    /// Writes each property of <paramref name="body"/> as a child element of
    /// <paramref name="parent"/>. Field XML element names are the JSON keys.
    /// </summary>
    private static void WriteFieldsFromJObject(
        XmlDocument doc, XmlElement parent, JObject body, string xmlNs, string? dataTypeId, VariantXmlContext ctx)
    {
        var fields = ctx.ResolveStructureFields(dataTypeId);

        foreach (var prop in body.Properties())
        {
            var fieldDef = fields?.FirstOrDefault(f => f.Name == prop.Name);
            WriteField(doc, parent, prop.Name, prop.Value, xmlNs, fieldDef, ctx);
        }
    }

    private static void WriteField(
        XmlDocument doc, XmlElement parent, string fieldName, JToken? value, string xmlNs,
        Json.DataTypeField? fieldDef, VariantXmlContext ctx)
    {
        if (value == null || value.Type == JTokenType.Null)
        {
            var nilEl = doc.CreateElement("t", EncodeNameForXml(fieldName), xmlNs);
            nilEl.SetAttribute("nil", XsiNamespace, "true");
            parent.AppendChild(nilEl);
            return;
        }

        // Field-level ValueRank ≥ 1: emit one <FieldName> child per array element.
        if (value is JArray arr && fieldDef?.ValueRank is int rank && rank >= 1)
        {
            foreach (var item in arr)
            {
                WriteSingleFieldValue(doc, parent, fieldName, item, xmlNs, fieldDef, ctx);
            }
            return;
        }

        WriteSingleFieldValue(doc, parent, fieldName, value, xmlNs, fieldDef, ctx);
    }

    private static void WriteSingleFieldValue(
        XmlDocument doc, XmlElement parent, string fieldName, JToken value, string xmlNs,
        Json.DataTypeField? fieldDef, VariantXmlContext ctx)
    {
        var fieldEl = doc.CreateElement("t", EncodeNameForXml(fieldName), xmlNs);

        // ExtensionObject wrapper form (Part 6: body fields inlined alongside UaTypeId).
        if (value is JObject jo && IsExtensionObjectWrapper(jo))
        {
            var eoEl = WriteExtensionObjectToXml(doc, jo, ctx);
            if (eoEl != null) fieldEl.AppendChild(eoEl);
            parent.AppendChild(fieldEl);
            return;
        }

        // Variant wrapper form
        if (value is JObject vo && vo["UaType"] != null)
        {
            var inner = ReconstructVariantFromJson(vo);
            // WriteVariantToXml creates its own XmlDocument, so the returned
            // node is owned by a different document. Import it into ours
            // before appending — XmlNode.AppendChild rejects cross-document
            // adoption with "node is from a different document context".
            var innerXml = WriteVariantToXml(inner, ctx);
            if (innerXml != null)
            {
                var imported = doc.ImportNode(innerXml, deep: true);
                fieldEl.AppendChild(imported);
            }
            parent.AppendChild(fieldEl);
            return;
        }

        // Schema known: route through the field DataType.
        if (fieldDef?.DataType != null && ctx.IsBuiltInDataType(fieldDef.DataType, out var bt))
        {
            // LocalizedText / StatusCode / NodeId / QualifiedName / etc. round-trip
            // their JSON-shaped value via WriteBuiltInScalar pattern, but inside a struct
            // body the *element* uses the field name (no extra type wrapper).
            WriteBuiltInIntoFieldElement(doc, fieldEl, bt, value, ctx);
            parent.AppendChild(fieldEl);
            return;
        }

        // Schema known: nested concrete struct → wrap with the inner type element.
        if (value is JObject nested && fieldDef?.DataType != null
            && ctx.ResolveDataTypeBrowseName(fieldDef.DataType) is string innerTypeName)
        {
            var innerNs = ctx.ResolveXmlSchemaUri(fieldDef.DataType) ?? xmlNs;
            var innerEl = doc.CreateElement("t", EncodeNameForXml(innerTypeName), innerNs);
            WriteFieldsFromJObject(doc, innerEl, nested, innerNs, fieldDef.DataType, ctx);
            fieldEl.AppendChild(innerEl);
            parent.AppendChild(fieldEl);
            return;
        }

        // Schema-free fallbacks:
        if (value is JValue prim)
        {
            fieldEl.InnerText = JTokenToInvariantString(prim);
            parent.AppendChild(fieldEl);
            return;
        }
        if (value is JObject objVal)
        {
            // Compound built-in shape recognition (LocalizedText, StatusCode, …).
            if (objVal["Locale"] != null || objVal["Text"] != null)
            {
                if (objVal["Locale"] != null)
                {
                    var localeEl = doc.CreateElement("t", "Locale", xmlNs);
                    localeEl.InnerText = objVal["Locale"]?.ToString() ?? "";
                    fieldEl.AppendChild(localeEl);
                }
                if (objVal["Text"] != null)
                {
                    var textEl = doc.CreateElement("t", "Text", xmlNs);
                    textEl.InnerText = objVal["Text"]?.ToString() ?? "";
                    fieldEl.AppendChild(textEl);
                }
                parent.AppendChild(fieldEl);
                return;
            }
            if (objVal["Code"] != null && objVal.Count <= 2)
            {
                var codeEl = doc.CreateElement("t", "Code", xmlNs);
                codeEl.InnerText = objVal["Code"]?.ToString() ?? "0";
                fieldEl.AppendChild(codeEl);
                if (objVal["Symbol"] != null)
                {
                    var symEl = doc.CreateElement("t", "Symbol", xmlNs);
                    symEl.InnerText = objVal["Symbol"]?.ToString() ?? "";
                    fieldEl.AppendChild(symEl);
                }
                parent.AppendChild(fieldEl);
                return;
            }
            // Plain struct body — emit fields inline (no inner type wrapper).
            WriteFieldsFromJObject(doc, fieldEl, objVal, xmlNs, dataTypeId: null, ctx);
            parent.AppendChild(fieldEl);
            return;
        }
        if (value is JArray a)
        {
            // Array without ValueRank → emit each item under the same field-name element.
            foreach (var item in a)
            {
                WriteSingleFieldValue(doc, parent, fieldName, item, xmlNs, fieldDef: null, ctx);
            }
            return;
        }

        // Default: stringify.
        fieldEl.InnerText = JTokenToInvariantString(value);
        parent.AppendChild(fieldEl);
    }

    /// <summary>
    /// Writes the JSON value of a built-in-typed field directly into the field element,
    /// without an extra type-name wrapper. e.g. for a NodeId-typed field <c>P</c> with JSON
    /// <c>"i=42"</c>, emits <c>&lt;P&gt;&lt;Identifier&gt;i=42&lt;/Identifier&gt;&lt;/P&gt;</c>.
    /// </summary>
    private static void WriteBuiltInIntoFieldElement(
        XmlDocument doc, XmlElement fieldEl, Json.BuiltInType bt, JToken value, VariantXmlContext ctx)
    {
        var ci = CultureInfo.InvariantCulture;
        if (value is JValue jv) { /* fall through */ }

        switch (bt)
        {
            case Json.BuiltInType.Boolean:
            case Json.BuiltInType.SByte:
            case Json.BuiltInType.Byte:
            case Json.BuiltInType.Int16:
            case Json.BuiltInType.UInt16:
            case Json.BuiltInType.Int32:
            case Json.BuiltInType.UInt32:
            case Json.BuiltInType.Int64:
            case Json.BuiltInType.UInt64:
            case Json.BuiltInType.Float:
            case Json.BuiltInType.Double:
            case Json.BuiltInType.String:
            case Json.BuiltInType.DateTime:
            case Json.BuiltInType.ByteString:
            case Json.BuiltInType.XmlElement:
                fieldEl.InnerText = JTokenToInvariantString(value);
                break;
            case Json.BuiltInType.Guid:
                {
                    var s = doc.CreateElement(UaPrefix, "String", UaTypesXmlNamespace);
                    s.InnerText = JTokenToInvariantString(value);
                    fieldEl.AppendChild(s);
                    break;
                }
            case Json.BuiltInType.NodeId:
            case Json.BuiltInType.ExpandedNodeId:
                {
                    var id = doc.CreateElement(UaPrefix, "Identifier", UaTypesXmlNamespace);
                    id.InnerText = ctx.NsuToNsIndex(JTokenToInvariantString(value));
                    fieldEl.AppendChild(id);
                    break;
                }
            case Json.BuiltInType.QualifiedName:
                {
                    ParseQualifiedName(JTokenToInvariantString(value), ctx, out var nsIdx, out var name);
                    var nsEl = doc.CreateElement(UaPrefix, "NamespaceIndex", UaTypesXmlNamespace);
                    nsEl.InnerText = nsIdx.ToString(ci);
                    fieldEl.AppendChild(nsEl);
                    var nameEl = doc.CreateElement(UaPrefix, "Name", UaTypesXmlNamespace);
                    nameEl.InnerText = name;
                    fieldEl.AppendChild(nameEl);
                    break;
                }
            case Json.BuiltInType.LocalizedText:
                {
                    // Same optional-field semantics as the top-level LocalizedText writer.
                    string? locale = null;
                    string? text = null;
                    if (value is JObject jo)
                    {
                        if (jo["Locale"] is JToken lt && lt.Type != JTokenType.Null) locale = lt.ToString();
                        if (jo["Text"] is JToken tt && tt.Type != JTokenType.Null) text = tt.ToString();
                    }
                    else
                    {
                        text = JTokenToInvariantString(value);
                    }
                    if (locale != null)
                    {
                        var localeEl = doc.CreateElement(UaPrefix, "Locale", UaTypesXmlNamespace);
                        localeEl.InnerText = locale;
                        fieldEl.AppendChild(localeEl);
                    }
                    if (text != null)
                    {
                        var textEl = doc.CreateElement(UaPrefix, "Text", UaTypesXmlNamespace);
                        textEl.InnerText = text;
                        fieldEl.AppendChild(textEl);
                    }
                    break;
                }
            case Json.BuiltInType.StatusCode:
                {
                    uint code = 0;
                    string? sym = null;
                    if (value is JObject jo)
                    {
                        if (jo["Code"] != null) uint.TryParse(jo["Code"]?.ToString(), NumberStyles.Integer, ci, out code);
                        sym = jo["Symbol"]?.ToString();
                    }
                    else
                    {
                        uint.TryParse(JTokenToInvariantString(value), NumberStyles.Integer, ci, out code);
                    }
                    var codeEl = doc.CreateElement(UaPrefix, "Code", UaTypesXmlNamespace);
                    codeEl.InnerText = code.ToString(ci);
                    fieldEl.AppendChild(codeEl);
                    if (!string.IsNullOrEmpty(sym))
                    {
                        var symEl = doc.CreateElement(UaPrefix, "Symbol", UaTypesXmlNamespace);
                        symEl.InnerText = sym;
                        fieldEl.AppendChild(symEl);
                    }
                    break;
                }
            default:
                fieldEl.InnerText = JTokenToInvariantString(value);
                break;
        }
    }

    private static Json.Variant ReconstructVariantFromJson(JObject vo)
    {
        int uaType = (int)Json.BuiltInType.String;
        if (vo["UaType"] is JValue tv && tv.Value != null)
            int.TryParse(tv.Value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uaType);

        object? value = vo["Value"] is JToken vTok ? UnboxJToken(vTok) : null;

        List<int>? dims = null;
        if (vo["Dimensions"] is JArray da)
        {
            dims = new List<int>();
            foreach (var d in da)
                if (int.TryParse(d.ToString(), out var di)) dims.Add(di);
        }
        return new Json.Variant(uaType, value, dims);
    }

    private static object? UnboxJToken(JToken t)
    {
        if (t is JValue jv) return jv.Value;
        if (t is JArray ja)
        {
            var list = new List<object?>();
            foreach (var item in ja) list.Add(UnboxJToken(item));
            return list;
        }
        return t; // JObject — let downstream code handle
    }

    private static string JTokenToInvariantString(JToken? t)
    {
        if (t == null) return "";
        if (t is JValue jv)
        {
            if (jv.Value is bool b) return b ? "true" : "false";
            if (jv.Value is DateTime dt) return XmlConvert.ToString(dt, XmlDateTimeSerializationMode.Utc);
            if (jv.Value is DateTimeOffset dto) return XmlConvert.ToString(dto.UtcDateTime, XmlDateTimeSerializationMode.Utc);
            if (jv.Value is IFormattable f) return f.ToString(null, CultureInfo.InvariantCulture);
            return jv.Value?.ToString() ?? "";
        }
        return t.ToString(Newtonsoft.Json.Formatting.None);
    }

    #endregion

    #region Built-in type name mapping

    private static bool IsBuiltInTypeName(string name, out Json.BuiltInType type) =>
        Enum.TryParse(name, out type);

    #endregion
}

/// <summary>
/// Carries context needed for Variant XML ↔ Part 6 JSON conversion.
/// </summary>
internal sealed class VariantXmlContext
{
    private readonly ServiceMessageContext? _nsContext;
    private readonly AddressSpace? _addressSpace;

    public VariantXmlContext(ServiceMessageContext? nsContext, AddressSpace? addressSpace = null)
    {
        _nsContext = nsContext;
        _addressSpace = addressSpace;
    }

    public string NsIndexToNsu(string nodeId)
    {
        if (_nsContext == null) return nodeId;
        if (nodeId.StartsWith("ns=", StringComparison.Ordinal))
        {
            var semi = nodeId.IndexOf(';');
            if (semi > 3 && int.TryParse(nodeId.AsSpan(3, semi - 3), out var idx))
            {
                var uri = _nsContext.NamespaceUris.GetString(idx);
                if (uri != null) return $"nsu={uri};{nodeId[(semi + 1)..]}";
            }
        }
        return nodeId;
    }

    public string NsuToNsIndex(string nodeId)
    {
        if (_nsContext == null) return nodeId;
        if (!nodeId.StartsWith("nsu=", StringComparison.Ordinal)) return nodeId;
        var semi = nodeId.IndexOf(';');
        if (semi <= 4) return nodeId;
        var uri = nodeId.Substring(4, semi - 4);
        var idx = _nsContext.NamespaceUris.GetIndexOrAppend(uri);
        var rest = nodeId.Substring(semi + 1);
        return idx == 0 ? rest : $"ns={idx};{rest}";
    }

    public string? GetNamespaceUri(int index) => _nsContext?.NamespaceUris.GetString(index);

    public int GetOrAppendNamespaceIndex(string uri) =>
        _nsContext != null ? (int)_nsContext.NamespaceUris.GetIndexOrAppend(uri) : 0;

    public string? ResolveXmlEncodingNodeId(string? dataTypeNodeId)
    {
        if (_addressSpace == null || dataTypeNodeId == null) return null;
        return _addressSpace.TryGetXmlEncodingNodeId(dataTypeNodeId);
    }

    /// <summary>
    /// If <paramref name="nodeId"/> is an encoding NodeId (Default XML / Default JSON /
    /// Default Binary), returns the underlying DataType NodeId; otherwise returns
    /// <paramref name="nodeId"/> unchanged. Used by the Variant XML writer to look up
    /// struct wrapper element names against the DataType, not the encoding object.
    /// </summary>
    public string? NormalizeEncodingNodeIdToDataType(string? nodeId)
    {
        if (_addressSpace == null) return nodeId;
        return _addressSpace.NormalizeEncodingNodeIdToDataType(nodeId);
    }

    public string? ResolveXmlSchemaUri(string? dataTypeNodeId)
    {
        if (_addressSpace == null || dataTypeNodeId == null) return null;
        return _addressSpace.TryGetXmlSchemaUriForNodeId(dataTypeNodeId);
    }

    public string? ResolveDataTypeBrowseName(string? dataTypeNodeId)
    {
        if (_addressSpace == null || dataTypeNodeId == null) return null;
        return _addressSpace.TryGetDataTypeBrowseName(dataTypeNodeId);
    }

    /// <summary>
    /// Returns the merged inherited+own field list for the given DataType, or null when
    /// no AddressSpace is attached or the type is unresolvable.
    /// </summary>
    public List<Json.DataTypeField>? ResolveStructureFields(string? dataTypeNodeId)
    {
        if (_addressSpace == null || dataTypeNodeId == null) return null;
        return _addressSpace.TryGetAllStructureFields(dataTypeNodeId);
    }

    /// <summary>
    /// True when the DataType is (or transitively subtypes) a Part 6 SCALAR
    /// built-in type whose JSON value can be written inline into a struct field
    /// element. The range is i=1..i=21 — i=22 (Structure abstract), i=23
    /// (DataValue) and i=24 (Variant/BaseDataType) are deliberately excluded:
    /// concrete subtypes of Structure are user-defined structures that need to
    /// recurse as nested XML elements, and a field literally typed as
    /// Structure / Variant arrives wrapped (ExtensionObject / Variant JSON
    /// shapes) and is intercepted earlier in the field-write call chain.
    /// </summary>
    public bool IsBuiltInDataType(string dataTypeNodeId, out Json.BuiltInType bt)
    {
        bt = default;
        if (string.IsNullOrEmpty(dataTypeNodeId)) return false;

        if (dataTypeNodeId.StartsWith("i=", StringComparison.Ordinal)
            && int.TryParse(dataTypeNodeId.AsSpan(2), out var n)
            && n >= 1 && n <= 21)
        {
            bt = (Json.BuiltInType)n;
            return true;
        }

        // Walk the supertype chain. Match only on a scalar built-in (1..21);
        // hitting i=22 just means "this is some Structure subtype" and the
        // caller should take the nested-struct path instead.
        if (_addressSpace == null) return false;
        var resolved = _addressSpace.NormalizeEncodingNodeIdToDataType(dataTypeNodeId);
        var current = resolved;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (current != null && seen.Add(current))
        {
            if (current.StartsWith("i=", StringComparison.Ordinal)
                && int.TryParse(current.AsSpan(2), out var nn) && nn >= 1 && nn <= 21)
            {
                bt = (Json.BuiltInType)nn;
                return true;
            }
            current = _addressSpace.TryGetSuperTypeId(current);
        }
        return false;
    }
}
