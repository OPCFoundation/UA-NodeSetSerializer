using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

/// <summary>
/// The manifest.json entry of a multi-file archive. It specifies the order in which the archive's
/// files are processed. Any file in the archive that is not referenced by the manifest is an error.
/// The Models and Declarations of the archive live here rather than on the individual files.
/// </summary>
[DataContract]
public class Manifest
{
    /// <summary>
    /// The name of the archive entry holding the manifest. Every other entry must be named by it.
    /// </summary>
    public const string FileName = "manifest.json";

    /// <summary>If TRUE, the set of files follows the ordering rules defined in Annex I.2.</summary>
    [DataMember]
    [JsonProperty(Order = 1)]
    public bool? Ordered { get; set; }

    [DataMember]
    [JsonProperty(Order = 2)]
    public List<ModelDefinition>? Models { get; set; }

    /// <summary>
    /// Nodes needed to resolve recursive relationships. Only the mandatory fields of UANode are
    /// specified. Only present if <see cref="Ordered"/> is TRUE and recursive relationships exist.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 3)]
    public List<UANode>? Declarations { get; set; }

    /// <summary>
    /// The archive file names. The order of the list specifies the order in which the files are
    /// processed.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 4)]
    public List<string>? Files { get; set; }
}
