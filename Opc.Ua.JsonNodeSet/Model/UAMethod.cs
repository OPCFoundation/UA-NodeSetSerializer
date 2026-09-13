using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UAMethod : UANode
{
    /// <summary>
    /// The NodeId of the Method with the same BrowseName declared by the TypeDefinition of the
    /// Object that contains this Method.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 21)]
    public string? MethodDeclarationId { get; set; }

    [DataMember]
    [JsonProperty(Order = 22)]
    public bool? Executable { get; set; }
}
