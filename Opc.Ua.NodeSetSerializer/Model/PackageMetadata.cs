using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// The <c>META/package_metadata.json</c> entry of a NodeSet package — OPC 10000-100 Table 120,
/// encoded per §8.7.3 with the JSON VerboseEncoding.
///
/// <para>A NodeSet package is a stripped-down profile of the DI <i>Software Package</i>: one
/// signature, several JSONL documents, and none of the rest the generic format allows. The fields
/// modelled here are the ones that profile writes. DI's <c>Compatibilities</c>, <c>Assignments</c>
/// and <c>UpdateTargets</c> describe a device update and mean nothing for a NodeSet, so they are
/// neither written nor preserved — a package produced by another tool does not survive a load and
/// save through this one.</para>
///
/// <para>The mandatory fields describe the identity of a <i>package</i>, which a NodeSet does not
/// have. They are derived from the first Model: the ModelUri is the ManufacturerUri, its last
/// segment the Name, its host the Manufacturer, and the ModelVersion the PackageRevision. A caller
/// that knows better supplies its own.</para>
/// </summary>
[DataContract]
public class PackageMetadata
{
    /// <summary>The name of the archive entry holding this object. Mandatory in every package.</summary>
    public const string FileName = "META/package_metadata.json";

    /// <summary>Names the package; the package file name itself can be changed. Mandatory.</summary>
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? Name { get; set; }

    [DataMember]
    [JsonProperty(Order = 2)]
    public string? Description { get; set; }

    /// <summary>Identifies the author of the package. Mandatory.</summary>
    [DataMember]
    [JsonProperty(Order = 3)]
    public string? ManufacturerUri { get; set; }

    /// <summary>The author, for display. Mandatory.</summary>
    [DataMember]
    [JsonProperty(Order = 4)]
    public string? Manufacturer { get; set; }

    /// <summary>Version of the package. Mandatory.</summary>
    [DataMember]
    [JsonProperty(Order = 5)]
    public string? PackageRevision { get; set; }

    /// <summary>Mandatory. A NodeSet package is <see cref="Model.PackageType.Configuration"/>.</summary>
    [DataMember]
    [JsonProperty(Order = 6)]
    public PackageType? PackageType { get; set; }

    [DataMember]
    [JsonProperty(Order = 7)]
    public DateTime? ReleaseDate { get; set; }

    /// <summary>
    /// The documents in the package, in the order they are processed. Concatenating the Node lines
    /// of each in this order reproduces the single-document form of the same NodeSet, which is what
    /// lets a Node in one document depend on a Node in an earlier one without declaring it.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 8)]
    public List<FileDescriptor>? Files { get; set; }
}
