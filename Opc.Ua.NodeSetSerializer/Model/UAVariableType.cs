using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAVariableType : UANode
{
    [DataMember]
    [JsonProperty(Order = 22)]
    public string? DataType { get; set; }

    [DataMember]
    [JsonProperty(Order = 23)]
    public int? ValueRank { get; set; }

    [DataMember]
    [JsonProperty(Order = 24)]
    public string? ArrayDimensions { get; set; }

    [DataMember]
    [JsonProperty(Order = 25)]
    public Variant? Value { get; set; }
}
