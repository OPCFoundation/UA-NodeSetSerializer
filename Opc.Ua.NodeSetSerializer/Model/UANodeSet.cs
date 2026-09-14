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

    // Annex I.2. If TRUE the order of Nodes is guaranteed to follow the ordering requirements.
    // An absent field is equivalent to FALSE and asserts nothing.
    [DataMember]
    [JsonProperty(Order = 2)]
    public bool? Ordered { get; set; }

    // If TRUE a manifest file exists and this UANodeSet is part of a multi-file archive. The Models
    // and Declarations fields are specified in the manifest and are not present here.
    [DataMember]
    [JsonProperty(Order = 3)]
    public bool? HasManifest { get; set; }

    [DataMember]
    [JsonProperty(Order = 4)]
    public List<ModelDefinition>? Models { get; set; }

    // Annex I.4. If TRUE the Nodes are changes to be applied to an existing model rather than a
    // model definition in their own right. ChangeSet = TRUE with Operation = Insert is equivalent
    // to ChangeSet = FALSE.
    [DataMember]
    [JsonProperty(Order = 5)]
    public bool? ChangeSet { get; set; }

    // Annex I.4. The operation to apply when the file is processed. Only meaningful when
    // ChangeSet is TRUE; absent means Insert.
    [DataMember]
    [JsonProperty(Order = 6)]
    public OperationType? Operation { get; set; }

    // Nodes needed to resolve recursive relationships. Only the mandatory fields of UANode are
    // specified. Only present if Ordered is TRUE and recursive relationships exist.
    [DataMember]
    [JsonProperty(Order = 7)]
    public List<UANode>? Declarations { get; set; }

    [DataMember]
    [JsonProperty(Order = 8)]
    public UANodeSetNodes? Nodes { get; set; }
}
