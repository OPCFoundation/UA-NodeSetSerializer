using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// The <c>META/package_metadata.json</c> entry of a NodeSet package — OPC 10000-100 Table 120,
/// encoded per §8.7.3 with the JSON VerboseEncoding.
///
/// <para>A NodeSet package is a stripped-down profile of the DI <i>Software Package</i>: one
/// signature, several JSONL documents, and none of the rest the generic format allows. Table 120
/// has a good deal more than this — <c>Description</c>, <c>SoftwareRevision</c>,
/// <c>Compatibilities</c>, <c>Assignments</c>, <c>UpdateTargets</c> and the rest describe a device
/// update and mean nothing for a NodeSet. The profile writes the seven fields below and nothing
/// else, so a package produced by another tool does not survive a load and save through this one.</para>
///
/// <para>These fields describe the identity of a <i>package</i>, which a NodeSet does not have. All
/// but one are taken from the primary model: its ModelUri is the ManufacturerUri and its last
/// segment the Name, its ModelVersion the PackageRevision, its PublicationDate the ReleaseDate. The
/// exception is <see cref="Manufacturer"/> — nothing in a NodeSet records who produced it. A caller
/// that knows better supplies its own values.</para>
/// </summary>
[DataContract]
public class PackageMetadata
{
    /// <summary>The name of the archive entry holding this object. Mandatory in every package.</summary>
    public const string FileName = "META/package_metadata.json";

    // The fields are written in the profile's order, which pairs Manufacturer with ManufacturerUri
    // and puts ReleaseDate next to the PackageRevision it dates. Table 120 lists them differently.
    // That is a deliberate difference and not a conformance one: a JSON object is an unordered
    // collection of members (IETF RFC 8259), and JSON Schema has no way to constrain their order, so
    // no reader can depend on either arrangement. Note this is unlike a NodeSet document, where
    // field order *is* normative and NodeSetOrderValidator enforces it.

    /// <summary>Names the package; the package file name itself can be changed.</summary>
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? Name { get; set; }

    /// <summary>
    /// The organization or tool that generated the UANodeSet, for display. Where that is not known
    /// it is "OPC Foundation" — a NodeSet does not record who produced it, and the field is not
    /// optional.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 2)]
    public string? Manufacturer { get; set; }

    /// <summary>Identifies the maker of the package: the ModelUri of the primary model.</summary>
    [DataMember]
    [JsonProperty(Order = 3)]
    public string? ManufacturerUri { get; set; }

    /// <summary>The ModelVersion of the primary model.</summary>
    [DataMember]
    [JsonProperty(Order = 4)]
    public string? PackageRevision { get; set; }

    /// <summary>
    /// When the <see cref="PackageRevision"/> was published: the PublicationDate of the primary
    /// model.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 5)]
    public DateTime? ReleaseDate { get; set; }

    /// <summary>A NodeSet package is <see cref="Model.PackageType.Configuration"/>.</summary>
    [DataMember]
    [JsonProperty(Order = 6)]
    public PackageType? PackageType { get; set; }

    /// <summary>
    /// The documents in the package, in the order they are processed. Concatenating the Node lines
    /// of each in this order reproduces the single-document form of the same NodeSet, which is what
    /// lets a Node in one document depend on a Node in an earlier one without declaring it.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 8)]
    public List<FileDescriptor>? Files { get; set; }
}
