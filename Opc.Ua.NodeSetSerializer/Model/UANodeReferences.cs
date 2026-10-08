namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// The References of a Node as a flat list, including the two that Annex I.9 states as fields
/// instead: the hierarchical Reference from the owner, on <see cref="UANode.ParentId"/> and
/// <see cref="UANode.ReferenceTypeId"/>, and HasSubtype, on <see cref="UANode.SuperTypeId"/>.
///
/// <para>For code that works on the reference graph rather than on the encoding — a validator, a
/// projection into another model — and would otherwise have to know which Reference moved to which
/// field. TypeId and ModellingRuleId are not included: they predate this and callers that want
/// them read them directly.</para>
/// </summary>
public static class UANodeReferences
{
    private const string HasSubtype = "i=45";

    public static IEnumerable<Reference> All(UANode node)
    {
        if (node.References != null)
        {
            foreach (var reference in node.References) yield return reference;
        }

        // The subtype carries the inverse direction, which is the one an XML NodeSet writes.
        if (node.SuperTypeId != null)
        {
            yield return new Reference()
            {
                ReferenceTypeId = HasSubtype,
                IsForward = false,
                TargetId = node.SuperTypeId,
            };
        }

        // The child carries the inverse direction; the forward one belongs to the parent.
        if (node.ParentId != null && node.ReferenceTypeId != null)
        {
            yield return new Reference()
            {
                ReferenceTypeId = node.ReferenceTypeId,
                IsForward = false,
                TargetId = node.ParentId,
            };
        }
    }
}
