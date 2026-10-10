using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// Part 6 Annex I.10. A Reference standing on its own rather than inside the Node it belongs to,
/// which is how the JSONL layout carries one: a line of its own, written after the Nodes it names.
/// A <c>.json</c> document has no use for this type — there, a Reference is nested under its source
/// and <see cref="Reference"/> is the shape it takes.
///
/// <para>There is no IsForward field. A UAReference is always the forward direction, so
/// <see cref="SourceId"/> is the source of the ReferenceType and <see cref="TargetId"/> its target;
/// an inverse Reference is written as the forward Reference between the same two Nodes the other way
/// round. A decoder always creates both directions, so one is all that needs writing — and with the
/// direction fixed there is nothing for a Unidirectional flag to say either.</para>
///
/// <para>The structural References that <see cref="UANode"/> states as fields are not written this
/// way and keep their own rules: HasTypeDefinition and HasModellingRule stay one-way, while the
/// supertype and the owner's hierarchical Reference exist in both directions.</para>
/// </summary>
/// <remarks>Order 2 is left free for the Operation field a ChangeSet puts there, which this
/// implementation does not yet write (it still carries one operation per file).</remarks>
[DataContract]
public class UAReference
{
    /// <summary>The source Node of the forward Reference.</summary>
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? SourceId { get; set; }

    /// <summary>The ReferenceType. Named TypeId here, ReferenceTypeId inside a Node.</summary>
    [DataMember]
    [JsonProperty(Order = 3)]
    public string? TypeId { get; set; }

    /// <summary>The target Node of the forward Reference.</summary>
    [DataMember]
    [JsonProperty(Order = 4)]
    public string? TargetId { get; set; }
}
