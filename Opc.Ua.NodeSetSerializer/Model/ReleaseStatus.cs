using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum ReleaseStatus
{
    [EnumMember(Value = "Released")]
    Released,
    [EnumMember(Value = "Draft")]
    Draft,
    [EnumMember(Value = "Deprecated")]
    Deprecated
}
