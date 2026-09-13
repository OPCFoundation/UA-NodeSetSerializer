using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class LocalizedText
{
    [DataMember(Name = "t")]
    [JsonProperty("t")]
    public List<List<string>>? T { get; set; }

    [DataMember(Name = "r")]
    [JsonProperty("r")]
    public List<List<string>>? R { get; set; }
}
