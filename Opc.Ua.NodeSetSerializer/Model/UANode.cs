using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// Base type for all OPC UA Nodes. NodeId, NodeClass and BrowseName are mandatory and are emitted
/// first, so a decoder can register a Node before reading the fields that may refer back to it.
/// Subtypes continue the Order sequence from 24.
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

    /// <summary>
    /// Marks a stub that registers a Node the document defines later, so a sequential decoder can
    /// resolve a circular dependency without looking ahead. A declaration carries NodeId, NodeClass
    /// and BrowseName and nothing else, and the Node it names must appear in full further on. Used
    /// by the JSONL layout, where a declaration is a line of its own.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 4)]
    public bool? IsDeclaration { get; set; }

    [DataMember]
    [JsonProperty(Order = 5)]
    public string? SymbolicName { get; set; }

    /// <summary>
    /// The owner of the Node. Ownership is separate from any References between a parent and child:
    /// it indicates that a Node is deleted when its parent is deleted. Ignored when the Node is in a
    /// ChildList, where the owner is always the containing Node.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 6)]
    public string? ParentId { get; set; }

    /// <summary>
    /// The hierarchical ReferenceType from the parent to this Node. Set whenever
    /// <see cref="ParentId"/> is, and neither direction of the Reference it names appears in the
    /// <see cref="References"/> of the parent or of the child — this field is the Reference.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 7)]
    public string? ReferenceTypeId { get; set; }

    [DataMember]
    [JsonProperty(Order = 8)]
    public string? TypeId { get; set; }

    /// <summary>
    /// The supertype of a ReferenceType, DataType, ObjectType or VariableType. Replaces the
    /// HasSubtype Reference, which appears in the <see cref="References"/> of neither the subtype
    /// nor the supertype.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 9)]
    public string? SuperTypeId { get; set; }

    [DataMember]
    [JsonProperty(Order = 10)]
    public string? ModellingRuleId { get; set; }

    [DataMember]
    [JsonProperty(Order = 11)]
    public LocalizedText? DisplayName { get; set; }

    [DataMember]
    [JsonProperty(Order = 12)]
    public ReleaseStatus? ReleaseStatus { get; set; }

    [DataMember]
    [JsonProperty(Order = 13)]
    public string? Documentation { get; set; }

    [DataMember]
    [JsonProperty(Order = 14)]
    public LocalizedText? Description { get; set; }

    [DataMember]
    [JsonProperty(Order = 15)]
    public bool? IsAbstract { get; set; }

    [DataMember]
    [JsonProperty(Order = 16)]
    public long? WriteMask { get; set; }

    [DataMember]
    [JsonProperty(Order = 17)]
    public List<RolePermission>? RolePermissions { get; set; }

    [DataMember]
    [JsonProperty(Order = 18)]
    public long? AccessRestrictions { get; set; }

    [DataMember]
    [JsonProperty(Order = 19)]
    public bool? HasNoPermissions { get; set; }

    [DataMember]
    [JsonProperty(Order = 20)]
    public bool? DesignToolOnly { get; set; }

    [DataMember]
    [JsonProperty(Order = 21)]
    public ChildList? Children { get; set; }

    [DataMember]
    [JsonProperty(Order = 22)]
    public List<Reference>? References { get; set; }

    [DataMember]
    [JsonProperty(Order = 23)]
    public List<string>? ConformanceUnits { get; set; }
}
