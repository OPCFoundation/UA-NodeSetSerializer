using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum DataTypePurpose
{
    [EnumMember(Value = "Normal")]
    Normal,
    [EnumMember(Value = "ServicesOnly")]
    ServicesOnly,
    [EnumMember(Value = "CodeGenerator")]
    CodeGenerator
}
