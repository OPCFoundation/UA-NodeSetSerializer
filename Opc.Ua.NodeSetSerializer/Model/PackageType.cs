using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.NodeSetSerializer.Model;

/// <summary>
/// OPC 10000-100 Table 130. The kind of Software Package. A NodeSet package is
/// <see cref="Configuration"/> — the only listed value that fits a description of a model rather
/// than something executable. The remaining values are here so a package written by another tool
/// reads back as what it says it is.
/// </summary>
[DataContract]
[JsonConverter(typeof(VerboseEnumConverter))]
public enum PackageType
{
    [EnumMember(Value = "Firmware")]
    Firmware = 0,

    [EnumMember(Value = "Application")]
    Application = 1,

    [EnumMember(Value = "Configuration")]
    Configuration = 2,

    [EnumMember(Value = "Solution")]
    Solution = 3
}
