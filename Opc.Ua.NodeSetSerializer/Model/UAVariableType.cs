using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAVariableType : UANode
{
    [DataMember]
    [JsonProperty(Order = 24)]
    public string? DataType { get; set; }

    [DataMember]
    [JsonProperty(Order = 25)]
    public int? ValueRank { get; set; }

    [DataMember]
    [JsonProperty(Order = 26)]
    public string? ArrayDimensions { get; set; }

    [DataMember]
    [JsonProperty(Order = 27)]
    public Variant? Value { get; set; }
}
