using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

/// <summary>
/// A dependency of the Model defined in the UANodeSet. Order 4 is deliberately left free: the
/// normative order interleaves <see cref="ModelDefinition.IsPartial"/> between PublicationDate and
/// Version, and an inherited property cannot re-declare its Order in the subtype.
/// </summary>
[DataContract]
public class ModelReference
{
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? ModelUri { get; set; }

    /// <summary>The model version as a SemVer string.</summary>
    [DataMember]
    [JsonProperty(Order = 2)]
    public string? ModelVersion { get; set; }

    [DataMember]
    [JsonProperty(Order = 3)]
    public DateTime? PublicationDate { get; set; }

    /// <summary>A human-readable version string.</summary>
    [DataMember(Name = "Version")]
    [JsonProperty("Version", Order = 5)]
    public string? VarVersion { get; set; }

    /// <summary>
    /// The URI for the XML schema namespace used to serialize values of the DataTypes defined by
    /// the model. Required if DataTypes are defined in the UANodeSet.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 6)]
    public string? XmlSchemaUri { get; set; }
}
