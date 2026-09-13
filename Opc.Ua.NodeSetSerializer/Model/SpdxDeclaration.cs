using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Opc.Ua.JsonNodeSet.Model;

/// <summary>
/// Licence/copyright metadata for a JSON NodeSet. The XML NodeSet carries the same information in
/// SPDX header comments, but JSON has no comment syntax, so it is stored as a structured object
/// emitted as the first field of the document. In a multi-file archive it appears only on the first
/// file, alongside <see cref="UANodeSet.Models"/>.
/// </summary>
[DataContract]
public class SpdxDeclaration
{
    /// <summary>SPDX-FileCopyrightText prose, e.g. "Copyright (C) 2026 OPC Federation AISBL".</summary>
    [DataMember]
    [JsonProperty(Order = 1)]
    public string? CopyrightText { get; set; }

    /// <summary>The SPDX licence identifier, e.g. "MIT" or "LicenseRef-OPC-Foundation".</summary>
    [DataMember]
    [JsonProperty(Order = 2)]
    public string? LicenceId { get; set; }

    /// <summary>
    /// A URL for the licence. Optional if <see cref="LicenceId"/> is a standard SPDX identifier;
    /// required if it is a LicenseRef- identifier.
    /// </summary>
    [DataMember]
    [JsonProperty(Order = 3)]
    public string? LicenceRef { get; set; }
}
