using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// OPC 10000-100 Table 132. How an Update Client should treat a file in the package. Every NodeSet
/// in a package is a <see cref="DeploymentItem"/>: it is the payload rather than something shown to
/// the user before installing it.
/// </summary>
[DataContract]
[JsonConverter(typeof(VerboseEnumConverter))]
public enum FileType
{
    [EnumMember(Value = "DeploymentItem")]
    DeploymentItem = 0,

    [EnumMember(Value = "ReleaseNotes")]
    ReleaseNotes = 1,

    [EnumMember(Value = "LicenseInfo")]
    LicenseInfo = 2,

    [EnumMember(Value = "PreInstallNote")]
    PreInstallNote = 3
}
