using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

/// <summary>
/// A model defined in the UANodeSet, including its version and dependencies. IsPartial takes
/// Order 4, the slot <see cref="ModelReference"/> leaves free, so the emitted order matches the
/// normative one.
/// </summary>
[DataContract]
public class ModelDefinition : ModelReference
{
    /// <summary>If TRUE the UANodeSet only contains some of the Nodes in the Model.</summary>
    [DataMember]
    [JsonProperty(Order = 4)]
    public bool? IsPartial { get; set; }

    [DataMember]
    [JsonProperty(Order = 7)]
    public long? DefaultAccessRestrictions { get; set; }

    [DataMember]
    [JsonProperty(Order = 8)]
    public List<RolePermission>? DefaultRolePermissions { get; set; }

    [DataMember]
    [JsonProperty(Order = 9)]
    public List<ModelReference>? RequiredModels { get; set; }
}
