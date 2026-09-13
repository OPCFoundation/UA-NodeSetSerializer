using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAObject : UANode
{
    [DataMember]
    [JsonProperty(Order = 21)]
    public int? EventNotifier { get; set; }
}
