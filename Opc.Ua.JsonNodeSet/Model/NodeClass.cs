using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public enum NodeClass
{
    [EnumMember(Value = "0")]
    Unspecified = 0,
    [EnumMember(Value = "1")]
    UAObject = 1,
    [EnumMember(Value = "2")]
    UAVariable = 2,
    [EnumMember(Value = "4")]
    UAMethod = 4,
    [EnumMember(Value = "8")]
    UAObjectType = 8,
    [EnumMember(Value = "16")]
    UAVariableType = 16,
    [EnumMember(Value = "32")]
    UAReferenceType = 32,
    [EnumMember(Value = "64")]
    UADataType = 64,
    [EnumMember(Value = "128")]
    UAView = 128
}
