namespace Opc.Ua
{
    /// <summary>
    /// Parses and formats OPC UA ExpandedNodeId strings.
    /// Handles both namespace-index form (ns=1;i=123) and namespace-URI form (nsu=http://…;i=123).
    /// </summary>
    internal readonly struct ExpandedNodeId
    {
        public ushort NamespaceIndex { get; }
        public string? NamespaceUri { get; }
        public string IdPart { get; }

        private ExpandedNodeId(ushort nsIndex, string? nsUri, string idPart)
        {
            NamespaceIndex = nsIndex;
            NamespaceUri = nsUri;
            IdPart = idPart;
        }

        /// <summary>
        /// Parses a NodeId / ExpandedNodeId string, resolving namespace URIs against the context.
        /// </summary>
        public static ExpandedNodeId Parse(ServiceMessageContext? context, string input)
        {
            if (string.IsNullOrEmpty(input))
                return new ExpandedNodeId(0, null, input);

            var remaining = input.AsSpan();
            ushort nsIndex = 0;
            string? nsUri = null;

            // Handle svr= prefix (consume and discard for our purposes).
            if (remaining.StartsWith("svr=".AsSpan(), StringComparison.Ordinal))
            {
                int semi = remaining.IndexOf(';');
                if (semi >= 0)
                    remaining = remaining.Slice(semi + 1);
            }

            // Handle nsu= prefix (namespace URI form).
            if (remaining.StartsWith("nsu=".AsSpan(), StringComparison.Ordinal))
            {
                remaining = remaining.Slice(4);
                int semi = remaining.IndexOf(';');
                if (semi >= 0)
                {
                    nsUri = remaining.Slice(0, semi).ToString();
                    remaining = remaining.Slice(semi + 1);

                    // Resolve URI to index via context.
                    if (context != null)
                    {
                        int idx = context.NamespaceUris.GetIndex(nsUri);
                        if (idx >= 0)
                            nsIndex = (ushort)idx;
                    }
                }
            }
            // Handle ns= prefix (namespace index form).
            else if (remaining.StartsWith("ns=".AsSpan(), StringComparison.Ordinal))
            {
                remaining = remaining.Slice(3);
                int semi = remaining.IndexOf(';');
                if (semi >= 0)
                {
                    if (ushort.TryParse(remaining.Slice(0, semi), out var idx))
                        nsIndex = idx;
                    remaining = remaining.Slice(semi + 1);

                    // Resolve index to URI via context.
                    if (context != null)
                        nsUri = context.NamespaceUris.GetString(nsIndex);
                }
            }

            return new ExpandedNodeId(nsIndex, nsUri, remaining.ToString());
        }

        /// <summary>
        /// Formats this ExpandedNodeId as a string.
        /// When <paramref name="useUri"/> is true, produces the nsu= form; otherwise the ns= form.
        /// Namespace index 0 omits the prefix entirely.
        /// </summary>
        public string Format(ServiceMessageContext? context, bool useUri)
        {
            if (useUri)
            {
                // Resolve index → URI if we don't already have a URI.
                var uri = NamespaceUri;
                if (uri == null && NamespaceIndex > 0 && context != null)
                    uri = context.NamespaceUris.GetString(NamespaceIndex);

                if (!string.IsNullOrEmpty(uri))
                    return $"nsu={uri};{IdPart}";

                return IdPart;
            }
            else
            {
                // Resolve URI → index if we have a URI but no index.
                var idx = NamespaceIndex;
                if (idx == 0 && NamespaceUri != null && context != null)
                {
                    int resolved = context.NamespaceUris.GetIndex(NamespaceUri);
                    if (resolved >= 0)
                        idx = (ushort)resolved;
                }

                if (idx > 0)
                    return $"ns={idx};{IdPart}";

                return IdPart;
            }
        }
    }

    /// <summary>
    /// Parses and formats OPC UA QualifiedName strings.
    /// XML form: "N:Name". Expanded form: "nsu=URI;Name".
    /// </summary>
    internal readonly struct QualifiedName
    {
        public ushort NamespaceIndex { get; }
        public string? NamespaceUri { get; }
        public string Name { get; }

        private QualifiedName(ushort nsIndex, string? nsUri, string name)
        {
            NamespaceIndex = nsIndex;
            NamespaceUri = nsUri;
            Name = name;
        }

        /// <summary>
        /// Parses a QualifiedName string. Handles both "N:Name" and "nsu=URI;Name" forms.
        /// </summary>
        public static QualifiedName Parse(ServiceMessageContext? context, string input, bool useUri)
        {
            if (string.IsNullOrEmpty(input))
                return new QualifiedName(0, null, input);

            // nsu= form
            if (input.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int semi = input.IndexOf(';', 4);
                if (semi >= 0)
                {
                    var uri = input.Substring(4, semi - 4);
                    var name = input.Substring(semi + 1);
                    ushort idx = 0;

                    if (context != null)
                    {
                        int resolved = context.NamespaceUris.GetIndex(uri);
                        if (resolved >= 0)
                            idx = (ushort)resolved;
                    }

                    return new QualifiedName(idx, uri, name);
                }
            }

            // N:Name form
            int colon = input.IndexOf(':');
            if (colon >= 0 && ushort.TryParse(input.AsSpan(0, colon), out var nsIndex))
            {
                var name = input.Substring(colon + 1);
                string? nsUri = null;

                if (context != null)
                    nsUri = context.NamespaceUris.GetString(nsIndex);

                return new QualifiedName(nsIndex, nsUri, name);
            }

            // Plain name, namespace 0.
            return new QualifiedName(0, null, input);
        }

        /// <summary>
        /// Formats this QualifiedName as a string.
        /// When <paramref name="useUri"/> is true, produces the "nsu=URI;Name" form.
        /// Otherwise produces "N:Name" (0: prefix is omitted).
        /// </summary>
        public string Format(ServiceMessageContext? context, bool useUri)
        {
            if (useUri)
            {
                var uri = NamespaceUri;
                if (uri == null && NamespaceIndex > 0 && context != null)
                    uri = context.NamespaceUris.GetString(NamespaceIndex);

                if (!string.IsNullOrEmpty(uri))
                    return $"nsu={uri};{Name}";

                return Name;
            }
            else
            {
                var idx = NamespaceIndex;
                if (idx == 0 && NamespaceUri != null && context != null)
                {
                    int resolved = context.NamespaceUris.GetIndex(NamespaceUri);
                    if (resolved >= 0)
                        idx = (ushort)resolved;
                }

                if (idx > 0)
                    return $"{idx}:{Name}";

                return Name;
            }
        }

        public static implicit operator QualifiedName(string value)
        {
            return new QualifiedName(0, null, value);
        }
    }

    /// <summary>
    /// Well-known OPC UA constants.
    /// </summary>
    internal static class ValueRanks
    {
        public const int Scalar = -1;
    }

    internal static class EventNotifiers
    {
        public const byte None = 0;
    }

    internal static class BrowseNames
    {
        public const string Boolean = "Boolean";
        public const string SByte = "SByte";
        public const string Byte = "Byte";
        public const string Int16 = "Int16";
        public const string UInt16 = "UInt16";
        public const string Int32 = "Int32";
        public const string UInt32 = "UInt32";
        public const string Int64 = "Int64";
        public const string UInt64 = "UInt64";
        public const string Float = "Float";
        public const string Double = "Double";
        public const string DateTime = "DateTime";
        public const string String = "String";
        public const string ByteString = "ByteString";
        public const string Guid = "Guid";
        public const string XmlElement = "XmlElement";
        public const string NodeId = "NodeId";
        public const string ExpandedNodeId = "ExpandedNodeId";
        public const string QualifiedName = "QualifiedName";
        public const string LocalizedText = "LocalizedText";
        public const string StatusCode = "StatusCode";
        public const string Structure = "Structure";
        public const string Number = "Number";
        public const string Integer = "Integer";
        public const string UInteger = "UInteger";
        public const string HasComponent = "HasComponent";
        public const string HasProperty = "HasProperty";
        public const string Organizes = "Organizes";
        public const string HasEventSource = "HasEventSource";
        public const string HasNotifier = "HasNotifier";
        public const string HasSubtype = "HasSubtype";
        public const string HasTypeDefinition = "HasTypeDefinition";
        public const string HasModellingRule = "HasModellingRule";
        public const string HasEncoding = "HasEncoding";
        public const string HasDescription = "HasDescription";
        public const string HasCause = "HasCause";
        public const string ToState = "ToState";
        public const string FromState = "FromState";
        public const string HasEffect = "HasEffect";
        public const string HasTrueSubState = "HasTrueSubState";
        public const string HasFalseSubState = "HasFalseSubState";
        public const string HasDictionaryEntry = "HasDictionaryEntry";
        public const string HasCondition = "HasCondition";
        public const string HasGuard = "HasGuard";
        public const string HasAddIn = "HasAddIn";
        public const string HasInterface = "HasInterface";
        public const string GeneratesEvent = "GeneratesEvent";
        public const string AlwaysGeneratesEvent = "AlwaysGeneratesEvent";
        public const string HasOrderedComponent = "HasOrderedComponent";
        public const string HasAlarmSuppressionGroup = "HasAlarmSuppressionGroup";
        public const string AlarmGroupMember = "AlarmGroupMember";
        public const string AlarmSuppressionGroupMember = "AlarmSuppressionGroupMember";
    }

    internal static class DataTypeIds
    {
        public const string Boolean = "i=1";
        public const string SByte = "i=2";
        public const string Byte = "i=3";
        public const string Int16 = "i=4";
        public const string UInt16 = "i=5";
        public const string Int32 = "i=6";
        public const string UInt32 = "i=7";
        public const string Int64 = "i=8";
        public const string UInt64 = "i=9";
        public const string Float = "i=10";
        public const string Double = "i=11";
        public const string String = "i=12";
        public const string DateTime = "i=13";
        public const string Guid = "i=14";
        public const string ByteString = "i=15";
        public const string XmlElement = "i=16";
        public const string NodeId = "i=17";
        public const string ExpandedNodeId = "i=18";
        public const string StatusCode = "i=19";
        public const string QualifiedName = "i=20";
        public const string LocalizedText = "i=21";
        public const string Structure = "i=22";
        public const string Number = "i=26";
        public const string Integer = "i=27";
        public const string UInteger = "i=28";
    }

    internal static class ReferenceTypeIds
    {
        public const string Organizes = "i=35";
        public const string HasEventSource = "i=36";
        public const string HasModellingRule = "i=37";
        public const string HasEncoding = "i=38";
        public const string HasDescription = "i=39";
        public const string HasTypeDefinition = "i=40";
        public const string GeneratesEvent = "i=41";
        public const string HasSubtype = "i=45";
        public const string HasProperty = "i=46";
        public const string HasComponent = "i=47";
        public const string HasNotifier = "i=48";
        public const string FromState = "i=51";
        public const string ToState = "i=52";
        public const string HasCause = "i=53";
        public const string HasEffect = "i=54";
        public const string AlwaysGeneratesEvent = "i=3065";
        public const string HasTrueSubState = "i=9004";
        public const string HasFalseSubState = "i=9005";
        public const string HasCondition = "i=9006";
        public const string HasOrderedComponent = "i=14156";
        public const string HasGuard = "i=15112";
        public const string HasAlarmSuppressionGroup = "i=16361";
        public const string AlarmGroupMember = "i=16362";
        public const string HasDictionaryEntry = "i=17597";
        public const string HasInterface = "i=17603";
        public const string HasAddIn = "i=17604";
        public const string AlarmSuppressionGroupMember = "i=32060";
    }
}
