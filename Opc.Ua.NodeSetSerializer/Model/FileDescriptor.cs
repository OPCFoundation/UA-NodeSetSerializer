using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// OPC 10000-100 Table 124. One file within the package.
///
/// <para>In a NodeSet package every descriptor names a JSONL document in the CONTENT folder, and the
/// order of <see cref="PackageMetadata.Files"/> is the order those documents are processed in. DI
/// does not give the list an order, so that meaning is ours: a generic Update Client can read the
/// package but is not expected to reassemble a NodeSet from it.</para>
/// </summary>
[DataContract]
public class FileDescriptor
{
    /// <summary>How an Update Client should treat the file.</summary>
    [DataMember]
    [JsonProperty(Order = 1)]
    public FileType? FileType { get; set; }

    /// <summary>Path of the file within the package, relative to the root, e.g. <c>CONTENT/uanodeset_001.jsonl</c>.</summary>
    [DataMember]
    [JsonProperty(Order = 2)]
    public string? FileName { get; set; }

    /// <summary>Media type. Absent means <c>application/octet-stream</c>.</summary>
    [DataMember]
    [JsonProperty(Order = 3)]
    public string? MimeType { get; set; }

    /// <summary>Language, for a document that exists in several.</summary>
    [DataMember]
    [JsonProperty(Order = 4)]
    public string? Language { get; set; }
}
