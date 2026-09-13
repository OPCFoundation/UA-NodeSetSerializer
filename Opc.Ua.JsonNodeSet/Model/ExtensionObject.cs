using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Opc.Ua.JsonNodeSet.Model;

/// <summary>
/// Part 6 §5.4 JSON ExtensionObject.
///
/// <para>For the default (JSON inline) encoding the structure body fields are emitted
/// at the SAME level as <c>UaTypeId</c> — there is no <c>UaBody</c> wrapper:</para>
/// <code>
/// { "UaTypeId": "ns=...;i=42", "FieldA": ..., "FieldB": ... }
/// </code>
///
/// <para><c>UaBody</c> is reserved for non-JSON encodings:</para>
/// <list type="bullet">
///   <item><c>UaEncoding=1</c> — Binary: <c>UaBody</c> holds the base64-encoded body bytes.</item>
///   <item><c>UaEncoding=2</c> — XML: <c>UaBody</c> holds the XML body string.</item>
///   <item>Default / 0 — JSON inline: body fields are spread into the wrapper.</item>
/// </list>
/// </summary>
[JsonConverter(typeof(ExtensionObjectJsonConverter))]
[DataContract]
public class ExtensionObject
{
    /// <summary>The DataType NodeId of the body's structure.</summary>
    public string? TypeId { get; set; }

    /// <summary>0/null = JSON inline (default), 1 = Binary, 2 = XML.</summary>
    public byte? Encoding { get; set; }

    /// <summary>
    /// For JSON-inline encoding (default): a <see cref="JObject"/> containing the
    /// structure's fields. For Binary/XML encodings: a <see cref="string"/> carrying the
    /// base64 / XML-encoded body.
    /// </summary>
    public object? Body { get; set; }
}

internal sealed class ExtensionObjectJsonConverter : JsonConverter<ExtensionObject>
{
    private static readonly HashSet<string> ReservedKeys = new(StringComparer.Ordinal)
    {
        "UaTypeId", "UaEncoding", "UaBody",
    };

    public override ExtensionObject? ReadJson(
        JsonReader reader, Type objectType, ExtensionObject? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null) return null;
        var jo = JObject.Load(reader);

        var typeId = jo["UaTypeId"]?.ToString();
        byte? encoding = null;
        if (jo["UaEncoding"] is JValue encVal && encVal.Value != null
            && byte.TryParse(encVal.Value.ToString(), out var encByte))
        {
            encoding = encByte == 0 ? (byte?)null : encByte;
        }

        object? body;
        if (encoding is null or 0)
        {
            // JSON inline encoding — body fields are at the same level as UaTypeId.
            var bodyObj = new JObject();
            foreach (var prop in jo.Properties())
            {
                if (ReservedKeys.Contains(prop.Name)) continue;
                bodyObj.Add(prop.Name, prop.Value);
            }
            body = bodyObj.Count > 0 ? bodyObj : null;
        }
        else
        {
            // Binary / XML — body is the UaBody string.
            body = jo["UaBody"]?.ToString();
        }

        return new ExtensionObject { TypeId = typeId, Encoding = encoding, Body = body };
    }

    public override void WriteJson(JsonWriter writer, ExtensionObject? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }

        writer.WriteStartObject();

        if (value.TypeId != null)
        {
            writer.WritePropertyName("UaTypeId");
            writer.WriteValue(value.TypeId);
        }

        var encoding = value.Encoding;
        if (encoding is byte enc && enc != 0)
        {
            writer.WritePropertyName("UaEncoding");
            writer.WriteValue(enc);
            writer.WritePropertyName("UaBody");
            writer.WriteValue(value.Body?.ToString());
        }
        else if (value.Body is JObject bodyObj)
        {
            // JSON inline — flatten body fields alongside UaTypeId.
            foreach (var prop in bodyObj.Properties())
            {
                if (ReservedKeys.Contains(prop.Name)) continue;
                writer.WritePropertyName(prop.Name);
                prop.Value.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }
}
