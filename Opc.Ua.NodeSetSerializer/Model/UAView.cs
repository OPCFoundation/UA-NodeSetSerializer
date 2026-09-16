using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAView : UANode
{
    [DataMember]
    [JsonProperty(Order = 22)]
    public int? EventNotifier { get; set; }

    [DataMember]
    [JsonProperty(Order = 23)]
    public bool? ContainsNoLoops { get; set; }
}
