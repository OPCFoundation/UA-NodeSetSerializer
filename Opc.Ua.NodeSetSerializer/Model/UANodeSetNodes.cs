using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// Nodes organized into eight properties, one per NodeClass. Absent properties indicate that no
/// Nodes of that class are present. Type Nodes precede instance Nodes.
/// </summary>
[DataContract]
public class UANodeSetNodes
{
    [DataMember]
    [JsonProperty(Order = 1)]
    public List<UAReferenceType>? ReferenceTypes { get; set; }

    [DataMember]
    [JsonProperty(Order = 2)]
    public List<UADataType>? DataTypes { get; set; }

    [DataMember]
    [JsonProperty(Order = 3)]
    public List<UAVariableType>? VariableTypes { get; set; }

    [DataMember]
    [JsonProperty(Order = 4)]
    public List<UAObjectType>? ObjectTypes { get; set; }

    [DataMember]
    [JsonProperty(Order = 5)]
    public List<UAVariable>? Variables { get; set; }

    [DataMember]
    [JsonProperty(Order = 6)]
    public List<UAMethod>? Methods { get; set; }

    [DataMember]
    [JsonProperty(Order = 7)]
    public List<UAObject>? Objects { get; set; }

    [DataMember]
    [JsonProperty(Order = 8)]
    public List<UAView>? Views { get; set; }
}
