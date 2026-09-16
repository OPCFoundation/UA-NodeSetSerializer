using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

[DataContract]
public class UANodeSet
{
    // Licence/copyright header. Emitted as the first field of the document. In a multi-file archive
    // it is written only on the first file (see NodeSetSerializer.Package), matching how Models is
    // handled.
    [DataMember]
    [JsonProperty(Order = 1)]
    public SpdxDeclaration? SPDX { get; set; }

    // The models formally defined by this UANodeSet, with their versions and dependencies. In a
    // package they appear on the first document only: they describe the package as a whole, and a
    // table that must be identical everywhere is better written once than copied.
    [DataMember]
    [JsonProperty(Order = 2)]
    public List<ModelDefinition>? Models { get; set; }

    // Annex I.4. If TRUE the Nodes are changes to be applied to an existing model rather than a
    // model definition in their own right. ChangeSet = TRUE with Operation = Insert is equivalent
    // to ChangeSet = FALSE.
    [DataMember]
    [JsonProperty(Order = 3)]
    public bool? ChangeSet { get; set; }

    // Annex I.4. The operation to apply when the file is processed. Only meaningful when
    // ChangeSet is TRUE; absent means Insert.
    [DataMember]
    [JsonProperty(Order = 4)]
    public OperationType? Operation { get; set; }

    [DataMember]
    [JsonProperty(Order = 5)]
    public UANodeSetNodes? Nodes { get; set; }
}
