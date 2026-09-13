using System.Runtime.Serialization;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class Variant
{
    public Variant()
    {
    }

    public Variant(int uaType, object? value, List<int>? dimensions = null)
    {
        UaType = uaType;
        Value = value;
        Dimensions = dimensions;
    }

    [DataMember]
    public int? UaType { get; set; }

    [DataMember]
    public object? Value { get; set; }

    [DataMember]
    public List<int>? Dimensions { get; set; }
}
