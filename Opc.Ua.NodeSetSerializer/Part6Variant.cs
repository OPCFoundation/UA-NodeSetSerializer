using System.IO;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Json = Opc.Ua.NodeSetSerializer.Model;

namespace Opc.Ua.NodeSetSerializer;

/// <summary>
/// Public facade over <see cref="VariantConverter"/> for converting OPC UA Variable
/// values between Part 6 XML and Part 6 JSON shapes. The facade speaks JSON TEXT at
/// the boundary so callers using <c>System.Text.Json</c> don't have to take a
/// Newtonsoft dependency.
/// </summary>
public static class Part6Variant
{
    /// <summary>
    /// Reads an OPC UA Value XML element (e.g. <c>&lt;String&gt;…&lt;/String&gt;</c>,
    /// <c>&lt;ExtensionObject&gt;…&lt;/&gt;</c>, <c>&lt;ListOfInt32&gt;…&lt;/&gt;</c>) into
    /// the Part 6 JSON value form, returned as JSON text. Returns null when input is
    /// null or unparseable.
    ///
    /// <para>Schema-free: pass null for <paramref name="addressSpace"/>. Schema-aware:
    /// pass the AddressSpace holding the relevant DataTypeDefinitions; the result will
    /// already be canonicalized for known DataTypes.</para>
    /// </summary>
    public static string? ReadXmlValueAsJsonText(XmlElement? xml, AddressSpace? addressSpace = null)
    {
        var token = ReadXmlValueAsToken(xml, addressSpace);
        return token == null ? null : token.ToString(Newtonsoft.Json.Formatting.None);
    }

    /// <summary>
    /// Same as <see cref="ReadXmlValueAsJsonText"/> but returns the raw Newtonsoft
    /// <see cref="JToken"/> (skipping the text round-trip). For internal callers that
    /// already use Newtonsoft.
    /// </summary>
    public static JToken? ReadXmlValueAsToken(XmlElement? xml, AddressSpace? addressSpace = null)
    {
        if (xml == null) return null;
        var ctx = new VariantXmlContext(new ServiceMessageContext(), addressSpace);
        var v = VariantConverter.ReadVariantFromXml(xml, ctx);
        return ToPart6JsonValue(v);
    }

    /// <summary>
    /// Inverse of <see cref="ReadXmlValueAsJsonText"/>. Renders a Part 6 JSON text
    /// value back to the OPC UA Value XML element. The Part 6 JSON shape carries
    /// enough information to determine the target XML element name for built-ins;
    /// for Structures the AddressSpace must contain the DataTypeDefinition pointed
    /// at by <c>UaTypeId</c>.
    /// </summary>
    public static XmlElement? WriteJsonTextValue(
        string? jsonText, AddressSpace? addressSpace = null,
        string? dataTypeNodeId = null, IReadOnlyList<string>? namespaceUris = null)
    {
        if (string.IsNullOrEmpty(jsonText) || jsonText == "null") return null;
        JToken token;
        try
        {
            // Newtonsoft's default DateParseHandling=DateTime auto-parses ISO date
            // strings into CLR DateTime values, which then re-format culture-aware
            // when stringified. Part 6 DateTime is a string — keep it as a string
            // through the parser so round-trip is byte-stable.
            using var reader = new JsonTextReader(new StringReader(jsonText))
            {
                DateParseHandling = DateParseHandling.None,
            };
            token = JToken.Load(reader);
        }
        catch { return null; }
        return WriteJsonValue(token, addressSpace, dataTypeNodeId, namespaceUris);
    }

    /// <summary>
    /// Same as <see cref="WriteJsonTextValue"/> but takes a Newtonsoft <see cref="JToken"/>
    /// directly. For internal callers that already use Newtonsoft.
    ///
    /// <para>When <paramref name="dataTypeNodeId"/> is supplied, the top-level value is
    /// typed according to the variable's declared DataType — this lets a JSON string
    /// like <c>"1:Lock"</c> render as <c>&lt;QualifiedName&gt;</c> instead of falling
    /// through to <c>&lt;String&gt;</c> via JSON-shape inference.</para>
    ///
    /// <para>When <paramref name="namespaceUris"/> is supplied, the writer's
    /// <c>nsu=URI;rest</c> → <c>ns=N;rest</c> conversion uses those indices, so the
    /// emitted XML round-trips through a NodeSet whose <c>NamespaceUris</c> table
    /// is in the same order. Required for any value containing NodeId / ExpandedNodeId
    /// / QualifiedName references — including identifier-only references whose
    /// namespace isn't a RequiredModel of the NodeSet.</para>
    /// </summary>
    public static XmlElement? WriteJsonValue(
        JToken? part6Value, AddressSpace? addressSpace = null,
        string? dataTypeNodeId = null, IReadOnlyList<string>? namespaceUris = null)
    {
        if (part6Value == null || part6Value.Type == JTokenType.Null) return null;

        var msgContext = new ServiceMessageContext();
        if (namespaceUris != null)
        {
            foreach (var uri in namespaceUris)
            {
                if (string.IsNullOrEmpty(uri)) continue;
                msgContext.NamespaceUris.GetIndexOrAppend(uri);
            }
        }

        var ctx = new VariantXmlContext(msgContext, addressSpace);
        var variant = FromPart6JsonValue(part6Value, ctx, dataTypeNodeId);
        return variant == null ? null : VariantConverter.WriteVariantToXml(variant, ctx);
    }

    /// <summary>
    /// Converts an internal <see cref="Json.Variant"/> (the schema-free pass-1 output)
    /// to a single Part 6 JSON token. Built-in scalars are emitted directly; arrays as
    /// JSON arrays; ExtensionObjects with their inline-fields wrapper.
    /// </summary>
    private static JToken? ToPart6JsonValue(Json.Variant? v)
    {
        if (v?.Value == null) return null;

        // Matrix
        if (v.Dimensions is { Count: > 1 } && v.Value is System.Collections.IEnumerable matrixItems)
        {
            var arr = new JArray();
            foreach (var item in matrixItems) arr.Add(ScalarToToken(item));
            var dims = new JArray();
            foreach (var d in v.Dimensions) dims.Add(d);
            return new JObject { ["Array"] = arr, ["Dimensions"] = dims };
        }

        // 1-D array
        if (IsArrayValue(v.Value, out var listItems))
        {
            var arr = new JArray();
            foreach (var item in listItems!) arr.Add(ScalarToToken(item));
            return arr;
        }

        // Scalar
        return ScalarToToken(v.Value);
    }

    private static JToken ScalarToToken(object? value)
    {
        if (value == null) return JValue.CreateNull();
        if (value is JToken jt) return jt;
        if (value is Json.ExtensionObject eo)
        {
            // Part 6 inline-fields ExtensionObject: {UaTypeId, ...body fields}.
            var obj = new JObject();
            if (eo.TypeId != null) obj["UaTypeId"] = eo.TypeId;
            if (eo.Encoding is byte enc && enc != 0)
            {
                obj["UaEncoding"] = enc;
                obj["UaBody"] = eo.Body?.ToString();
            }
            else if (eo.Body is JObject body)
            {
                foreach (var prop in body.Properties())
                {
                    if (prop.Name is "UaTypeId" or "UaEncoding" or "UaBody") continue;
                    obj[prop.Name] = prop.Value;
                }
            }
            return obj;
        }
        return new JValue(value);
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

    /// <summary>
    /// Inverse: take a Part 6 JSON token and reconstruct an internal Variant the writer
    /// can serialize. The Variant's UaType is inferred from the JSON shape:
    /// object with UaTypeId → ExtensionObject; object with Locale/Text → LocalizedText;
    /// number → Int32 or Double; bool → Boolean; string → String (with NodeId/Guid
    /// pattern recognition deferred to the writer's heuristics).
    /// </summary>
    /// <summary>
    /// Infers the OPC UA <see cref="Json.BuiltInType"/> for a value based on (a) its
    /// declared DataType (when known and resolvable through the AddressSpace) or
    /// (b) its JSON shape. The DataType always wins when available — a JSON string
    /// for a QualifiedName field stays a QualifiedName, not a String.
    /// </summary>
    private static Json.Variant? FromPart6JsonValue(
        JToken value, VariantXmlContext? ctx = null, string? dataTypeNodeId = null)
    {
        // If the caller knows the DataType and it resolves to a Part 6 built-in,
        // honour that directly — JSON-shape inference can't tell QualifiedName from
        // String, NodeId from String, etc.
        if (ctx != null && !string.IsNullOrEmpty(dataTypeNodeId)
            && ctx.IsBuiltInDataType(dataTypeNodeId, out var declaredBt))
        {
            return BuildVariantWithDeclaredType(value, declaredBt);
        }

        switch (value.Type)
        {
            case JTokenType.Boolean:
                return new Json.Variant((int)Json.BuiltInType.Boolean, ((JValue)value).Value);
            case JTokenType.Integer:
                {
                    // Pick the smallest built-in that holds the value without loss.
                    // Per Part 6 §5.4, Int64/UInt64 are JSON strings — so a JSON integer
                    // here can't exceed UInt32 range from a Part-6-compliant source.
                    // Negative values that fit in Int32 stay Int32; values up to UInt32
                    // promote to UInt32; the rare > UInt32 (legacy/non-compliant input)
                    // promotes to Int64.
                    var iv = ((JValue)value).Value;
                    long n = iv switch
                    {
                        long l => l,
                        int i => i,
                        short s => s,
                        sbyte sb => sb,
                        ulong u => unchecked((long)u),
                        uint ui => ui,
                        ushort us => us,
                        byte b => b,
                        _ => Convert.ToInt64(iv, System.Globalization.CultureInfo.InvariantCulture),
                    };
                    Json.BuiltInType bt;
                    object boxed;
                    if (n >= int.MinValue && n <= int.MaxValue) { bt = Json.BuiltInType.Int32; boxed = (int)n; }
                    else if (n >= 0 && n <= uint.MaxValue) { bt = Json.BuiltInType.UInt32; boxed = (uint)n; }
                    else { bt = Json.BuiltInType.Int64; boxed = n; }
                    return new Json.Variant((int)bt, boxed);
                }
            case JTokenType.Float:
                return new Json.Variant((int)Json.BuiltInType.Double, ((JValue)value).Value);
            case JTokenType.String:
                return new Json.Variant((int)Json.BuiltInType.String, ((JValue)value).Value);
            case JTokenType.Null:
                return null;

            case JTokenType.Array:
                {
                    // Array of items — element type inferred from first element.
                    var arr = (JArray)value;
                    if (arr.Count == 0)
                    {
                        return new Json.Variant((int)Json.BuiltInType.String, new System.Collections.Generic.List<object?>());
                    }
                    var firstBt = ScalarBuiltInType(arr[0]);
                    var list = new System.Collections.Generic.List<object?>();
                    foreach (var item in arr) list.Add(ScalarValue(item));
                    return new Json.Variant((int)firstBt, list);
                }

            case JTokenType.Object:
                {
                    var obj = (JObject)value;
                    // Matrix
                    if (obj["Array"] is JArray matrixArr && obj["Dimensions"] is JArray matrixDims)
                    {
                        var items = new System.Collections.Generic.List<object?>();
                        foreach (var item in matrixArr) items.Add(ScalarValue(item));
                        var dims = new System.Collections.Generic.List<int>();
                        foreach (var d in matrixDims)
                            if (int.TryParse(d.ToString(), out var di)) dims.Add(di);
                        var elemBt = matrixArr.Count > 0 ? ScalarBuiltInType(matrixArr[0]) : Json.BuiltInType.String;
                        return new Json.Variant((int)elemBt, items, dims);
                    }
                    // ExtensionObject (Part 6 inline-fields wrapper)
                    if (obj["UaTypeId"] != null)
                    {
                        var typeId = obj["UaTypeId"]?.ToString();
                        byte? encoding = null;
                        if (obj["UaEncoding"] is JValue ev && ev.Value != null
                            && byte.TryParse(ev.Value.ToString(), out var enc) && enc != 0)
                            encoding = enc;
                        object? body;
                        if (encoding is byte eb && eb != 0)
                            body = obj["UaBody"]?.ToString();
                        else
                        {
                            var bodyObj = new JObject();
                            foreach (var prop in obj.Properties())
                            {
                                if (prop.Name is "UaTypeId" or "UaEncoding" or "UaBody") continue;
                                bodyObj.Add(prop.Name, prop.Value);
                            }
                            body = bodyObj;
                        }
                        return new Json.Variant((int)Json.BuiltInType.ExtensionObject,
                            new Json.ExtensionObject { TypeId = typeId, Encoding = encoding, Body = body });
                    }
                    // LocalizedText (no further heuristics — let the writer disambiguate)
                    if (obj["Locale"] != null || obj["Text"] != null)
                    {
                        return new Json.Variant((int)Json.BuiltInType.LocalizedText, obj);
                    }
                    // StatusCode
                    if (obj["Code"] != null && obj.Count <= 2)
                    {
                        return new Json.Variant((int)Json.BuiltInType.StatusCode, obj);
                    }
                    // Bare struct body — no UaTypeId, no recognized compound. Treat as
                    // ExtensionObject with null TypeId; the writer will need schema to emit it.
                    return new Json.Variant((int)Json.BuiltInType.ExtensionObject,
                        new Json.ExtensionObject { TypeId = null, Body = obj });
                }
            default:
                return new Json.Variant((int)Json.BuiltInType.String, value.ToString());
        }
    }

    /// <summary>
    /// Builds a <see cref="Json.Variant"/> using the declared <paramref name="bt"/> as
    /// the UaType, regardless of the JSON value's shape. Used when the variable's
    /// DataType is known up front. Arrays apply the type element-wise; objects
    /// (compound built-ins like LocalizedText/StatusCode) and ExtensionObject
    /// wrappers pass through unmodified.
    /// </summary>
    private static Json.Variant BuildVariantWithDeclaredType(JToken value, Json.BuiltInType bt)
    {
        // Arrays — element-wise: each item gets the declared type.
        if (value is JArray arr)
        {
            var list = new System.Collections.Generic.List<object?>();
            foreach (var item in arr) list.Add(ScalarValue(item));
            return new Json.Variant((int)bt, list);
        }
        // Matrix — Part 6 {Array, Dimensions}.
        if (value is JObject mObj
            && mObj["Array"] is JArray mArr && mObj["Dimensions"] is JArray mDims)
        {
            var items = new System.Collections.Generic.List<object?>();
            foreach (var item in mArr) items.Add(ScalarValue(item));
            var dims = new System.Collections.Generic.List<int>();
            foreach (var d in mDims)
                if (int.TryParse(d.ToString(), out var di)) dims.Add(di);
            return new Json.Variant((int)bt, items, dims);
        }
        // Scalar — pass the underlying value through; the writer's WriteBuiltInScalar
        // dispatches on bt so the shape (string for QualifiedName/NodeId, object for
        // LocalizedText/StatusCode, etc.) is interpreted correctly.
        return new Json.Variant((int)bt, ScalarValue(value));
    }

    private static Json.BuiltInType ScalarBuiltInType(JToken t)
    {
        switch (t.Type)
        {
            case JTokenType.Boolean: return Json.BuiltInType.Boolean;
            case JTokenType.Integer:
                {
                    // Match the size-guarded promotion in FromPart6JsonValue so the array's
                    // ListOf<T> XML element name matches the actual element values.
                    var iv = ((JValue)t).Value;
                    long n = iv switch
                    {
                        long l => l,
                        int i => i,
                        short s => s,
                        sbyte sb => sb,
                        ulong u => unchecked((long)u),
                        uint ui => ui,
                        ushort us => us,
                        byte b => b,
                        _ => Convert.ToInt64(iv, System.Globalization.CultureInfo.InvariantCulture),
                    };
                    if (n >= int.MinValue && n <= int.MaxValue) return Json.BuiltInType.Int32;
                    if (n >= 0 && n <= uint.MaxValue) return Json.BuiltInType.UInt32;
                    return Json.BuiltInType.Int64;
                }
            case JTokenType.Float: return Json.BuiltInType.Double;
            case JTokenType.String: return Json.BuiltInType.String;
            case JTokenType.Object:
                {
                    var obj = (JObject)t;
                    if (obj["UaTypeId"] != null) return Json.BuiltInType.ExtensionObject;
                    if (obj["Locale"] != null || obj["Text"] != null) return Json.BuiltInType.LocalizedText;
                    if (obj["Code"] != null && obj.Count <= 2) return Json.BuiltInType.StatusCode;
                    if (obj["Array"] is JArray && obj["Dimensions"] is JArray) return Json.BuiltInType.Variant;
                    if (obj["UaType"] != null) return Json.BuiltInType.Variant;
                    return Json.BuiltInType.ExtensionObject;
                }
            default: return Json.BuiltInType.String;
        }
    }

    private static object? ScalarValue(JToken t)
    {
        if (t is JObject obj)
        {
            if (obj["UaTypeId"] != null) return UnwrapEoFromJson(obj);
            // LocalizedText / StatusCode pass through as JObject — WriteBuiltInScalar's
            // dedicated cases handle them.
            return obj;
        }
        return t is JValue jv ? jv.Value : (object)t;
    }

    private static Json.ExtensionObject UnwrapEoFromJson(JObject obj)
    {
        var typeId = obj["UaTypeId"]?.ToString();
        var bodyObj = new JObject();
        foreach (var prop in obj.Properties())
        {
            if (prop.Name is "UaTypeId" or "UaEncoding" or "UaBody") continue;
            bodyObj.Add(prop.Name, prop.Value);
        }
        return new Json.ExtensionObject { TypeId = typeId, Body = bodyObj };
    }
}
