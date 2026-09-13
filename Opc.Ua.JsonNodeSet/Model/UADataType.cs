using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

[DataContract]
public class UADataType : UANode
{
    [DataMember]
    [JsonProperty(Order = 21)]
    public DataTypePurpose? Purpose { get; set; }

    [DataMember]
    [JsonProperty(Order = 22)]
    public DataTypeDefinition? Definition { get; set; }

    /// <summary>
    /// Pre-calculated DataType form based on inheritance chain.
    /// Not serialized — computed after loading into AddressSpace.
    /// Values: "Structure", "Union", "Enumeration", "OptionSet", or null.
    /// </summary>
    public string? DataTypeForm { get; set; }
}
