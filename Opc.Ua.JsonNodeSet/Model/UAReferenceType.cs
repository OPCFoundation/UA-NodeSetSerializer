using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAReferenceType : UANode
{
    [DataMember]
    [JsonProperty(Order = 21)]
    public bool? Symmetric { get; set; }

    [DataMember]
    [JsonProperty(Order = 22)]
    public LocalizedText? InverseName { get; set; }
}
