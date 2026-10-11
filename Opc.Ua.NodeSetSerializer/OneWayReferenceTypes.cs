namespace Opc.Ua.NodeSetSerializer;

/// <summary>
/// The ReferenceTypes whose reverse direction does not exist, and so must not be created when a
/// NodeSet is decoded.
///
/// <para>A NodeSet states a Reference once and a consumer builds both directions — the rule holds
/// for the XML encoding as much as for JSON and JSONL. A few ReferenceTypes are the exception:
/// GeneratesEvent relates a Node to an EventType and the EventType gains nothing in return, so
/// mirroring it would invent a Reference the information model does not have.</para>
///
/// <para>Membership is by subtype, so AlwaysGeneratesEvent is covered by GeneratesEvent and is not
/// listed. That resolution needs a supertype chain, which <see cref="IsOneWay"/> takes as a
/// delegate: a caller holding an address space passes its own lookup, and a caller holding only a
/// NodeSet passes one over SuperTypeId. Where neither can reach a ReferenceType — a subtype defined
/// by a model that is not loaded — <see cref="s_wellKnownSuperTypes"/> covers the core ones, and a
/// caller adding an exception of its own should name any subtypes it needs along with it.</para>
/// </summary>
public sealed class OneWayReferenceTypes
{
    /// <summary>GeneratesEvent. Its subtypes resolve through the chain rather than being listed.</summary>
    private const string GeneratesEvent = "i=41";

    /// <summary>AlwaysGeneratesEvent, whose supertype is GeneratesEvent.</summary>
    private const string AlwaysGeneratesEvent = "i=3065";

    // Enough of the core ReferenceType hierarchy to resolve the built-in exception when the Core
    // NodeSet itself is not among the loaded models, which is the usual case for a companion model.
    private static readonly Dictionary<string, string> s_wellKnownSuperTypes = new(StringComparer.Ordinal)
    {
        [AlwaysGeneratesEvent] = GeneratesEvent,
    };

    /// <summary>The exceptions Part 6 and Part 3 define, with nothing added.</summary>
    public static OneWayReferenceTypes Default { get; } = new(new[] { GeneratesEvent });

    private readonly HashSet<string> m_nodeIds;

    public OneWayReferenceTypes(IEnumerable<string> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodeIds);

        m_nodeIds = new HashSet<string>(nodeIds, StringComparer.Ordinal);
    }

    /// <summary>The NodeIds named directly, without their subtypes.</summary>
    public IReadOnlyCollection<string> NodeIds => m_nodeIds;

    /// <summary>This set plus <paramref name="nodeIds"/>, for a caller extending the defaults.</summary>
    public OneWayReferenceTypes With(IEnumerable<string>? nodeIds) =>
        nodeIds == null ? this : new OneWayReferenceTypes(m_nodeIds.Concat(nodeIds));

    /// <summary>
    /// Whether a Reference of this type has no reverse direction.
    /// </summary>
    /// <param name="referenceTypeId">The ReferenceType to test. A null is not one-way.</param>
    /// <param name="superTypeOf">The supertype of a ReferenceType, or null at the top of the chain.
    /// Consulted before the well-known table, so a loaded model always wins over the fallback.</param>
    public bool IsOneWay(string? referenceTypeId, Func<string, string?>? superTypeOf = null)
    {
        if (referenceTypeId == null || m_nodeIds.Count == 0) return false;

        var current = referenceTypeId;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (current != null && seen.Add(current))
        {
            if (m_nodeIds.Contains(current)) return true;

            current = superTypeOf?.Invoke(current)
                ?? (s_wellKnownSuperTypes.TryGetValue(current, out var known) ? known : null);
        }

        return false;
    }
}
