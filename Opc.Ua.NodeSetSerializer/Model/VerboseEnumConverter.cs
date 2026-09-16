using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// The OPC 10000-6 JSON VerboseEncoding of an Enumeration: the symbolic name and the numeric value
/// joined by an underscore, <c>Configuration_2</c>.
///
/// <para>Used by the types that make up <see cref="PackageMetadata"/>, which OPC 10000-100 §8.7.3
/// specifies as VerboseEncoding so that a DI-aware decoder can read the package metadata. It is not
/// the encoding the rest of this model uses — a NodeSet writes <c>NodeClass</c> as a bare number —
/// which is why it is opted into per type rather than registered globally.</para>
///
/// <para>Reading is deliberately lenient: the name alone, the number alone, and the joined form are
/// all accepted, since the numeric half is redundant and a writer that omits it still names the
/// value unambiguously.</para>
/// </summary>
internal sealed class VerboseEnumConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => Underlying(objectType).IsEnum;

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        var type = value.GetType();

        writer.WriteValue($"{Name(type, value)}_{Convert.ToInt64(value)}");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var type = Underlying(objectType);

        switch (reader.TokenType)
        {
            case JsonToken.Null:
                return null;

            case JsonToken.Integer:
                return Enum.ToObject(type, Convert.ToInt64(reader.Value));

            case JsonToken.String:
                {
                    var text = (string)reader.Value!;

                    // "Configuration_2" -> "Configuration". A name cannot itself contain '_' in any
                    // of the enumerations this converter is used for, so the split is unambiguous.
                    var separator = text.LastIndexOf('_');
                    var name = separator > 0 ? text.Substring(0, separator) : text;

                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (Name(type, field.GetValue(null)!) == name)
                        {
                            return field.GetValue(null);
                        }
                    }

                    // A bare number arriving as a string, which is still unambiguous.
                    if (separator < 0 && Int64.TryParse(text, out var numeric))
                    {
                        return Enum.ToObject(type, numeric);
                    }

                    throw new JsonSerializationException($"'{text}' is not a value of {type.Name}.");
                }

            default:
                throw new JsonSerializationException(
                    $"{reader.TokenType} cannot be read as {type.Name}.");
        }
    }

    /// <summary>The EnumMember name of a value, falling back to the identifier.</summary>
    private static string Name(Type type, object value)
    {
        var field = type.GetField(value.ToString()!, BindingFlags.Public | BindingFlags.Static);

        return field?.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? value.ToString()!;
    }

    private static Type Underlying(Type type) => Nullable.GetUnderlyingType(type) ?? type;
}
