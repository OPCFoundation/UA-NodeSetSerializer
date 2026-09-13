using NodeSetTool;

namespace Opc.Ua.JsonNodeSet;

/// <summary>
/// Loads the OPC UA Core (Services) NodeSet bundled with this assembly as an embedded
/// resource. Used as the fallback type registry when callers convert a NodeSet without
/// having explicitly loaded Core themselves — so standard DataTypes like
/// <c>Argument</c>, <c>EUInformation</c>, <c>BuildInfo</c> resolve at variant resolve
/// time.
///
/// <para>If the caller has already loaded a Core NodeSet (detected by the presence of
/// <c>BaseDataType</c> at NodeId <c>i=24</c>), this loader is a no-op so we don't
/// duplicate-register the namespace.</para>
/// </summary>
public static class CoreNodeSetLoader
{
    private const string EmbeddedResourceName = "Opc.Ua.JsonNodeSet.Resources.Opc.Ua.NodeSet2.Services.xml";

    /// <summary>
    /// True when the AddressSpace already has the OPC UA Core namespace loaded.
    /// Detected by the presence of <c>BaseDataType</c> at <c>i=24</c>.
    /// </summary>
    public static bool HasCore(AddressSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);
        return space.Read("i=24") != null;
    }

    /// <summary>
    /// Loads the embedded Core NodeSet into the given AddressSpace, but only if Core
    /// is not already present. Returns true if the embedded NodeSet was loaded.
    /// </summary>
    public static bool EnsureCoreLoaded(AddressSpace space)
    {
        ArgumentNullException.ThrowIfNull(space);
        if (HasCore(space)) return false;

        var asm = typeof(CoreNodeSetLoader).Assembly;
        using var stream = asm.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Core NodeSet resource '{EmbeddedResourceName}' not found in {asm.FullName}.");

        var serializer = new NodeSetSerializer();
        serializer.LoadXml(stream);
        serializer.LoadInto(space);
        return true;
    }
}
