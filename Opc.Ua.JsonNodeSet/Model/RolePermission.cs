using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class RolePermission
{
    public RolePermission()
    {
    }

    public RolePermission(string? roleId, long permissions)
    {
        RoleId = roleId;
        Permissions = permissions;
    }

    [DataMember]
    public string? RoleId { get; set; }

    [DataMember]
    public long? Permissions { get; set; }
}
