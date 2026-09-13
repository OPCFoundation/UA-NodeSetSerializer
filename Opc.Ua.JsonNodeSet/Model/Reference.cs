using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class Reference
{
    [DataMember]
    public string? ReferenceTypeId { get; set; }

    [DataMember]
    public bool? IsForward { get; set; }

    [DataMember]
    public string? TargetId { get; set; }
}
