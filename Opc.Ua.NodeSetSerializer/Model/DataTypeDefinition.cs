using System.Runtime.Serialization;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
// The definition has neither a Name nor a SymbolicName of its own: the containing DataType is the
// normative source for both. NodeSetSerializer restores the XML Definition/@Name attribute from the
// DataType's BrowseName and Definition/@SymbolicName from its SymbolicName. Storing either here as
// well would let the copies disagree, and the XML attributes are mandatory and optional
// respectively — a definition-level field that went missing would silently produce invalid XML.
public class DataTypeDefinition
{
    [DataMember]
    public bool? IsUnion { get; set; }

    [DataMember]
    public bool? IsOptionSet { get; set; }

    [DataMember]
    public List<DataTypeField>? Fields { get; set; }
}
