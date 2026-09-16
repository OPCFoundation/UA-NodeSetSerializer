using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAReferenceType : UANode
{
    [DataMember]
    [JsonProperty(Order = 22)]
    public bool? Symmetric { get; set; }

    [DataMember]
    [JsonProperty(Order = 23)]
    public LocalizedText? InverseName { get; set; }
}
