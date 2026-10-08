using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAView : UANode
{
    [DataMember]
    [JsonProperty(Order = 24)]
    public int? EventNotifier { get; set; }

    [DataMember]
    [JsonProperty(Order = 25)]
    public bool? ContainsNoLoops { get; set; }
}
