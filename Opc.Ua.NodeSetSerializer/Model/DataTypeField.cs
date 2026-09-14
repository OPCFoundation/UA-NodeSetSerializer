using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class DataTypeField
{
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? Name { get; set; }

    [DataMember]
    [JsonProperty(Order = 2)]
    public string? SymbolicName { get; set; }

    [DataMember]
    [JsonProperty(Order = 3)]
    public LocalizedText? DisplayName { get; set; }

    [DataMember]
    [JsonProperty(Order = 4)]
    public LocalizedText? Description { get; set; }

    [DataMember]
    [JsonProperty(Order = 5)]
    public int? Value { get; set; }

    [DataMember]
    [JsonProperty(Order = 6)]
    public string? DataType { get; set; }

    [DataMember]
    [JsonProperty(Order = 7)]
    public int? ValueRank { get; set; }

    [DataMember]
    [JsonProperty(Order = 8)]
    public string? ArrayDimensions { get; set; }

    [DataMember]
    [JsonProperty(Order = 9)]
    public int? MaxStringLength { get; set; }

    [DataMember]
    [JsonProperty(Order = 10)]
    public bool? IsOptional { get; set; }

    [DataMember]
    [JsonProperty(Order = 11)]
    public bool? AllowSubTypes { get; set; }
}
