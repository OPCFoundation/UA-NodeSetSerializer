using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAReferenceType : UANode
{
    [DataMember]
    [JsonProperty(Order = 24)]
    public bool? Symmetric { get; set; }

    [DataMember]
    [JsonProperty(Order = 25)]
    public LocalizedText? InverseName { get; set; }
}
