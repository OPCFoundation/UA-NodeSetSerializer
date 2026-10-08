using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// Part 6 Annex I.10. A Reference standing on its own rather than inside the Node it belongs to,
/// which is how the JSONL layout carries one: a line of its own, written after the Node it came
/// from. A <c>.json</c> document has no use for this type — there, a Reference is nested under its
/// source and <see cref="Reference"/> is the shape it takes.
///
/// <para>Only one direction is written. A decoder creates both unless
/// <see cref="Unidirectional"/> says otherwise, so a NodeSet states each Reference once.</para>
/// </summary>
/// <remarks>Order 2 is left free for the Operation field a ChangeSet puts there, which this
/// implementation does not yet write (it still carries one operation per file).</remarks>
[DataContract]
public class UAReference
{
    /// <summary>The Node the Reference belongs to. Always a Node written on an earlier line.</summary>
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? SourceId { get; set; }

    /// <summary>The ReferenceType. Named TypeId here, ReferenceTypeId inside a Node.</summary>
    [DataMember]
    [JsonProperty(Order = 3)]
    public string? TypeId { get; set; }

    [DataMember]
    [JsonProperty(Order = 4)]
    public bool? IsForward { get; set; }

    [DataMember]
    [JsonProperty(Order = 5)]
    public string? TargetId { get; set; }

    /// <summary>
    /// TRUE when only the stated direction exists. A NodeSet describes an AddressSpace, where a
    /// Reference always has both directions, so the writer leaves this absent; it is here for
    /// documents that model something narrower, and for a ChangeSet deleting one direction.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 6)]
    public bool? Unidirectional { get; set; }
}
