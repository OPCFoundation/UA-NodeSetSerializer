using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
// The definition has no Name of its own: the BrowseName of the containing DataType is the
// normative source. NodeSetSerializer restores the XML Definition/@Name attribute from it.
public class DataTypeDefinition
{
    [DataMember]
    public string? SymbolicName { get; set; }

    [DataMember]
    public bool? IsUnion { get; set; }

    [DataMember]
    public bool? IsOptionSet { get; set; }

    [DataMember]
    public List<DataTypeField>? Fields { get; set; }
}
