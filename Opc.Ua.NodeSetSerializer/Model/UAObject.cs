using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UAObject : UANode
{
    [DataMember]
    [JsonProperty(Order = 21)]
    public int? EventNotifier { get; set; }
}
