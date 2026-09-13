using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

/// <summary>
/// Base type for all OPC UA Nodes. NodeId, NodeClass and BrowseName are mandatory and are emitted
/// first, so a decoder can register a Node before reading the fields that may refer back to it.
/// Subtypes continue the Order sequence from 21.
/// </summary>
[DataContract]
public class UANode
{
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? NodeId { get; set; }

    [DataMember]
    [JsonProperty(Order = 2)]
    public NodeClass? NodeClass { get; set; }

    [DataMember]
    [JsonProperty(Order = 3)]
    public string? BrowseName { get; set; }

    [DataMember]
    [JsonProperty(Order = 4)]
    public string? SymbolicName { get; set; }

    /// <summary>
    /// The owner of the Node. Ownership is separate from any References between a parent and child:
    /// it indicates that a Node is deleted when its parent is deleted. Ignored when the Node is in a
    /// ChildList, where the owner is always the containing Node.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 5)]
    public string? ParentId { get; set; }

    [DataMember]
    [JsonProperty(Order = 6)]
    public string? TypeId { get; set; }

    [DataMember]
    [JsonProperty(Order = 7)]
    public string? ModellingRuleId { get; set; }

    [DataMember]
    [JsonProperty(Order = 8)]
    public LocalizedText? DisplayName { get; set; }

    [DataMember]
    [JsonProperty(Order = 9)]
    public ReleaseStatus? ReleaseStatus { get; set; }

    [DataMember]
    [JsonProperty(Order = 10)]
    public string? Documentation { get; set; }

    [DataMember]
    [JsonProperty(Order = 11)]
    public LocalizedText? Description { get; set; }

    [DataMember]
    [JsonProperty(Order = 12)]
    public bool? IsAbstract { get; set; }

    [DataMember]
    [JsonProperty(Order = 13)]
    public long? WriteMask { get; set; }

    [DataMember]
    [JsonProperty(Order = 14)]
    public List<RolePermission>? RolePermissions { get; set; }

    [DataMember]
    [JsonProperty(Order = 15)]
    public long? AccessRestrictions { get; set; }

    [DataMember]
    [JsonProperty(Order = 16)]
    public bool? HasNoPermissions { get; set; }

    [DataMember]
    [JsonProperty(Order = 17)]
    public bool? DesignToolOnly { get; set; }

    [DataMember]
    [JsonProperty(Order = 18)]
    public ChildList? Children { get; set; }

    [DataMember]
    [JsonProperty(Order = 19)]
    public List<Reference>? References { get; set; }

    [DataMember]
    [JsonProperty(Order = 20)]
    public List<string>? ConformanceUnits { get; set; }
}
