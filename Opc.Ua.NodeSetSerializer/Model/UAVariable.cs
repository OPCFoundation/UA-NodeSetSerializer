using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAVariable : UANode
{
    [DataMember]
    [JsonProperty(Order = 21)]
    public string? DataType { get; set; }

    [DataMember]
    [JsonProperty(Order = 22)]
    public int? ValueRank { get; set; }

    [DataMember]
    [JsonProperty(Order = 23)]
    public string? ArrayDimensions { get; set; }

    [DataMember]
    [JsonProperty(Order = 24)]
    public Variant? Value { get; set; }

    [DataMember]
    [JsonProperty(Order = 25)]
    public long? AccessLevel { get; set; }

    [DataMember]
    [JsonProperty(Order = 26)]
    public decimal? MinimumSamplingInterval { get; set; }

    [DataMember]
    [JsonProperty(Order = 27)]
    public bool? Historizing { get; set; }
}
