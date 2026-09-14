using System.Runtime.Serialization;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class ChildList
{
    [DataMember]
    public List<UAObject>? Objects { get; set; }

    [DataMember]
    public List<UAVariable>? Variables { get; set; }

    [DataMember]
    public List<UAMethod>? Methods { get; set; }
}
