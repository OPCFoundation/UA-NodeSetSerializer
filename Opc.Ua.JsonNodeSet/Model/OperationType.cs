using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

/// <summary>
/// Part 6 Annex I.9. The type of change applied when the Nodes in a ChangeSet are processed.
/// </summary>
[DataContract]
public enum OperationType
{
    /// <summary>Each Node is new. An error occurs if a Node with the same NodeId already exists.</summary>
    [EnumMember(Value = "0")]
    Insert = 0,

    /// <summary>
    /// Each Node updates an existing Node, identified by NodeId. An error occurs if it does not exist.
    /// Updateable (RW) fields present in the file replace the current value; fields that are not
    /// updateable (RO) are ignored. A JSON null clears an optional field and is an error on a
    /// mandatory one.
    /// </summary>
    [EnumMember(Value = "1")]
    Update = 1,

    /// <summary>
    /// Each Node deletes an existing Node, identified by NodeId. An error occurs if it does not
    /// exist. Every Node whose ParentId names a deleted Node is deleted with it.
    /// </summary>
    [EnumMember(Value = "2")]
    Delete = 2,
}
