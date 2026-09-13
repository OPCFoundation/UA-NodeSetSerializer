using Xml = Opc.Ua.Export;
using Json = Opc.Ua.JsonNodeSet.Model;
using Opc.Ua;
using Opc.Ua.JsonNodeSet;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO.Compression;
using SharpCompress.Writers;
using SharpCompress.Common;
using System.Formats.Tar;
using System.Xml.Linq;
using System.Collections.ObjectModel;
using Opc.Ua.JsonNodeSet.Model;
using System.Text.Json.Nodes;
using System.Reflection.Metadata.Ecma335;

namespace NodeSetTool
{
    public partial class NodeSetSerializer
    {
        // internal, not private: the prototype format assemblies (JSONL, RDF/JSON-LD) are friends
        // of this one and need the loaded document. Internal rather than public keeps these out
        // of the package API, so they stay free to change as long as the friends change with them.
        internal Opc.Ua.JsonNodeSet.AddressSpace? m_addressSpace;
        internal Dictionary<string, Json.ModelDefinition>? m_models;
        internal Dictionary<string, Json.UANode>? m_nodes;
        internal List<Json.UANode>? m_sequence;

        private ServiceMessageContext? m_context;
        private Dictionary<string, string>? m_aliases;
        private List<CompareError> m_errors = new();

        // ServiceMessageContext is internal, so m_context cannot itself be protected. A subclass
        // loading a document from scratch needs a fresh one; this is the whole of that need.
        internal void ResetMessageContext() => m_context = new ServiceMessageContext();

        internal const string CoreNamespaceUri = "http://opcfoundation.org/UA/";

        public IReadOnlyCollection<Json.ModelDefinition> Models => m_models?.Values ?? (IReadOnlyCollection<Json.ModelDefinition>)Array.Empty<Json.ModelDefinition>();

        /// <summary>Every Node the NodeSet holds, children included.</summary>
        public int NodeCount => m_sequence?.Count ?? 0;

        // Licence/copyright declaration for the JSON NodeSet (JSON has no comment syntax, so unlike
        // the XML path this is emitted as structured data — the first field of the document).
        // Populated on load from the document's SPDX field, and by callers before a Save*/export.
        public Json.SpdxDeclaration? Spdx { get; set; }

        #region Well-Known Aliases
        private struct AliasToUse
        {
            public AliasToUse(string alias, string nodeId)
            {
                Alias = alias;
                NodeId = nodeId;
            }

            public string Alias;
            public string NodeId;
        }

        private AliasToUse[] s_AliasesToUse = new AliasToUse[]
        {
            /*
            new AliasToUse(BrowseNames.Boolean, DataTypeIds.Boolean),
            new AliasToUse(BrowseNames.SByte, DataTypeIds.SByte),
            new AliasToUse(BrowseNames.Byte, DataTypeIds.Byte),
            new AliasToUse(BrowseNames.Int16, DataTypeIds.Int16),
            new AliasToUse(BrowseNames.UInt16, DataTypeIds.UInt16),
            new AliasToUse(BrowseNames.Int32, DataTypeIds.Int32),
            new AliasToUse(BrowseNames.UInt32, DataTypeIds.UInt32),
            new AliasToUse(BrowseNames.Int64, DataTypeIds.Int64),
            new AliasToUse(BrowseNames.UInt64, DataTypeIds.UInt64),
            new AliasToUse(BrowseNames.Float, DataTypeIds.Float),
            new AliasToUse(BrowseNames.Double, DataTypeIds.Double),
            new AliasToUse(BrowseNames.DateTime, DataTypeIds.DateTime),
            new AliasToUse(BrowseNames.String, DataTypeIds.String),
            new AliasToUse(BrowseNames.ByteString, DataTypeIds.ByteString),
            new AliasToUse(BrowseNames.Guid, DataTypeIds.Guid),
            new AliasToUse(BrowseNames.XmlElement, DataTypeIds.XmlElement),
            new AliasToUse(BrowseNames.NodeId, DataTypeIds.NodeId),
            new AliasToUse(BrowseNames.ExpandedNodeId, DataTypeIds.ExpandedNodeId),
            new AliasToUse(BrowseNames.QualifiedName, DataTypeIds.QualifiedName),
            new AliasToUse(BrowseNames.LocalizedText, DataTypeIds.LocalizedText),
            new AliasToUse(BrowseNames.StatusCode, DataTypeIds.StatusCode),
            new AliasToUse(BrowseNames.Structure, DataTypeIds.Structure),
            new AliasToUse(BrowseNames.Number, DataTypeIds.Number),
            new AliasToUse(BrowseNames.Integer, DataTypeIds.Integer),
            new AliasToUse(BrowseNames.UInteger, DataTypeIds.UInteger),
            new AliasToUse(BrowseNames.HasComponent, ReferenceTypeIds.HasComponent),
            new AliasToUse(BrowseNames.HasProperty, ReferenceTypeIds.HasProperty),
            new AliasToUse(BrowseNames.Organizes, ReferenceTypeIds.Organizes),
            new AliasToUse(BrowseNames.HasEventSource, ReferenceTypeIds.HasEventSource),
            new AliasToUse(BrowseNames.HasNotifier, ReferenceTypeIds.HasNotifier),
            new AliasToUse(BrowseNames.HasSubtype, ReferenceTypeIds.HasSubtype),
            new AliasToUse(BrowseNames.HasTypeDefinition, ReferenceTypeIds.HasTypeDefinition),
            new AliasToUse(BrowseNames.HasModellingRule, ReferenceTypeIds.HasModellingRule),
            new AliasToUse(BrowseNames.HasEncoding, ReferenceTypeIds.HasEncoding),
            new AliasToUse(BrowseNames.HasDescription, ReferenceTypeIds.HasDescription),
            new AliasToUse(BrowseNames.HasCause, ReferenceTypeIds.HasCause),
            new AliasToUse(BrowseNames.ToState, ReferenceTypeIds.ToState),
            new AliasToUse(BrowseNames.FromState, ReferenceTypeIds.FromState),
            new AliasToUse(BrowseNames.HasEffect, ReferenceTypeIds.HasEffect),
            new AliasToUse(BrowseNames.HasTrueSubState, ReferenceTypeIds.HasTrueSubState),
            new AliasToUse(BrowseNames.HasFalseSubState, ReferenceTypeIds.HasFalseSubState),
            new AliasToUse(BrowseNames.HasDictionaryEntry, ReferenceTypeIds.HasDictionaryEntry),
            new AliasToUse(BrowseNames.HasCondition, ReferenceTypeIds.HasCondition),
            new AliasToUse(BrowseNames.HasGuard, ReferenceTypeIds.HasGuard),
            new AliasToUse(BrowseNames.HasAddIn, ReferenceTypeIds.HasAddIn),
            new AliasToUse(BrowseNames.HasInterface, ReferenceTypeIds.HasInterface),
            new AliasToUse(BrowseNames.GeneratesEvent, ReferenceTypeIds.GeneratesEvent),
            new AliasToUse(BrowseNames.AlwaysGeneratesEvent, ReferenceTypeIds.AlwaysGeneratesEvent),
            new AliasToUse(BrowseNames.HasOrderedComponent, ReferenceTypeIds.HasOrderedComponent),
            new AliasToUse(BrowseNames.HasAlarmSuppressionGroup, ReferenceTypeIds.HasAlarmSuppressionGroup),
            new AliasToUse(BrowseNames.AlarmGroupMember, ReferenceTypeIds.AlarmGroupMember),
            new AliasToUse(BrowseNames.AlarmSuppressionGroupMember, ReferenceTypeIds.AlarmSuppressionGroupMember)
            */
        };
        #endregion

        #region NodeSet Comparisons
        public ReadOnlyCollection<CompareError> CompareErrors => new(m_errors);

        public bool Compare(NodeSetSerializer target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            m_errors = new();

            if (!CompareModels(target))
            {
                return false;
            }

            foreach (var node in m_sequence!)
            {
                if (!target.m_nodes!.TryGetValue(node.NodeId!, out var match))
                {
                    m_errors.Add(new CompareError(node, "Node Not Found", node.NodeId, null));
                    return false;
                }

                if (!Compare(node, match))
                {
                    return false;
                }
            }

            foreach (var node in target.m_sequence!)
            {
                if (!m_nodes!.TryGetValue(node.NodeId!, out var match))
                {
                    m_errors.Add(new CompareError(node, "Extra Node Found", node.NodeId, null));
                    return false;
                }

                if (!Compare(node, match))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CompareModels(NodeSetSerializer target)
        {
            if (m_models!.Count != target.m_models!.Count)
            {
                m_errors.Add(new CompareError(null, "Model Count", m_models.Count, target.m_models.Count));
                return false;
            }

            foreach (var kvp in m_models)
            {
                if (!target.m_models.TryGetValue(kvp.Key, out var targetModel))
                {
                    m_errors.Add(new CompareError(null, "Model Not Found", kvp.Key, null));
                    return false;
                }

                var srcModel = kvp.Value;

                if (!CompareModelRef(srcModel, targetModel, srcModel.ModelUri!))
                {
                    return false;
                }

                if (!CompareRequiredModels(srcModel.RequiredModels, targetModel.RequiredModels, srcModel.ModelUri!))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CompareModelRef(Json.ModelReference original, Json.ModelReference target, string context)
        {
            if (original.ModelUri != target.ModelUri)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' ModelUri", original.ModelUri, target.ModelUri));
                return false;
            }

            if (!String.IsNullOrWhiteSpace(original.XmlSchemaUri) &&
                !String.IsNullOrWhiteSpace(target.XmlSchemaUri) &&
                original.XmlSchemaUri != target.XmlSchemaUri)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' XmlSchemaUri", original.XmlSchemaUri, target.XmlSchemaUri));
                return false;
            }

            //if (original.VarVersion != target.VarVersion)
            //{
            //    m_errors.Add(new CompareError(null, $"Model '{context}' Version", original.VarVersion, target.VarVersion));
            //    return false;
            //}

            //if (original.ModelVersion != target.ModelVersion)
            //{
            //    m_errors.Add(new CompareError(null, $"Model '{context}' ModelVersion", original.ModelVersion, target.ModelVersion));
            //    return false;
            //}

            //if (original.PublicationDate != target.PublicationDate)
            //{
            //    m_errors.Add(new CompareError(null, $"Model '{context}' PublicationDate", original.PublicationDate, target.PublicationDate));
            //    return false;
            //}

            return true;
        }

        private bool CompareRequiredModels(List<Json.ModelReference>? original, List<Json.ModelReference>? target, string context)
        {
            if (original == null && target == null) return true;

            if (original == null || target == null)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' RequiredModels", original?.Count, target?.Count));
                return false;
            }

            if (original.Count != target.Count)
            {
                m_errors.Add(new CompareError(null, $"Model '{context}' RequiredModels Count", original.Count, target.Count));
                return false;
            }

            // Order-independent comparison: match by ModelUri
            var unmatched = new List<Json.ModelReference>(target);

            foreach (var src in original)
            {
                var match = unmatched.FirstOrDefault(t => t.ModelUri == src.ModelUri);

                if (match == null)
                {
                    m_errors.Add(new CompareError(null, $"Model '{context}' RequiredModel Not Found", src.ModelUri, null));
                    return false;
                }

                if (!CompareModelRef(src, match, $"{context} -> {src.ModelUri}"))
                {
                    return false;
                }

                unmatched.Remove(match);
            }

            return true;
        }

        public class CompareError
        {
            public CompareError(Json.UANode? node, string fieldName, object? original, object? target)
            {
                Node = node;
                Message = fieldName;
                Original = original;
                Target = target;
            }

            public Json.UANode? Node { get; }

            public string Message { get; }

            public object? Original { get; }

            public object? Target { get; }

            public override string ToString()
            {
                return $"{Node?.BrowseName} [{Node?.NodeId}] {Message}: {Original} != {Target}";
            }
        }

        private bool Compare(Json.UANode? original, Json.UANode? target)
        {
            if (original == null || target == null) return false;

            if (original.NodeId != target.NodeId) { m_errors.Add(new CompareError(original, nameof(Json.UANode.NodeId), original.NodeId, target.NodeId)); return false; }
            if (original.NodeClass != target.NodeClass) { m_errors.Add(new CompareError(original, nameof(Json.UANode.NodeClass), original.NodeClass, target.NodeClass)); return false; }
            if (original.SymbolicName != target.SymbolicName) { m_errors.Add(new CompareError(original, nameof(Json.UANode.SymbolicName), original.SymbolicName, target.SymbolicName)); return false; }
            if (original.BrowseName != target.BrowseName) { m_errors.Add(new CompareError(original, nameof(Json.UANode.BrowseName), original.BrowseName, target.BrowseName)); return false; }
            if (!Compare(original.DisplayName, target.DisplayName)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.DisplayName), original.DisplayName, target.DisplayName)); return false; }
            if (!Compare(original.Description, target.Description)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.Description), original.Description, target.Description)); return false; }
            if (original.WriteMask != target.WriteMask) { m_errors.Add(new CompareError(original, nameof(Json.UANode.WriteMask), original.WriteMask, target.WriteMask)); return false; }
            if (original.Documentation != target.Documentation) { m_errors.Add(new CompareError(original, nameof(Json.UANode.Documentation), original.Documentation, target.Documentation)); return false; }
            if (original.AccessRestrictions != target.AccessRestrictions) { m_errors.Add(new CompareError(original, nameof(Json.UANode.AccessRestrictions), original.AccessRestrictions, target.AccessRestrictions)); return false; }
            if (original.HasNoPermissions != target.HasNoPermissions) { m_errors.Add(new CompareError(original, nameof(Json.UANode.HasNoPermissions), original.HasNoPermissions, target.HasNoPermissions)); return false; }
            if (original.ParentId != target.ParentId) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ParentId), original.ParentId, target.ParentId)); return false; }
            if (original.IsAbstract != target.IsAbstract) { m_errors.Add(new CompareError(original, nameof(Json.UANode.IsAbstract), original.IsAbstract, target.IsAbstract)); return false; }
            if (original.DesignToolOnly != target.DesignToolOnly) { m_errors.Add(new CompareError(original, nameof(Json.UANode.DesignToolOnly), original.DesignToolOnly, target.DesignToolOnly)); return false; }
            if (original.ModellingRuleId != target.ModellingRuleId) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ModellingRuleId), original.ModellingRuleId, target.ModellingRuleId)); return false; }
            if (original.ReleaseStatus != target.ReleaseStatus) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ReleaseStatus), original.ReleaseStatus, target.ReleaseStatus)); return false; }
            if (!Compare(original.ConformanceUnits, target.ConformanceUnits)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.ConformanceUnits), original.ConformanceUnits, target.ConformanceUnits)); return false; }
            if (!Compare(original, original.References, target.References)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.References), original.References, target.References)); return false; }
            if (!Compare(original, original.Children, target.Children)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.Children), original.Children, target.Children)); return false; }
            if (!Compare(original.RolePermissions, target.RolePermissions)) { m_errors.Add(new CompareError(original, nameof(Json.UANode.RolePermissions), original.RolePermissions, target.RolePermissions)); return false; }

            return true;
        }

        private bool Compare(IList<Json.RolePermission>? original, IList<Json.RolePermission>? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);

            if (original.Count != target.Count)
            {
                return false;
            }

            for (int ii = 0; ii < original.Count; ii++)
            {
                if (original[ii].Permissions != target[ii].Permissions) return false;
                if (original[ii].RoleId != target[ii].RoleId) return false;
            }

            return true;
        }

        private bool Compare(Json.UANode? context, Json.ChildList? original, Json.ChildList? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);

            if (original.Objects != null && target.Objects != null)
            {
                if (original.Objects.Count != target.Objects.Count)
                {
                    m_errors.Add(new CompareError(context, "ChildList.Objects.Count", original.Objects.Count, target.Objects.Count));
                    return false;
                }

                for (int ii = 0; ii < original.Objects.Count; ii++)
                {
                    if (original.Objects[ii] == null || target.Objects[ii] == null || !Compare(original.Objects[ii], target.Objects[ii]))
                    {
                        return false;
                    }
                }
            }
            else if (original.Objects != null || target.Objects != null)
            {
                return false;
            }

            if (original.Variables != null && target.Variables != null)
            {
                if (original.Variables.Count != target.Variables.Count)
                {
                    m_errors.Add(new CompareError(context, "ChildList.Variables.Count", original.Variables.Count, target.Variables.Count));
                    return false;
                }

                for (int ii = 0; ii < original.Variables.Count; ii++)
                {
                    if (original.Variables[ii] == null || target.Variables[ii] == null || !Compare(original.Variables[ii], target.Variables[ii]))
                    {
                        return false;
                    }
                }
            }
            else if (original.Variables != null || target.Variables != null)
            {
                return false;
            }

            if (original.Methods != null && target.Methods != null)
            {
                if (original.Methods.Count != target.Methods.Count)
                {
                    m_errors.Add(new CompareError(context, "ChildList.Methods.Count", original.Methods.Count, target.Methods.Count));
                    return false;
                }

                for (int ii = 0; ii < original.Methods.Count; ii++)
                {
                    if (original.Methods[ii] == null || target.Methods[ii] == null || !Compare(original.Methods[ii], target.Methods[ii]))
                    {
                        return false;
                    }
                }
            }
            else if (original.Methods != null || target.Methods != null)
            {
                return false;
            }

            return true;
        }

        private bool Compare(Json.UANode? context, IList<Json.Reference>? original, IList<Json.Reference>? target)
        {
            if (original == null || target == null)
            {
                // An absent list and an empty list both mean "no references".
                if (original == null && target == null)
                {
                    return true;
                }

                if (original != null && original.Count == 0)
                {
                    return true;
                }

                return (target != null && target.Count == 0);
            }

            if (original.Count != target.Count)
            {
                m_errors.Add(new CompareError(context, "ReferenceList", original.Count, target.Count));
                return false;
            }

            for (int ii = 0; ii < original.Count; ii++)
            {
                if (original[ii].ReferenceTypeId != target[ii].ReferenceTypeId)
                {
                    m_errors.Add(new CompareError(context, "Reference.ReferenceTypeId", original[ii].ReferenceTypeId, target[ii].ReferenceTypeId));
                    return false;
                }

                if (original[ii].IsForward != target[ii].IsForward)
                {
                    m_errors.Add(new CompareError(context, "Reference.IsForward", original[ii].IsForward, target[ii].IsForward));
                    return false;
                }

                if (original[ii].TargetId != target[ii].TargetId)
                {
                    m_errors.Add(new CompareError(context, "Reference.TargetId", original[ii].TargetId, target[ii].TargetId));
                    return false;
                }
            }

            return true;
        }

        private bool Compare(IList<string>? original, IList<string>? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);
            return original.SequenceEqual(target);
        }

        private bool Compare(Json.LocalizedText? original, Json.LocalizedText? target)
        {
            if (original == null || target == null) return Object.ReferenceEquals(original, target);

            if (original.T == null || target.T == null)
            {
                return Object.ReferenceEquals(original.T, target.T);
            }

            if (original.T.Count != target.T.Count)
            {
                return false;
            }

            for (int ii = 0; ii < original.T.Count; ii++)
            {
                if (original.T[ii] == null || target.T[ii] == null || !original.T[ii].SequenceEqual(target.T[ii]))
                {
                    return false;
                }
            }

            if (original.R == null || target.R == null)
            {
                return Object.ReferenceEquals(original.R, target.R);
            }

            if (original.R.Count != target.R.Count)
            {
                return false;
            }

            for (int ii = 0; ii < original.R.Count; ii++)
            {
                if (original.R[ii] == null || target.R[ii] == null || !original.R[ii].SequenceEqual(target.R[ii]))
                {
                    return false;
                }
            }

            return true;
        }
        #endregion

        /// <summary>
        /// Formats that can be written, as accepted by <see cref="Save(string, Stream, int)"/>:
        /// the three built in, plus whatever has been attached with <see cref="AddFormat"/>.
        /// Callers that surface a format picker should read this rather than hard-code a list —
        /// it is the only thing that reflects which assemblies the host actually references.
        /// </summary>
        public static IReadOnlyCollection<string> SupportedFormats
        {
            get
            {
                var formats = new List<string> { FormatXml, FormatJson, FormatArchive };
                foreach (var format in Formats())
                {
                    if (format.CanWrite) formats.Add(format.Id);
                }
                return formats;
            }
        }

        public const string FormatXml = "xml";
        public const string FormatJson = "json";
        public const string FormatArchive = "uanodeset";

        /// <summary>
        /// Writes the NodeSet in the named format. The single entry point callers should use:
        /// it is non-virtual so a subclass cannot bypass the unsupported-format contract, and
        /// delegates the format switch to <see cref="SaveAsFormat"/>.
        /// </summary>
        /// <summary>
        /// Writes the NodeSet to <paramref name="filePath"/> in the named format. The counterpart
        /// to <see cref="Load(string)"/>; the format is stated rather than inferred, because on the
        /// way out the caller has already chosen it.
        /// </summary>
        public void Save(string format, string filePath, int maxNodesPerFile = 10000)
        {
            // Dispatched on the path rather than through Save(format, Stream): each built-in has a
            // file overload whose behaviour depends on the name (SaveArchive compresses), and a
            // registered format may too (JSONL gzips on a .gz suffix). Opening the stream here and
            // handing it over would silently drop that.
            switch (format)
            {
                case FormatXml: SaveXml(filePath); return;
                case FormatJson: SaveJson(filePath); return;
                case FormatArchive: SaveArchive(filePath, maxNodesPerFile); return;
            }

            var handler = FindFormat(format);

            if (handler == null || !handler.CanWrite)
            {
                throw new NotSupportedException(
                    $"Format '{format}' is not supported by this build. Supported: {string.Join(", ", SupportedFormats)}.");
            }

            handler.WriteFile(this, filePath, maxNodesPerFile);
        }

        public void Save(string format, Stream stream, int maxNodesPerFile = 10000)
        {
            if (!SaveAsFormat(format, stream, maxNodesPerFile))
            {
                throw new NotSupportedException(
                    $"Format '{format}' is not supported by this build. Supported: {string.Join(", ", SupportedFormats)}.");
            }
        }

        /// <summary>
        /// Writes <paramref name="format"/> and returns true, or returns false if unrecognized.
        /// The built-in encodings are handled here; anything else is offered to the registry.
        /// </summary>
        private bool SaveAsFormat(string format, Stream stream, int maxNodesPerFile)
        {
            switch (format)
            {
                case FormatXml: SaveXml(stream); return true;
                case FormatJson: SaveJson(stream); return true;
                case FormatArchive: SaveArchive(stream, maxNodesPerFile); return true;
                default: return TrySaveRegistered(format, stream, maxNodesPerFile);
            }
        }

        public void Load(string filePath)
        {
            if (filePath.EndsWith(".xml"))
            {
                LoadXml(filePath);
                return;
            }

            // Prototype encodings are recognized by registered format assemblies, not here. Checked in
            // the extensions it claims (.jsonld, .jsonl, .jsonl.gz) used to occupy, so adding them
            // back cannot change how any other extension resolves.
            if (TryLoadRegisteredByExtension(filePath))
            {
                return;
            }

            if (filePath.EndsWith(".json"))
            {
                LoadJson(filePath);
                return;
            }
            else if (filePath.EndsWith(".tar.gz") || filePath.EndsWith(".uanodeset"))
            {
                LoadArchive(filePath);
                return;
            }

            if (IsValidXml(filePath))
            {
                LoadXml(filePath);
                return;
            }

            // Ahead of the JSON check: a JSONL document is a sequence of JSON values rather than one,
            // so IsValidJson rejects it and it would fall through to the archive reader.
            if (TryLoadRegisteredByContent(filePath))
            {
                return;
            }

            if (IsValidJson(filePath))
            {
                LoadJson(filePath);
                return;
            }

            LoadArchive(filePath);
        }

        private static bool IsValidXml(string filePath)
        {
            try
            {
                // Load the file as an XDocument. This will throw an exception if the file is not valid XML.
                XDocument.Load(filePath);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsValidJson(string filePath)
        {
            try
            {
                string json = File.ReadAllText(filePath);

                // Early exit if content is null or whitespace.
                if (String.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                // Attempt to deserialize into a dynamic object.
                JsonConvert.DeserializeObject(json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void LoadXml(string filePath)
        {
            using var istrm = File.OpenRead(filePath);
            LoadXml(istrm);
        }

        public void LoadXml(Stream stream)
        {
            // Buffered because the licence header lives in comments, which the XML deserializer
            // discards — it has to be read off the raw text before the document is parsed.
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            var spdx = SpdxComments.Parse(ReadPrologue(buffer.ToArray()));

            buffer.Position = 0;
            var input = Xml.UANodeSet.Read(buffer)!;
            ValidateLoadedXml(input);
            Initialize(input);
            Spdx = spdx;
        }

        // Everything ahead of the root element start tag, where a header comment belongs.
        private static string ReadPrologue(byte[] xml)
        {
            var text = new UTF8Encoding(false).GetString(xml).TrimStart('﻿');
            var root = System.Text.RegularExpressions.Regex.Match(text, @"<[A-Za-z]");
            return root.Success ? text.Substring(0, root.Index) : text;
        }

        public void SaveXml(string filePath)
        {
            ValidateForSave();
            var xml = BuildXml();
            using var ostrm = File.Open(filePath, FileMode.Create, FileAccess.ReadWrite);
            xml.Write(ostrm, SpdxComments.Format(Spdx));
        }

        public void SaveXml(Stream stream)
        {
            ValidateForSave();
            var xml = BuildXml();
            xml.Write(stream, SpdxComments.Format(Spdx));
        }

        public void SaveJson(string filePath)
        {
            ValidateForSave();

            JsonSerializer serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            var nodeset = BuildJson();

            using (StreamWriter file = File.CreateText(filePath))
            using (JsonTextWriter writer = new JsonTextWriter(file))
            {
                serializer.Serialize(writer, nodeset);
            }
        }

        public void SaveJson(Stream stream)
        {
            ValidateForSave();

            var serializer = new JsonSerializer
            {
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Newtonsoft.Json.Formatting.Indented,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            var nodeset = BuildJson();

            using var writer = new StreamWriter(stream, leaveOpen: true);
            using var jsonWriter = new JsonTextWriter(writer);
            serializer.Serialize(jsonWriter, nodeset);
        }

        public void LoadJson(string filePath)
        {
            LoadWellKnownAliases();

            using (FileStream fs = File.OpenRead(filePath))
            using (StreamReader js = new StreamReader(fs))
            using (JsonTextReader reader = new JsonTextReader(js))
            {
                var serializer = new JsonSerializer();
                var nodeset = serializer.Deserialize<Json.UANodeSet>(reader)!;
                ValidateLoadedJson(nodeset);
                IndexFile(nodeset);
            }
        }

        internal void LoadWellKnownAliases()
        {
            m_aliases = new();

            foreach (var alias in s_AliasesToUse)
            {
                m_aliases[alias.Alias] = alias.NodeId;
            }
        }

        public void LoadArchive(string filePath)
        {
            LoadWellKnownAliases();

            // Read every entry up front: tar is sequential, but the manifest decides the processing
            // order and is not guaranteed to be the entry that appears first.
            Dictionary<string, byte[]> entries = new(StringComparer.Ordinal);

            using (FileStream fs = File.OpenRead(filePath))
            using (GZipStream gzipStream = new GZipStream(fs, CompressionMode.Decompress))
            using (TarReader tarReader = new TarReader(gzipStream))
            {
                TarEntry? entry;

                while ((entry = tarReader.GetNextEntry()) != null)
                {
                    if (entry.EntryType == TarEntryType.V7RegularFile || entry.EntryType == TarEntryType.RegularFile)
                    {
                        using var ms = new MemoryStream();
                        entry.DataStream!.CopyTo(ms);
                        entries[entry.Name] = ms.ToArray();
                    }
                }
            }

            Json.Manifest? manifest = null;

            if (entries.TryGetValue(ManifestFileName, out var manifestBytes))
            {
                manifest = FromArchiveEntry<Json.Manifest>(manifestBytes);
                entries.Remove(ManifestFileName);
            }

            // Without a manifest the entry order is all there is to go on. Archives written by this
            // tool always carry one; the fallback keeps hand-assembled archives readable.
            var order = manifest?.Files ?? entries.Keys.ToList();

            List<Json.UANodeSet> files = new();

            foreach (var name in order)
            {
                if (!entries.TryGetValue(name, out var bytes))
                {
                    throw new InvalidDataException($"The archive manifest names '{name}', which is not in the archive.");
                }

                files.Add(FromArchiveEntry<Json.UANodeSet>(bytes));
                entries.Remove(name);
            }

            if (manifest != null && entries.Count > 0)
            {
                throw new InvalidDataException($"The archive contains files the manifest does not name: {String.Join(", ", entries.Keys)}.");
            }

            Json.UANodeSet nodeset = new Json.UANodeSet();
            nodeset.Models = manifest?.Models;

            foreach (var file in files)
            {
                if (nodeset.SPDX == null) nodeset.SPDX = file.SPDX;
                if (nodeset.Models == null) nodeset.Models = file.Models;
                if (nodeset.Nodes == null) nodeset.Nodes = new();

                if (file.Nodes!.ReferenceTypes != null)
                {
                    if (nodeset.Nodes!.ReferenceTypes == null) nodeset.Nodes.ReferenceTypes = new();
                    nodeset.Nodes.ReferenceTypes.AddRange(file.Nodes.ReferenceTypes);
                }

                if (file.Nodes.DataTypes != null)
                {
                    if (nodeset.Nodes!.DataTypes == null) nodeset.Nodes.DataTypes = new();
                    nodeset.Nodes.DataTypes.AddRange(file.Nodes.DataTypes);
                }

                if (file.Nodes.VariableTypes != null)
                {
                    if (nodeset.Nodes!.VariableTypes == null) nodeset.Nodes.VariableTypes = new();
                    nodeset.Nodes.VariableTypes.AddRange(file.Nodes.VariableTypes);
                }

                if (file.Nodes.ObjectTypes != null)
                {
                    if (nodeset.Nodes!.ObjectTypes == null) nodeset.Nodes.ObjectTypes = new();
                    nodeset.Nodes.ObjectTypes.AddRange(file.Nodes.ObjectTypes);
                }

                if (file.Nodes.Variables != null)
                {
                    if (nodeset.Nodes!.Variables == null) nodeset.Nodes.Variables = new();
                    nodeset.Nodes.Variables.AddRange(file.Nodes.Variables);
                }

                if (file.Nodes.Methods != null)
                {
                    if (nodeset.Nodes!.Methods == null) nodeset.Nodes.Methods = new();
                    nodeset.Nodes.Methods.AddRange(file.Nodes.Methods);
                }

                if (file.Nodes.Objects != null)
                {
                    if (nodeset.Nodes!.Objects == null) nodeset.Nodes.Objects = new();
                    nodeset.Nodes.Objects.AddRange(file.Nodes.Objects);
                }

                if (file.Nodes.Views != null)
                {
                    if (nodeset.Nodes!.Views == null) nodeset.Nodes.Views = new();
                    nodeset.Nodes.Views.AddRange(file.Nodes.Views);
                }
            }

            ValidateLoadedJson(nodeset);
            IndexFile(nodeset);
        }

        private static T FromArchiveEntry<T>(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var js = new StreamReader(ms);
            using var reader = new JsonTextReader(js);

            var serializer = new JsonSerializer();
            return serializer.Deserialize<T>(reader)!;
        }

        public void SaveArchive(string filePath, int maxNodesPerFile)
        {
            ValidateForSave();
            if (File.Exists(filePath)) File.Delete(filePath);

            var nodeset = BuildJson();
            var (manifest, files) = Package(nodeset, maxNodesPerFile);

            using (FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (GZipStream gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
            using (var tarWriter = WriterFactory.OpenWriter(gzipStream, ArchiveType.Tar, WriterOptions.ForTar(CompressionType.None)))
            {
                WriteArchive(tarWriter, manifest, files);
            }
        }

        // The manifest is written first so a reader can learn the processing order before it reaches
        // any of the files it names.
        private static void WriteArchive(IWriter tarWriter, Json.Manifest manifest, List<Json.UANodeSet> files)
        {
            var serializer = new JsonSerializer
            {
                Formatting = Newtonsoft.Json.Formatting.None,
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };

            WriteArchiveEntry(tarWriter, serializer, ManifestFileName, manifest);

            for (int ii = 0; ii < files.Count; ii++)
            {
                WriteArchiveEntry(tarWriter, serializer, manifest.Files![ii], files[ii]);
            }
        }

        private static void WriteArchiveEntry(IWriter tarWriter, JsonSerializer serializer, string entryName, object content)
        {
            using var memoryStream = new MemoryStream();
            using var jsonStream = new StreamWriter(memoryStream);
            using var writer = new JsonTextWriter(jsonStream);

            serializer.Serialize(writer, content);
            writer.Flush();
            memoryStream.Seek(0, SeekOrigin.Begin);
            tarWriter.Write(entryName, memoryStream, null); // null = use defaults for entry metadata
        }

        public void SaveArchive(Stream stream, int maxNodesPerFile)
        {
            ValidateForSave();
            var nodeset = BuildJson();
            var (manifest, files) = Package(nodeset, maxNodesPerFile);

            using var gzipStream = new GZipStream(stream, CompressionLevel.Optimal, leaveOpen: true);
            using var tarWriter = WriterFactory.OpenWriter(gzipStream, ArchiveType.Tar, WriterOptions.ForTar(CompressionType.None));

            WriteArchive(tarWriter, manifest, files);
        }

        private void IndexChildren(Json.UANode parent)
        {
            if (parent == null || parent.Children == null)
            {
                return;
            }

            if (parent.Children.Objects != null)
            {
                foreach (var child in parent.Children.Objects)
                {
                    child.ParentId = parent.NodeId;
                    if (m_nodes!.ContainsKey(child.NodeId!)) continue; // already indexed via flat list
                    m_nodes[child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }

            if (parent.Children.Variables != null)
            {
                foreach (var child in parent.Children.Variables)
                {
                    child.ParentId = parent.NodeId;
                    if (m_nodes!.ContainsKey(child.NodeId!)) continue;
                    m_nodes[child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }

            if (parent.Children.Methods != null)
            {
                foreach (var child in parent.Children.Methods)
                {
                    child.ParentId = parent.NodeId;
                    if (m_nodes!.ContainsKey(child.NodeId!)) continue;
                    m_nodes[child.NodeId!] = child;
                    m_sequence!.Add(child);
                    IndexChildren(child);
                }
            }
        }

        internal static IEnumerable<Json.UANode> EnumerateChildren(Json.ChildList? children)
        {
            if (children == null)
            {
                yield break;
            }

            if (children.Objects != null)
            {
                foreach (var child in children.Objects) yield return child;
            }

            if (children.Variables != null)
            {
                foreach (var child in children.Variables) yield return child;
            }

            if (children.Methods != null)
            {
                foreach (var child in children.Methods) yield return child;
            }
        }

        private static bool HasInverseReference(Json.UANode child, string? referenceTypeId, string? parentId)
        {
            if (child.References == null)
            {
                return false;
            }

            foreach (var reference in child.References)
            {
                if (reference.IsForward == false && reference.ReferenceTypeId == referenceTypeId && reference.TargetId == parentId)
                {
                    return true;
                }
            }

            return false;
        }

        // A child states its parent with ParentId and is nested under the parent's Children, so the
        // parent -> child direction of a reference is implied and is not stored in the JSON form. XML
        // NodeSets state both directions; move any forward reference from a parent to one of its own
        // children onto the child as an inverse reference (which preserves the reference type) and drop
        // the parent's copy. BuildXml re-emits the forward direction when writing XML.
        internal void CollapseParentReferences()
        {
            if (m_sequence == null || m_nodes == null)
            {
                return;
            }

            foreach (var child in m_sequence)
            {
                if (child.ParentId == null || child.NodeId == null) continue;
                if (!m_nodes.TryGetValue(child.ParentId, out var parent)) continue;
                if (parent.References == null) continue;

                for (int ii = parent.References.Count - 1; ii >= 0; ii--)
                {
                    var forward = parent.References[ii];

                    if (forward.IsForward == false || forward.TargetId != child.NodeId)
                    {
                        continue;
                    }

                    if (!HasInverseReference(child, forward.ReferenceTypeId, parent.NodeId))
                    {
                        if (child.References == null) child.References = new();

                        child.References.Add(new Json.Reference()
                        {
                            ReferenceTypeId = forward.ReferenceTypeId,
                            IsForward = false,
                            TargetId = parent.NodeId
                        });
                    }

                    parent.References.RemoveAt(ii);
                }

                if (parent.References.Count == 0)
                {
                    parent.References = null;
                }
            }
        }

        internal void IndexFile(Json.UANodeSet nodeset)
        {
            m_context = new();
            m_models = new();
            Spdx = nodeset.SPDX;

            IndexModels(nodeset.Models);

            m_nodes = new();
            m_sequence = new();

            if (nodeset.Nodes != null)
            {
                if (nodeset.Nodes!.ReferenceTypes != null)
                {
                    foreach (var node in nodeset.Nodes.ReferenceTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.DataTypes != null)
                {
                    foreach (var node in nodeset.Nodes.DataTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.VariableTypes != null)
                {
                    foreach (var node in nodeset.Nodes.VariableTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.ObjectTypes != null)
                {
                    foreach (var node in nodeset.Nodes.ObjectTypes)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.Variables != null)
                {
                    foreach (var node in nodeset.Nodes.Variables)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.Methods != null)
                {
                    foreach (var node in nodeset.Nodes.Methods)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.Objects != null)
                {
                    foreach (var node in nodeset.Nodes.Objects)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                if (nodeset.Nodes!.Views != null)
                {
                    foreach (var node in nodeset.Nodes.Views)
                    {
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }
            }

            CollapseParentReferences();
        }

        /// <summary>
        /// Registers the models and the namespaces they and their dependencies bring in. Shared by
        /// the JSON and JSONL loaders, which differ only in where the model table is read from.
        /// </summary>
        internal void IndexModels(List<Json.ModelDefinition>? models)
        {
            if (models == null) return;

            foreach (var model in models)
            {
                m_context!.NamespaceUris.GetIndexOrAppend(model.ModelUri!);

                if (model.RequiredModels != null)
                {
                    foreach (var dependency in model.RequiredModels)
                    {
                        m_context.NamespaceUris.GetIndexOrAppend(dependency.ModelUri!);
                    }
                }

                m_models![model.ModelUri!] = model;
            }
        }

        private Xml.UANodeSet BuildXml()
        {
            AssignMethodDeclarationIds();

            Xml.UANodeSet nodeset = new Xml.UANodeSet();

            List<Xml.ModelTableEntry> models = new();

            foreach (var model in m_models!.Values)
            {
                models.Add(ToXml(model));
            }

            nodeset.Models = models.ToArray();

            List<Xml.UANode> nodes = new();

            foreach (var item in m_sequence!)
            {
                switch (item)
                {
                    case Json.UAReferenceType rt: { nodes.Add(ToXmlNode(rt)); break; }
                    case Json.UADataType dt: { nodes.Add(ToXmlNode(dt)); break; }
                    case Json.UAVariableType vt: { nodes.Add(ToXmlNode(vt)); break; }
                    case Json.UAObjectType ot: { nodes.Add(ToXmlNode(ot)); break; }
                    case Json.UAObject on: { nodes.Add(ToXmlNode(on)); break; }
                    case Json.UAVariable vn: { nodes.Add(ToXmlNode(vn)); break; }
                    case Json.UAMethod mn: { nodes.Add(ToXmlNode(mn)); break; }
                    case Json.UAView wn: { nodes.Add(ToXmlNode(wn)); break; }
                }
            }

            nodeset.Items = nodes.ToArray();
            nodeset.NamespaceUris = m_context!.NamespaceUris.ToArray();
            nodeset.ServerUris = m_context.ServerUris.ToArray();

            List<Xml.NodeIdAlias> aliases = new();

            foreach (var alias in m_aliases!)
            {
                aliases.Add(new Xml.NodeIdAlias() { Alias = alias.Key, Value = alias.Value });
            }

            nodeset.Aliases = aliases.ToArray();

            return nodeset;
        }

        internal Json.UANodeSet BuildJson()
        {
            AssignMethodDeclarationIds();

            Json.UANodeSet nodeset = new Json.UANodeSet();
            nodeset.SPDX = Spdx;
            nodeset.Models = m_models!.Values.Select(m => m.ModelVersion != null ? m : new Json.ModelDefinition
            {
                ModelUri = m.ModelUri,
                XmlSchemaUri = m.XmlSchemaUri,
                PublicationDate = m.PublicationDate,
                VarVersion = m.VarVersion,
                ModelVersion = m.VarVersion,   // fall back to Version when ModelVersion absent
                DefaultAccessRestrictions = m.DefaultAccessRestrictions,
                DefaultRolePermissions = m.DefaultRolePermissions,
                RequiredModels = m.RequiredModels,
                IsPartial = m.IsPartial,
            }).ToList();
            nodeset.Nodes = new();

            foreach (var item in m_sequence!)
            {
                if (item.ParentId != null && m_nodes!.ContainsKey(item.ParentId))
                {
                    continue;
                }

                switch (item)
                {
                    case Json.UAReferenceType rt: { if (nodeset.Nodes.ReferenceTypes == null) nodeset.Nodes.ReferenceTypes = new(); nodeset.Nodes.ReferenceTypes.Add(rt); break; }
                    case Json.UADataType dt: { if (nodeset.Nodes.DataTypes == null) nodeset.Nodes.DataTypes = new(); nodeset.Nodes.DataTypes.Add(dt); break; }
                    case Json.UAVariableType vt: { if (nodeset.Nodes.VariableTypes == null) nodeset.Nodes.VariableTypes = new(); nodeset.Nodes.VariableTypes.Add(vt); break; }
                    case Json.UAObjectType ot: { if (nodeset.Nodes.ObjectTypes == null) nodeset.Nodes.ObjectTypes = new(); nodeset.Nodes.ObjectTypes.Add(ot); break; }
                    case Json.UAVariable vn: { if (nodeset.Nodes.Variables == null) nodeset.Nodes.Variables = new(); nodeset.Nodes.Variables.Add(vn); break; }
                    case Json.UAMethod mn: { if (nodeset.Nodes.Methods == null) nodeset.Nodes.Methods = new(); nodeset.Nodes.Methods.Add(mn); break; }
                    case Json.UAObject on: { if (nodeset.Nodes.Objects == null) nodeset.Nodes.Objects = new(); nodeset.Nodes.Objects.Add(on); break; }
                    case Json.UAView wn: { if (nodeset.Nodes.Views == null) nodeset.Nodes.Views = new(); nodeset.Nodes.Views.Add(wn); break; }
                }
            }

            nodeset.Nodes.ReferenceTypes = SortBySuperType(nodeset.Nodes.ReferenceTypes);
            nodeset.Nodes.DataTypes = SortBySuperType(nodeset.Nodes.DataTypes);
            nodeset.Nodes.VariableTypes = SortBySuperType(nodeset.Nodes.VariableTypes);
            nodeset.Nodes.ObjectTypes = SortBySuperType(nodeset.Nodes.ObjectTypes);

            // The lists are final, so the emission order is known and the remaining forward
            // references can be pre-declared. Only with those in hand is Ordered true.
            nodeset.Declarations = BuildDeclarations(nodeset);
            nodeset.Ordered = true;

            return nodeset;
        }

        // A Method that is a component of an Object instance points at the Method of the same
        // BrowseName declared by the Object's TypeDefinition. It is only set when the whole chain
        // resolves inside this NodeSet: the Method has a ParentId, the parent is an Object, its TypeId
        // is an ObjectType in this file, and that ObjectType or one of its supertypes declares a
        // Method with the same BrowseName. A value already present on the Method is left alone.
        private void AssignMethodDeclarationIds()
        {
            if (m_sequence == null || m_nodes == null)
            {
                return;
            }

            Dictionary<(string ParentId, string BrowseName), string>? declarations = null;

            foreach (var node in m_sequence)
            {
                if (node is not Json.UAMethod method) continue;
                if (method.MethodDeclarationId != null) continue;
                if (method.ParentId == null || method.BrowseName == null) continue;

                if (!m_nodes.TryGetValue(method.ParentId, out var parent)) continue;
                if (parent is not Json.UAObject || parent.TypeId == null) continue;
                if (!m_nodes.TryGetValue(parent.TypeId, out var typeDefinition)) continue;
                if (typeDefinition is not Json.UAObjectType) continue;

                declarations ??= IndexMethodDeclarations();

                method.MethodDeclarationId = FindMethodDeclaration(declarations, typeDefinition, method.BrowseName);
            }
        }

        // Walks from the TypeDefinition up its supertypes and returns the first Method of the given
        // BrowseName. Starting at the TypeDefinition means an override declared by a subtype wins over
        // the Method it inherits. The walk stops at the first supertype outside this NodeSet.
        private string? FindMethodDeclaration(
            Dictionary<(string ParentId, string BrowseName), string> declarations,
            Json.UANode typeDefinition,
            string browseName)
        {
            var visited = new HashSet<string>();
            var type = typeDefinition;

            while (type?.NodeId != null && visited.Add(type.NodeId))
            {
                if (declarations.TryGetValue((type.NodeId, browseName), out var declarationId))
                {
                    return declarationId;
                }

                var superTypeId = FindSuperTypeId(type);
                if (superTypeId == null || !m_nodes!.TryGetValue(superTypeId, out type)) break;
            }

            return null;
        }

        // (ParentId, BrowseName) -> NodeId for every Method that has a parent.
        private Dictionary<(string ParentId, string BrowseName), string> IndexMethodDeclarations()
        {
            var declarations = new Dictionary<(string, string), string>();

            foreach (var node in m_sequence!)
            {
                if (node is not Json.UAMethod method) continue;
                if (method.ParentId == null || method.BrowseName == null || method.NodeId == null) continue;

                declarations[(method.ParentId, method.BrowseName)] = method.NodeId;
            }

            return declarations;
        }

        // A node's supertype is the target of its inverse HasSubtype reference.
        internal static string? FindSuperTypeId(Json.UANode node)
        {
            if (node.References == null)
            {
                return null;
            }

            foreach (var reference in node.References)
            {
                if (reference.IsForward == false && reference.ReferenceTypeId == ReferenceTypeIds.HasSubtype)
                {
                    return reference.TargetId;
                }
            }

            return null;
        }

        // Orders a list of type nodes so a subtype always follows the type it derives from, which lets
        // a reader resolve a supertype without looking ahead. The sort is stable: a node keeps its
        // position unless a supertype in the same list comes later, in which case that supertype (and
        // its own supertypes) move ahead of it. Supertypes in another model, and cycles in a malformed
        // NodeSet, are left alone.
        private static List<T>? SortBySuperType<T>(List<T>? nodes) where T : Json.UANode
        {
            if (nodes == null || nodes.Count < 2)
            {
                return nodes;
            }

            var byId = new Dictionary<string, T>();

            foreach (var node in nodes)
            {
                if (node.NodeId != null && !byId.ContainsKey(node.NodeId))
                {
                    byId.Add(node.NodeId, node);
                }
            }

            var sorted = new List<T>(nodes.Count);
            var emitted = new HashSet<T>();
            var visiting = new HashSet<T>();

            void Emit(T node)
            {
                if (emitted.Contains(node) || !visiting.Add(node))
                {
                    return;
                }

                var superTypeId = FindSuperTypeId(node);

                if (superTypeId != null && byId.TryGetValue(superTypeId, out var superType) && !ReferenceEquals(superType, node))
                {
                    Emit(superType);
                }

                visiting.Remove(node);
                emitted.Add(node);
                sorted.Add(node);
            }

            foreach (var node in nodes)
            {
                Emit(node);
            }

            return sorted;
        }

        // The name of the archive entry that lists the files and carries the Models. Every other
        // entry in the archive must be named by it.
        public const string ManifestFileName = Json.Manifest.FileName;

        private static string ArchiveFileName(int index) => $"UANodeSet_{index + 1:D3}.json";

        private Json.UANodeSet NewFile(Json.UANodeSet nodeset, int currentFileCount)
        {
            // Models and Declarations live in the manifest, so a member file only flags that one
            // exists and carries its share of the Nodes. The split preserves the order the Nodes
            // were in, so each file is as ordered as the whole.
            return new Json.UANodeSet()
            {
                Ordered = nodeset.Ordered,
                HasManifest = true,
                Nodes = new()
            };
        }

        private int CountNodes(Json.UANode node)
        {
            if (node?.Children == null)
            {
                return 1;
            }

            int count = 1;

            if (node.Children.Objects != null)
            {
                foreach (var child in node.Children.Objects)
                {
                    count += CountNodes(child);
                }
            }

            if (node.Children.Variables != null)
            {
                foreach (var child in node.Children.Variables)
                {
                    count += CountNodes(child);
                }
            }

            if (node.Children.Methods != null)
            {
                foreach (var child in node.Children.Methods)
                {
                    count += CountNodes(child);
                }
            }

            return count;
        }


        private (Json.Manifest Manifest, List<Json.UANodeSet> Files) Package(Json.UANodeSet nodeset, int maxNodesPerFile)
        {
            List<Json.UANodeSet> files = new List<Json.UANodeSet>();

            int count = 0;

            Json.UANodeSet current = NewFile(nodeset, files.Count);
            // The SPDX declaration, unlike Models, is not part of the manifest, so it goes on the
            // first file of the archive.
            current.SPDX = nodeset.SPDX;

            if (nodeset.Nodes!.ReferenceTypes != null)
            {
                current.Nodes!.ReferenceTypes = new();

                foreach (var node in nodeset.Nodes.ReferenceTypes)
                {
                    current.Nodes!.ReferenceTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.ReferenceTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.DataTypes != null)
            {
                current.Nodes!.DataTypes = new();

                foreach (var node in nodeset.Nodes.DataTypes)
                {
                    current.Nodes!.DataTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.DataTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.VariableTypes != null)
            {
                current.Nodes!.VariableTypes = new();

                foreach (var node in nodeset.Nodes.VariableTypes)
                {
                    current.Nodes!.VariableTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.VariableTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.ObjectTypes != null)
            {
                current.Nodes!.ObjectTypes = new();

                foreach (var node in nodeset.Nodes.ObjectTypes)
                {
                    current.Nodes!.ObjectTypes.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.ObjectTypes = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.Variables != null)
            {
                current.Nodes!.Variables = new();

                foreach (var node in nodeset.Nodes.Variables)
                {
                    current.Nodes!.Variables.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.Variables = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.Methods != null)
            {
                current.Nodes!.Methods = new();

                foreach (var node in nodeset.Nodes.Methods)
                {
                    current.Nodes!.Methods.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.Methods = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.Objects != null)
            {
                current.Nodes!.Objects = new();

                foreach (var node in nodeset.Nodes.Objects)
                {
                    current.Nodes!.Objects.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.Objects = new();
                        count = 0;
                    }
                }
            }

            if (nodeset.Nodes!.Views != null)
            {
                current.Nodes!.Views = new();

                foreach (var node in nodeset.Nodes.Views)
                {
                    current.Nodes!.Views.Add(node);
                    count += CountNodes(node);

                    if (count > maxNodesPerFile)
                    {
                        files.Add(current);
                        current = NewFile(nodeset, files.Count);
                        current.Nodes!.Views = new();
                        count = 0;
                    }
                }
            }

            files.Add(current);

            // Splitting the Nodes across files only makes forward references longer-range, never
            // fewer, so the whole-document Declarations carry over unchanged — to the manifest,
            // which is read before any of the files it names.
            var manifest = new Json.Manifest()
            {
                Ordered = nodeset.Ordered,
                Models = nodeset.Models,
                Declarations = nodeset.Declarations,
                Files = Enumerable.Range(0, files.Count).Select(ArchiveFileName).ToList()
            };

            return (manifest, files);
        }

        private void Initialize(Opc.Ua.Export.UANodeSet input)
        {
            m_context = new ServiceMessageContext();

            if (input.NamespaceUris != null)
            {
                foreach (var uri in input.NamespaceUris)
                {
                    m_context.NamespaceUris.GetIndexOrAppend(uri);
                }
            }

            if (input.ServerUris != null)
            {
                foreach (var uri in input.ServerUris)
                {
                    m_context.ServerUris.GetIndexOrAppend(uri);
                }
            }

            m_aliases = new();

            if (input.Aliases != null)
            {
                foreach (var alias in input.Aliases)
                {
                    m_aliases[alias.Alias!] = alias.Value!;
                }
            }

            m_models = new();

            if (input.Models != null)
            {
                foreach (var item in input.Models)
                {
                    var model = ToJson(item);
                    m_models[model.ModelUri!] = model;
                }
            }

            m_nodes = new();
            m_sequence = new();

            if (input.Items != null)
            {
                foreach (var item in input.Items)
                {
                    var node = ToJson(item)!;
                    m_nodes[node.NodeId!] = node;
                    m_sequence.Add(node);
                }
            }

            RebuildChildLists();

            CollapseParentReferences();
        }

        /// <summary>
        /// Attaches every owned Node to its parent's ChildList. A flat sequence — an XML NodeSet, or
        /// the JSONL layout — states ownership on ParentId alone, and the writers nest a child under
        /// its parent, so the nesting has to be rebuilt before anything is written back out.
        /// </summary>
        internal void RebuildChildLists()
        {
            foreach (var item in m_sequence!)
            {
                if (item.ParentId == null) continue;
                if (!m_nodes!.TryGetValue(item.ParentId, out var parent)) continue;

                parent.Children ??= new Json.ChildList();

                switch (item)
                {
                    case Json.UAObject od: { (parent.Children.Objects ??= new()).Add(od); break; }
                    case Json.UAVariable vn: { (parent.Children.Variables ??= new()).Add(vn); break; }
                    case Json.UAMethod mn: { (parent.Children.Methods ??= new()).Add(mn); break; }
                }
            }
        }

        private Json.UANode? ToJson(Xml.UANode input)
        {
            switch (input)
            {
                case Xml.UAObject od: return ToJsonNode(od);
                case Xml.UAVariable vn: return ToJsonNode(vn);
                case Xml.UAMethod mn: return ToJsonNode(mn);
                case Xml.UAObjectType ot: return ToJsonNode(ot);
                case Xml.UAVariableType vt: return ToJsonNode(vt);
                case Xml.UADataType dt: return ToJsonNode(dt);
                case Xml.UAReferenceType rt: return ToJsonNode(rt);
                case Xml.UAView wn: return ToJsonNode(wn);
            }

            return null;
        }

        private Json.ModelDefinition ToJson(Xml.ModelTableEntry input)
        {
            Json.ModelDefinition output = new();

            output.ModelUri = input.ModelUri;
            output.XmlSchemaUri = input.XmlSchemaUri;
            output.VarVersion = input.Version;
            output.ModelVersion = input.ModelVersion;
            output.PublicationDate = input.PublicationDateSpecified ? input.PublicationDate : null;
            output.DefaultAccessRestrictions = input.AccessRestrictions != 0 ? input.AccessRestrictions : null;
            output.DefaultRolePermissions = ToJsonRolePermission(input.RolePermissions);

            if (input.RequiredModel != null)
            {
                output.RequiredModels = new();

                foreach (var item in input.RequiredModel)
                {
                    output.RequiredModels.Add(new Json.ModelReference()
                    {
                        ModelUri = item.ModelUri,
                        XmlSchemaUri = item.XmlSchemaUri,
                        VarVersion = item.Version,
                        ModelVersion = item.ModelVersion,
                        PublicationDate = item.PublicationDateSpecified ? item.PublicationDate : null
                    });
                }
            }

            return output;
        }

        private Xml.ModelTableEntry ToXml(Json.ModelDefinition input)
        {
            Xml.ModelTableEntry output = new();

            output.ModelUri = input.ModelUri;
            output.XmlSchemaUri = input.XmlSchemaUri;
            output.Version = input.VarVersion;
            output.ModelVersion = input.ModelVersion ?? input.VarVersion;   // fall back to Version when ModelVersion absent
            output.PublicationDate = input.PublicationDate ?? DateTime.MinValue;
            output.PublicationDateSpecified = input.PublicationDate != null;
            output.AccessRestrictions = (ushort)(input.DefaultAccessRestrictions ?? 0);
            output.RolePermissions = ToXmlRolePermission(input.DefaultRolePermissions);

            if (input.RequiredModels != null)
            {
                List<Xml.ModelTableEntry> models = new();

                foreach (var item in input.RequiredModels)
                {
                    models.Add(new Xml.ModelTableEntry()
                    {
                        ModelUri = item.ModelUri,
                        XmlSchemaUri = item.XmlSchemaUri,
                        Version = item.VarVersion,
                        ModelVersion = item.ModelVersion,
                        PublicationDate = item.PublicationDate ?? DateTime.MinValue,
                        PublicationDateSpecified = item.PublicationDate != null
                    });
                }

                output.RequiredModel = models.ToArray();
            }

            return output;
        }


        private void Update(Json.UANode output, Xml.UANode input)
        {
            output.NodeId = ToJsonNodeId(input.NodeId);
            output.SymbolicName = input.SymbolicName;
            output.BrowseName = ToJsonQualifiedName(input.BrowseName);
            output.DisplayName = ToJsonLocalizedText(input.DisplayName);
            output.Description = ToJsonLocalizedText(input.Description);
            output.WriteMask = input.WriteMask != 0 ? input.WriteMask : null;
            output.Documentation = input.Documentation;
            output.ConformanceUnits = input.Category != null ? new(input.Category) : null;
            output.ReleaseStatus = ToJsonReleaseStatus(input.ReleaseStatus);
            output.HasNoPermissions = input.HasNoPermissions ? input.HasNoPermissions : null;
            output.RolePermissions = ToJsonRolePermission(input.RolePermissions);
            output.AccessRestrictions = (input.AccessRestrictionsSpecified && input.AccessRestrictions != 0) ? input.AccessRestrictions : null;

            if (input is Xml.UAInstance instance)
            {
                output.ParentId = ToJsonNodeId(instance.ParentNodeId);
                output.DesignToolOnly = instance.DesignToolOnly ? instance.DesignToolOnly : null;
            }

            if (input is Xml.UAType type)
            {
                output.IsAbstract = type.IsAbstract ? type.IsAbstract : null;
            }

            QualifiedName qname;

            try
            {
                qname = QualifiedName.Parse(m_context, output.BrowseName!, false);
            }
            catch (Exception)
            {
                qname = output.BrowseName!;
            }

            if (output.SymbolicName == qname.Name)
            {
                output.SymbolicName = null;
            }

            if (output.DisplayName?.T?.Count == 1)
            {
                if (output.DisplayName.T[0].Count >= 2 && output.DisplayName.T[0][1] == qname.Name)
                {
                    output.DisplayName = null;
                }
            }

            if (input.References != null)
            {
                List<Json.Reference> references = new();

                foreach (var reference in input.References)
                {
                    var referenceTypeId = ToJsonNodeId(reference.ReferenceType);

                    if (referenceTypeId == ReferenceTypeIds.HasModellingRule)
                    {
                        output.ModellingRuleId = ToJsonNodeId(reference.Value);
                        continue;
                    }

                    if (referenceTypeId == ReferenceTypeIds.HasTypeDefinition)
                    {
                        output.TypeId = ToJsonNodeId(reference.Value);
                        continue;
                    }

                    references.Add(new Json.Reference()
                    {
                        ReferenceTypeId = referenceTypeId,
                        IsForward = !reference.IsForward ? reference.IsForward : null,
                        TargetId = ToJsonNodeId(reference.Value)
                    });
                }

                output.References = references;
            }
        }

        private void Update(Xml.UANode output, Json.UANode input)
        {
            output.NodeId = ToXmlNodeId(input.NodeId, true);
            output.SymbolicName = input.SymbolicName;
            output.BrowseName = ToXmlQualifiedName(input.BrowseName);
            output.DisplayName = ToXmlLocalizedText(input.DisplayName);
            output.Description = ToXmlLocalizedText(input.Description);
            output.WriteMask = (uint)(input.WriteMask ?? 0);
            output.Documentation = input.Documentation;
            output.Category = input.ConformanceUnits != null ? input.ConformanceUnits.ToArray() : null;
            output.ReleaseStatus = ToXmlReleaseStatus(input.ReleaseStatus);
            output.HasNoPermissions = input.HasNoPermissions ?? false;
            output.RolePermissions = ToXmlRolePermission(input.RolePermissions);
            output.AccessRestrictions = (ushort)(input.AccessRestrictions ?? 0);
            output.AccessRestrictionsSpecified = input.AccessRestrictions != null && input.AccessRestrictions != 0;

            if (output is Xml.UAInstance instance)
            {
                instance.ParentNodeId = ToXmlNodeId(input.ParentId);
                instance.DesignToolOnly = input.DesignToolOnly ?? false;
            }

            if (output is Xml.UAType type)
            {
                type.IsAbstract = input.IsAbstract ?? false;
            }

            QualifiedName qname;

            try
            {
                qname = QualifiedName.Parse(m_context, output.BrowseName!, false);
            }
            catch (Exception)
            {
                qname = output.BrowseName!;
            }

            if (output.SymbolicName == qname.Name)
            {
                output.SymbolicName = null;
            }

            // Part 6 permits omitting a DisplayName that repeats the BrowseName, and the JSON
            // form drops it on that basis. The XML form always carries one: consumers that read
            // the file without an address space (diff tools, spreadsheets, importers) should not
            // have to reconstruct it, so synthesise it from the BrowseName when it is absent.
            if ((output.DisplayName == null || output.DisplayName.Length == 0)
                && !String.IsNullOrEmpty(qname.Name))
            {
                output.DisplayName = new Xml.LocalizedText[] { new() { Value = qname.Name } };
            }

            List<Xml.Reference> references = new();

            if (!String.IsNullOrEmpty(input.ModellingRuleId))
            {
                references.Add(new Xml.Reference()
                {
                    ReferenceType = ReferenceTypeIds.HasModellingRule,
                    IsForward = true,
                    Value = ToXmlNodeId(input.ModellingRuleId)
                });
            }

            if (!String.IsNullOrEmpty(input.TypeId))
            {
                references.Add(new Xml.Reference()
                {
                    ReferenceType = ReferenceTypeIds.HasTypeDefinition,
                    IsForward = true,
                    Value = ToXmlNodeId(input.TypeId)
                });
            }

            if (input.References != null)
            {
                foreach (var reference in input.References)
                {
                    references.Add(new Xml.Reference()
                    {
                        ReferenceType = ToXmlNodeId(reference.ReferenceTypeId),
                        IsForward = reference.IsForward ?? true,
                        Value = ToXmlNodeId(reference.TargetId)
                    });
                }
            }

            // The JSON form omits the parent -> child direction because ParentId implies it. XML
            // NodeSets state both directions, so re-emit a forward reference for every child that
            // points back at this node.
            foreach (var child in EnumerateChildren(input.Children))
            {
                if (child.References == null) continue;

                foreach (var reference in child.References)
                {
                    if (reference.IsForward != false || reference.TargetId != input.NodeId)
                    {
                        continue;
                    }

                    var referenceType = ToXmlNodeId(reference.ReferenceTypeId);
                    var targetId = ToXmlNodeId(child.NodeId);

                    if (references.Any(r => r.IsForward && r.ReferenceType == referenceType && r.Value == targetId))
                    {
                        continue;
                    }

                    references.Add(new Xml.Reference()
                    {
                        ReferenceType = referenceType,
                        IsForward = true,
                        Value = targetId
                    });
                }
            }

            if (references.Count > 0)
            {
                output.References = references.ToArray();
            }
        }

        private Json.UAObjectType ToJsonNode(Xml.UAObjectType input)
        {
            var output = new Json.UAObjectType();
            output.NodeClass = Json.NodeClass.UAObjectType;
            Update(output, input);
            return output;
        }

        private Xml.UAObjectType ToXmlNode(Json.UAObjectType input)
        {
            var output = new Xml.UAObjectType();
            Update(output, input);
            return output;
        }

        private Json.UAVariableType ToJsonNode(Xml.UAVariableType input)
        {
            var output = new Json.UAVariableType();
            output.NodeClass = Json.NodeClass.UAVariableType;
            Update(output, input);
            output.DataType = ToJsonNodeId(input.DataType);
            output.ValueRank = input.ValueRank != ValueRanks.Scalar ? input.ValueRank : null;
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Value = ToJsonVariant(input.Value);
            return output;
        }

        private Xml.UAVariableType ToXmlNode(Json.UAVariableType input)
        {
            var output = new Xml.UAVariableType();
            Update(output, input);
            output.DataType = ToXmlNodeId(input.DataType) ?? "i=24";
            output.ValueRank = (int)(input.ValueRank ?? ValueRanks.Scalar);
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Value = ToXmlVariant(input.Value);
            return output;
        }

        private Json.UADataType ToJsonNode(Xml.UADataType input)
        {
            var output = new Json.UADataType();
            output.NodeClass = Json.NodeClass.UADataType;
            Update(output, input);
            output.Purpose = ToJsonDataTypePurpose(input.Purpose);
            output.Definition = ToJsonDataTypeDefinition(input.Definition);
            return output;
        }

        private Xml.UADataType ToXmlNode(Json.UADataType input)
        {
            var output = new Xml.UADataType();
            Update(output, input);
            output.Purpose = ToXmlDataTypePurpose(input.Purpose);
            output.Definition = ToXmlDataTypeDefinition(input.Definition, output.BrowseName);
            return output;
        }

        private Json.UAReferenceType ToJsonNode(Xml.UAReferenceType input)
        {
            var output = new Json.UAReferenceType();
            output.NodeClass = Json.NodeClass.UAReferenceType;
            Update(output, input);
            output.InverseName = ToJsonLocalizedText(input.InverseName);
            // Symmetric defaults to false (UANodeSet.xsd), so omit it in that case and
            // treat a missing value as false on the way back. The operands used to be
            // swapped, which made null mean "symmetric": a node built in memory rather
            // than parsed (a newly created ReferenceType) came out Symmetric="true",
            // and a parsed symmetric type reported false to everything reading the model.
            output.Symmetric = input.Symmetric ? true : null;
            return output;
        }

        private Xml.UAReferenceType ToXmlNode(Json.UAReferenceType input)
        {
            var output = new Xml.UAReferenceType();
            Update(output, input);
            output.InverseName = ToXmlLocalizedText(input.InverseName);
            output.Symmetric = input.Symmetric ?? false;
            return output;
        }

        private Json.UAObject ToJsonNode(Xml.UAObject input)
        {
            var output = new Json.UAObject();
            output.NodeClass = Json.NodeClass.UAObject;
            Update(output, input);
            output.EventNotifier = input.EventNotifier != EventNotifiers.None ? input.EventNotifier : null;
            return output;
        }

        private Xml.UAObject ToXmlNode(Json.UAObject input)
        {
            var output = new Xml.UAObject();
            Update(output, input);
            output.EventNotifier = (byte)(input.EventNotifier ?? EventNotifiers.None);
            return output;
        }

        private Json.UAVariable ToJsonNode(Xml.UAVariable input)
        {
            var output = new Json.UAVariable();
            output.NodeClass = Json.NodeClass.UAVariable;
            Update(output, input);
            output.DataType = ToJsonNodeId(input.DataType);
            output.ValueRank = input.ValueRank != ValueRanks.Scalar ? input.ValueRank : null;
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Historizing = input.Historizing ? input.Historizing : null;
            output.MinimumSamplingInterval = input.MinimumSamplingInterval != 0 ? (decimal)input.MinimumSamplingInterval : null;
            output.Value = ToJsonVariant(input.Value);
            return output;
        }

        private Xml.UAVariable ToXmlNode(Json.UAVariable input)
        {
            var output = new Xml.UAVariable();
            Update(output, input);
            output.DataType = ToXmlNodeId(input.DataType) ?? "i=24";
            output.ValueRank = input.ValueRank ?? ValueRanks.Scalar;
            output.ArrayDimensions = !String.IsNullOrEmpty(input.ArrayDimensions) ? input.ArrayDimensions : null;
            output.Historizing = input.Historizing ?? false;
            output.MinimumSamplingInterval = (double)(input.MinimumSamplingInterval ?? 0);
            output.Value = ToXmlVariant(input.Value);
            return output;
        }

        private Json.UAMethod ToJsonNode(Xml.UAMethod input)
        {
            var output = new Json.UAMethod();
            output.NodeClass = Json.NodeClass.UAMethod;
            Update(output, input);
            output.Executable = !input.Executable ? input.Executable : null;
            output.MethodDeclarationId = ToJsonNodeId(input.MethodDeclarationId);
            return output;
        }

        private Xml.UAMethod ToXmlNode(Json.UAMethod input)
        {
            var output = new Xml.UAMethod();
            Update(output, input);
            output.Executable = input.Executable ?? true;
            output.MethodDeclarationId = ToXmlNodeId(input.MethodDeclarationId);
            return output;
        }

        private Json.UAView ToJsonNode(Xml.UAView input)
        {
            var output = new Json.UAView();
            output.NodeClass = Json.NodeClass.UAView;
            Update(output, input);
            output.EventNotifier = input.EventNotifier != EventNotifiers.None ? input.EventNotifier : null;
            output.ContainsNoLoops = input.ContainsNoLoops ? input.ContainsNoLoops : null;
            return output;
        }

        private Xml.UAView ToXmlNode(Json.UAView input)
        {
            var output = new Xml.UAView();
            Update(output, input);
            output.EventNotifier = (byte)(input.EventNotifier ?? EventNotifiers.None);
            output.ContainsNoLoops = input.ContainsNoLoops ?? false;
            return output;
        }


        private object? ToJson(XmlElement? input)
        {
            if (input == null)
            {
                return null;
            }

            if (input.NamespaceURI == CoreNamespaceUri)
            {
                switch (input.LocalName)
                {
                    case nameof(BuiltInType.Boolean):
                        {
                            if (!Boolean.TryParse(input.InnerText, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a Boolean.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.SByte):
                        {
                            if (!SByte.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a SByte.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.Byte):
                        {
                            if (!Byte.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a Byte.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.Int16):
                        {
                            if (!Int16.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a Int16.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.UInt16):
                        {
                            if (!Int16.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a UInt16.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.Int32):
                        {
                            if (!Int32.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a Int32.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.UInt32):
                        {
                            if (!UInt32.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a UInt32.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.Int64):
                        {
                            if (!Int64.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a Int64.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.UInt64):
                        {
                            if (!UInt64.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a UInt64.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.Float):
                        {
                            if (!Single.TryParse(input.InnerText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a                     case nameof(BuiltInType.Float):\r\n.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.Double):
                        {
                            if (!Double.TryParse(input.InnerText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is not a valid Double.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.String):
                        {
                            return input.InnerText;
                        }

                    case nameof(BuiltInType.ByteString):
                        {
                            byte[] bytes = new byte[(input.InnerText.Length * 3) / 4];

                            if (!Convert.TryFromBase64String(input.InnerText, bytes, out var bytesWritten))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a ByteString.");
                            }

                            return bytes.AsSpan(0, bytesWritten).ToArray();
                        }

                    case nameof(BuiltInType.DateTime):
                        {
                            try
                            {
                                return XmlConvert.ToDateTime(input.InnerText, XmlDateTimeSerializationMode.Utc);
                            }
                            catch
                            {
                                throw new InvalidDataException($"{input.InnerText} is a DateTime.");
                            }
                        }

                    case nameof(BuiltInType.Guid):
                        {
                            var child = input.FirstChild as XmlElement;

                            if (child == null || child.LocalName != "String" || String.IsNullOrEmpty(child.InnerText))
                            {
                                return Guid.Empty;
                            }

                            if (!Guid.TryParse(child.InnerText, out var value))
                            {
                                throw new InvalidDataException($"{child.InnerText} is a Guid.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.NodeId):
                    case nameof(BuiltInType.ExpandedNodeId):
                        {
                            var child = input.FirstChild as XmlElement;

                            if (child == null || child.LocalName != "Identifier")
                            {
                                return String.Empty;
                            }

                            return child.InnerText;
                        }

                    case nameof(BuiltInType.StatusCode):
                        {
                            var child = input.FirstChild as XmlElement;

                            if (child == null || child.LocalName != "Code")
                            {
                                return 0U;
                            }

                            if (!UInt32.TryParse(input.InnerText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value))
                            {
                                throw new InvalidDataException($"{input.InnerText} is a StatusCode.");
                            }

                            return value;
                        }

                    case nameof(BuiltInType.QualifiedName):
                        {
                            ushort ns = 0;
                            string name = String.Empty;
                            var child = input.FirstChild as XmlElement;

                            if (child != null)
                            {
                                if (child.LocalName == "NamespaceIndex")
                                {
                                    if (!Int16.TryParse(child.InnerText, out var value))
                                    {
                                        throw new InvalidDataException($"{input.InnerText} is a QualifiedName.");
                                    }

                                    child = child.NextSibling as XmlElement;
                                }
                            }

                            if (child != null)
                            {
                                if (child.LocalName == "Name")
                                {
                                    name = child.InnerText.Trim();
                                }
                            }

                            return (ns != 0) ? $"{ns}:{name}" : name;
                        }

                    case nameof(BuiltInType.LocalizedText):
                        {
                            string locale = String.Empty;
                            string text = String.Empty;
                            var child = input.FirstChild as XmlElement;

                            if (child != null)
                            {
                                if (child.LocalName == "Locale")
                                {
                                    locale = child.InnerText.Trim();
                                    child = child.NextSibling as XmlElement;
                                }
                            }

                            if (child != null)
                            {
                                if (child.LocalName == "Text")
                                {
                                    text = child.InnerText.Trim();
                                }
                            }

                            return new LocalizedText
                            {
                                T = new List<List<string>> { new() { locale, text } }
                            };
                        }

                    case nameof(BuiltInType.ExtensionObject):
                        {
                            // Delegate to the shared converter so the full envelope (TypeId + Body)
                            // is parsed into a Json.ExtensionObject with a JObject body.
                            return Opc.Ua.JsonNodeSet.VariantConverter
                                .ReadVariantFromXml(input, MakeVariantContext())?.Value;
                        }

                }
            }

            // Unknown element namespace/name — return null to signal "not a built-in type".
            return null;
        }

        private Opc.Ua.JsonNodeSet.VariantXmlContext MakeVariantContext() =>
            new Opc.Ua.JsonNodeSet.VariantXmlContext(m_context, m_addressSpace);

        /// <summary>
        /// Converts an OPC UA Value XmlElement to a JSON Variant. See
        /// <see cref="Opc.Ua.JsonNodeSet.VariantConverter"/> for the full conversion contract.
        /// </summary>
        private Json.Variant? ToJsonVariant(XmlElement? input) =>
            Opc.Ua.JsonNodeSet.VariantConverter.ReadVariantFromXml(input, MakeVariantContext());

        /// <summary>
        /// Converts a JSON Variant to an OPC UA Value XmlElement. Delegates to
        /// <see cref="Opc.Ua.JsonNodeSet.VariantConverter"/>.
        /// </summary>
        private XmlElement? ToXmlVariant(Json.Variant? input) =>
            Opc.Ua.JsonNodeSet.VariantConverter.WriteVariantToXml(input, MakeVariantContext());

#if LEGACY_TOXMLVARIANT
        private XmlElement? ToXmlVariantLegacy(Json.Variant? input)
        {
            if (input?.Value == null) return null;

            var doc = new XmlDocument();
            var uaNs = "http://opcfoundation.org/UA/2008/02/Types.xsd";

            var builtInType = (BuiltInType)(input.UaType ?? (int)BuiltInType.String);
            var typeName = UaTypeToName(builtInType);

            // Helper: creates a typed XML element for a single value per Opc.Ua.Types.xsd
            XmlElement MakeValueElement(string tn, BuiltInType bt, object val)
            {
                var el = doc.CreateElement("uax", tn, uaNs);
                var text = Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture) ?? "";

                switch (bt)
                {
                    case BuiltInType.Guid:
                    {
                        var c = doc.CreateElement("uax", nameof(BuiltInType.String), uaNs);
                        c.InnerText = text;
                        el.AppendChild(c);
                        break;
                    }
                    case BuiltInType.NodeId:
                    case BuiltInType.ExpandedNodeId:
                    {
                        // Convert nsu= format to ns= index format for XML
                        var c = doc.CreateElement("uax", "Identifier", uaNs);
                        c.InnerText = NsuToNsIndex(text);
                        el.AppendChild(c);
                        break;
                    }
                    case BuiltInType.StatusCode:
                    {
                        var c = doc.CreateElement("uax", "Code", uaNs);
                        c.InnerText = text;
                        el.AppendChild(c);
                        break;
                    }
                    case BuiltInType.QualifiedName:
                    {
                        // Parse "nsu=uri;Name" or "nsIndex:Name" format
                        string nsIndex = "0";
                        string qnName = text;

                        if (text.StartsWith("nsu="))
                        {
                            var semiIdx = text.IndexOf(';');
                            if (semiIdx > 4)
                            {
                                var uri = text[4..semiIdx];
                                nsIndex = m_context!.NamespaceUris.GetIndexOrAppend(uri).ToString();
                                qnName = text[(semiIdx + 1)..];
                            }
                        }
                        else
                        {
                            var colonIdx = text.IndexOf(':');
                            if (colonIdx > 0 && int.TryParse(text[..colonIdx], out _))
                            {
                                nsIndex = text[..colonIdx];
                                qnName = text[(colonIdx + 1)..];
                            }
                        }

                        var nsIdxEl = doc.CreateElement("uax", "NamespaceIndex", uaNs);
                        nsIdxEl.InnerText = nsIndex;
                        el.AppendChild(nsIdxEl);
                        var nameEl = doc.CreateElement("uax", "Name", uaNs);
                        nameEl.InnerText = qnName;
                        el.AppendChild(nameEl);
                        break;
                    }
                    case BuiltInType.LocalizedText:
                    {
                        var c = doc.CreateElement("uax", "Text", uaNs);
                        c.InnerText = text;
                        el.AppendChild(c);
                        break;
                    }
                    default:
                        el.InnerText = text;
                        break;
                }
                return el;
            }

            // Return the typed element directly — the caller (serializer) wraps it in <Value>
            XmlElement result;

            if (input.Value is System.Collections.IList list)
            {
                if (input.Dimensions != null && input.Dimensions.Count > 1)
                {
                    // Multi-dimensional: <Matrix> with <Dimensions> and <Elements>
                    result = doc.CreateElement("uax", "Matrix", uaNs);

                    var dimsEl = doc.CreateElement("uax", "Dimensions", uaNs);
                    foreach (var dim in input.Dimensions)
                    {
                        var dimEl = doc.CreateElement("uax", "Int32", uaNs);
                        dimEl.InnerText = dim.ToString();
                        dimsEl.AppendChild(dimEl);
                    }
                    result.AppendChild(dimsEl);

                    var elemsEl = doc.CreateElement("uax", "Elements", uaNs);
                    foreach (var item in list)
                        elemsEl.AppendChild(MakeValueElement(typeName, builtInType, item));
                    result.AppendChild(elemsEl);
                }
                else
                {
                    // One-dimensional: <ListOf{Type}>
                    result = doc.CreateElement("uax", $"ListOf{typeName}", uaNs);
                    foreach (var item in list)
                        result.AppendChild(MakeValueElement(typeName, builtInType, item));
                }
            }
            else
            {
                result = MakeValueElement(typeName, builtInType, input.Value);
            }

            doc.AppendChild(result);
            return doc.DocumentElement!;
        }
#endif

        /// <summary>Parses a string value into the correct CLR type for the given built-in type.</summary>
        private static object ParseTypedValue(BuiltInType type, string text) => type switch
        {
            BuiltInType.Boolean => bool.TryParse(text, out var bv) ? (object)bv : false,
            BuiltInType.SByte => sbyte.TryParse(text, out var sbv) ? (object)sbv : (sbyte)0,
            BuiltInType.Byte => byte.TryParse(text, out var byv) ? (object)byv : (byte)0,
            BuiltInType.Int16 => short.TryParse(text, out var sv) ? (object)sv : (short)0,
            BuiltInType.UInt16 => ushort.TryParse(text, out var usv) ? (object)usv : (ushort)0,
            BuiltInType.Int32 => int.TryParse(text, out var iv) ? (object)iv : 0,
            BuiltInType.UInt32 => uint.TryParse(text, out var uiv) ? (object)uiv : 0u,
            BuiltInType.Int64 => long.TryParse(text, out var lv) ? (object)lv : 0L,
            BuiltInType.UInt64 => ulong.TryParse(text, out var ulv) ? (object)ulv : 0UL,
            BuiltInType.Float => float.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var fv) ? (object)fv : 0f,
            BuiltInType.Double => double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var dv) ? (object)dv : 0d,
            _ => (object)text
        };

        /// <summary>
        /// Extracts a typed value from an XML element, handling complex types
        /// that have child elements per Opc.Ua.Types.xsd rather than plain inner text.
        /// </summary>
        private object ExtractValueFromElement(BuiltInType type, XmlElement el)
        {
            switch (type)
            {
                case BuiltInType.Guid:
                    var guidStr = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == nameof(BuiltInType.String));
                    return guidStr?.InnerText ?? el.InnerText;
                case BuiltInType.NodeId:
                case BuiltInType.ExpandedNodeId:
                    var idEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Identifier");
                    return NsIndexToNsu(idEl?.InnerText ?? el.InnerText);
                case BuiltInType.StatusCode:
                    var codeEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Code");
                    return codeEl?.InnerText ?? el.InnerText;
                case BuiltInType.QualifiedName:
                    var qnNsEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "NamespaceIndex");
                    var qnNameEl = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Name");
                    if (qnNsEl != null && qnNameEl != null)
                    {
                        if (int.TryParse(qnNsEl.InnerText, out var nsIdx) && nsIdx > 0)
                        {
                            var uri = m_context!.NamespaceUris.GetString(nsIdx);
                            if (uri != null) return $"nsu={uri};{qnNameEl.InnerText}";
                        }
                        return $"{qnNsEl.InnerText}:{qnNameEl.InnerText}";
                    }
                    if (qnNameEl != null) return qnNameEl.InnerText;
                    return el.InnerText;
                case BuiltInType.LocalizedText:
                    var ltText = el.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "Text");
                    return ltText?.InnerText ?? el.InnerText;
                default:
                    return ParseTypedValue(type, el.InnerText);
            }
        }

        /// <summary>
        /// Converts a NodeId/ExpandedNodeId from nsu= format to ns= index format.
        /// E.g., "nsu=http://example.org/;i=1234" → "ns=3;i=1234"
        /// Registers unknown namespace URIs in the namespace table.
        /// </summary>
        private string NsuToNsIndex(string nodeId)
        {
            if (!nodeId.StartsWith("nsu=")) return nodeId;
            var semiIdx = nodeId.IndexOf(';');
            if (semiIdx <= 4) return nodeId;
            var uri = nodeId[4..semiIdx];
            var idx = m_context!.NamespaceUris.GetIndexOrAppend(uri);
            return idx == 0 ? nodeId[(semiIdx + 1)..] : $"ns={idx};{nodeId[(semiIdx + 1)..]}";
        }

        /// <summary>
        /// Converts a NodeId/ExpandedNodeId from ns= index format to nsu= URI format.
        /// E.g., "ns=3;i=1234" → "nsu=http://example.org/;i=1234"
        /// </summary>
        private string NsIndexToNsu(string nodeId)
        {
            if (nodeId.StartsWith("ns="))
            {
                var semiIdx = nodeId.IndexOf(';');
                if (semiIdx > 3 && int.TryParse(nodeId[3..semiIdx], out var idx))
                {
                    var uri = m_context!.NamespaceUris.GetString(idx);
                    if (uri != null) return $"nsu={uri};{nodeId[(semiIdx + 1)..]}";
                }
            }
            // No namespace prefix — it's in namespace 0 (UA core), return as-is
            return nodeId;
        }

        /// <summary>Maps a BuiltInType enum value to its XML element name.</summary>
        private static string UaTypeToName(BuiltInType type) => type.ToString();

        /// <summary>Maps an XML element name to a BuiltInType enum value.</summary>
        private static BuiltInType NameToUaType(string name) =>
            Enum.TryParse<BuiltInType>(name, out var type) ? type : BuiltInType.String;

        private string? ToJsonNodeId(string? input)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                if (m_aliases!.TryGetValue(input, out var value))
                {
                    input = value;
                }

                var nid = Opc.Ua.ExpandedNodeId.Parse(m_context, input);
                return nid.Format(m_context, true);
            }
            catch (Exception)
            {
                return "s=" + input;
            }
        }

        private string? ToXmlNodeId(string? input, bool noAlias = false)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                var nid = Opc.Ua.ExpandedNodeId.Parse(m_context, input);
                var text = nid.Format(m_context, false);

                if (!noAlias)
                {
                    var alias = m_aliases!.Where(x => x.Value == text).Select(x => x.Key).FirstOrDefault();

                    if (alias != null)
                    {
                        return alias;
                    }
                }

                return text;

            }
            catch (Exception)
            {
                return "s=" + input;
            }
        }

        private string? ToJsonQualifiedName(string? input)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                var qn = Opc.Ua.QualifiedName.Parse(m_context, input, false);
                return qn.Format(m_context, true);
            }
            catch (Exception)
            {
                return input;
            }
        }

        private string? ToXmlQualifiedName(string? input)
        {
            if (String.IsNullOrEmpty(input))
            {
                return null;
            }

            try
            {
                var qn = Opc.Ua.QualifiedName.Parse(m_context, input, false);
                return qn.Format(m_context, false);
            }
            catch (Exception)
            {
                return input;
            }
        }

        private static Json.LocalizedText? ToJsonLocalizedText(IList<Xml.LocalizedText>? input)
        {
            if (input == null || input.Count == 0)
            {
                return null;
            }

            Json.LocalizedText output = new();
            output.T = new List<List<string>>();

            foreach (var item in input)
            {
                output.T.Add([item.Locale ?? "", item.Value ?? ""]);
            }

            return output;
        }


        private static Xml.LocalizedText[]? ToXmlLocalizedText(Json.LocalizedText? input)
        {
            if (input?.T == null)
            {
                return null;
            }

            List<Xml.LocalizedText> output = new();

            foreach (var item in input.T)
            {
                if (item.Count > 1)
                {
                    output.Add(new Xml.LocalizedText() { Locale = item[0], Value = item[1] });
                }
            }

            return output.ToArray();
        }

        private List<Json.RolePermission>? ToJsonRolePermission(IList<Xml.RolePermission>? input)
        {
            if (input == null || input.Count == 0)
            {
                return null;
            }

            List<Json.RolePermission> output = new();

            foreach (var item in input)
            {
                output.Add(new Json.RolePermission(ToJsonNodeId(item.Value), item.Permissions));
            }

            return output;
        }

        private Xml.RolePermission[]? ToXmlRolePermission(IList<Json.RolePermission>? input)
        {
            if (input == null || input.Count == 0)
            {
                return null;
            }

            List<Xml.RolePermission> output = new();

            foreach (var item in input)
            {
                output.Add(new Xml.RolePermission() { Permissions = (uint)(item.Permissions ?? 0), Value = ToXmlNodeId(item.RoleId) });
            }

            if (output.Count > 0)
            {
                return output.ToArray();
            }

            return null;
        }

        private static Json.ReleaseStatus? ToJsonReleaseStatus(Xml.ReleaseStatus input)
        {
            switch (input)
            {
                case Xml.ReleaseStatus.Draft: return Json.ReleaseStatus.Draft;
                case Xml.ReleaseStatus.Deprecated: return Json.ReleaseStatus.Deprecated;
            }

            return null;
        }

        private static Xml.ReleaseStatus ToXmlReleaseStatus(Json.ReleaseStatus? input)
        {
            if (input != null)
            {
                switch (input)
                {
                    case Json.ReleaseStatus.Draft: return Xml.ReleaseStatus.Draft;
                    case Json.ReleaseStatus.Deprecated: return Xml.ReleaseStatus.Deprecated;
                }
            }

            return Xml.ReleaseStatus.Released;
        }

        private static Json.DataTypePurpose? ToJsonDataTypePurpose(Xml.DataTypePurpose input)
        {
            switch (input)
            {
                case Xml.DataTypePurpose.CodeGenerator: return Json.DataTypePurpose.CodeGenerator;
                case Xml.DataTypePurpose.ServicesOnly: return Json.DataTypePurpose.ServicesOnly;
            }

            return null;
        }

        private static Xml.DataTypePurpose ToXmlDataTypePurpose(Json.DataTypePurpose? input)
        {
            if (input != null)
            {
                switch (input)
                {
                    case Json.DataTypePurpose.CodeGenerator: return Xml.DataTypePurpose.CodeGenerator;
                    case Json.DataTypePurpose.ServicesOnly: return Xml.DataTypePurpose.ServicesOnly;
                }
            }

            return Xml.DataTypePurpose.Normal;
        }

        private Json.DataTypeDefinition? ToJsonDataTypeDefinition(Xml.DataTypeDefinition? input)
        {
            if (input == null)
            {
                return null;
            }

            var output = new Json.DataTypeDefinition();

            // Name is dropped: the BrowseName of the containing DataType is the normative source.
            output.SymbolicName = input.SymbolicName;
            output.IsOptionSet = (input.IsOptionSet) ? input.IsOptionSet : null;
            output.IsUnion = (input.IsUnion) ? input.IsUnion : null;
            output.Fields = new();

            if (input.Field != null)
            {
                foreach (var field in input.Field)
                {
                    output.Fields.Add(new Json.DataTypeField()
                    {
                        Name = field.Name,
                        SymbolicName = field.SymbolicName,
                        DataType = ToJsonNodeId(field.DataType),
                        ValueRank = field.ValueRank != ValueRanks.Scalar ? field.ValueRank : null,
                        ArrayDimensions = !String.IsNullOrEmpty(field.ArrayDimensions) ? field.ArrayDimensions : null,
                        Description = ToJsonLocalizedText(field.Description),
                        DisplayName = ToJsonLocalizedText(field.DisplayName),
                        IsOptional = (field.IsOptional) ? field.IsOptional : null,
                        AllowSubTypes = (field.AllowSubTypes) ? field.AllowSubTypes : null,
                        MaxStringLength = (field.MaxStringLength > 0) ? (int)field.MaxStringLength : null,
                        // -1 is the "unset" sentinel for DataTypeField.Value (see [DefaultValue(-1)] on
                        // the Export model); 0 is a valid enum value and must be preserved.
                        Value = field.Value != -1 ? field.Value : null
                    });
                }
            }

            return output;
        }

        // browseName is the XML QualifiedName of the containing DataType and is the normative source
        // for the Definition Name; the JSON form does not store a Name of its own.
        private Xml.DataTypeDefinition? ToXmlDataTypeDefinition(Json.DataTypeDefinition? input, string? browseName)
        {
            if (input == null)
            {
                return null;
            }

            var output = new Xml.DataTypeDefinition();

            output.Name = browseName;
            output.SymbolicName = input.SymbolicName;
            output.IsOptionSet = input.IsOptionSet ?? false;
            output.IsUnion = input.IsUnion ?? false;

            List<Xml.DataTypeField> fields = new();

            if (input.Fields != null)
            {
                foreach (var field in input.Fields)
                {
                    fields.Add(new Xml.DataTypeField()
                    {
                        Name = field.Name,
                        SymbolicName = field.SymbolicName,
                        DataType = ToXmlNodeId(field.DataType) ?? "i=24",
                        ValueRank = field.ValueRank ?? ValueRanks.Scalar,
                        ArrayDimensions = !String.IsNullOrEmpty(field.ArrayDimensions) ? field.ArrayDimensions : null,
                        Description = ToXmlLocalizedText(field.Description),
                        DisplayName = ToXmlLocalizedText(field.DisplayName),
                        IsOptional = field.IsOptional ?? false,
                        AllowSubTypes = field.AllowSubTypes ?? false,
                        MaxStringLength = (uint)(field.MaxStringLength ?? 0),
                        // null (unset) maps to the Export model's -1 sentinel; an explicit 0 stays 0.
                        Value = field.Value ?? -1
                    });
                }
            }

            output.Field = fields.ToArray();

            return output;
        }

        public void LoadInto(AddressSpace addressSpace)
        {
            addressSpace.AddNodeSet(BuildJson());
            // Post-load: pre-compute DataTypeForm (Structure/Union/Enumeration/OptionSet)
            // for every UADataType so the Variant canonicalizer can recognise unions via
            // dt.DataTypeForm == "Union" even when the source NodeSet did not carry an
            // explicit IsUnion flag on the DataTypeDefinition.
            addressSpace.ComputeDataTypeForms();
            // Then canonicalize every Variant DOM so structure field order matches
            // DataTypeDefinition. Default JSON encoding nodes are NOT stripped automatically
            // — they are unused once Variants are canonical, but removing them would break
            // round-trip comparisons against the source nodeset. Callers that want them gone
            // can invoke AddressSpace.StripDefaultJsonEncodings() explicitly.
            addressSpace.ResolveVariants();
        }

        public static NodeSetSerializer FromAddressSpace(AddressSpace addressSpace, string modelUri)
        {
            var serializer = new NodeSetSerializer();
            serializer.m_addressSpace = addressSpace;
            var nodeSet = addressSpace.GetNodeSet(modelUri);
            serializer.LoadFromNodeSet(nodeSet);
            return serializer;
        }

        private void LoadFromNodeSet(Json.UANodeSet nodeSet)
        {
            m_context = new ServiceMessageContext();
            LoadWellKnownAliases();

            m_models = new();

            if (nodeSet.Models != null)
            {
                foreach (var model in nodeSet.Models)
                {
                    m_context.NamespaceUris.GetIndexOrAppend(model.ModelUri!);

                    if (model.RequiredModels != null)
                    {
                        foreach (var dep in model.RequiredModels)
                            m_context.NamespaceUris.GetIndexOrAppend(dep.ModelUri!);
                    }

                    m_models[model.ModelUri!] = model;
                }
            }

            m_nodes = new();
            m_sequence = new();

            if (nodeSet.Nodes != null)
            {
                void IndexList(IEnumerable<Json.UANode>? nodes)
                {
                    if (nodes == null) return;
                    foreach (var node in nodes)
                    {
                        if (m_nodes.ContainsKey(node.NodeId!)) continue; // already indexed via parent's Children
                        m_nodes[node.NodeId!] = node;
                        m_sequence.Add(node);
                        IndexChildren(node);
                    }
                }

                IndexList(nodeSet.Nodes.ReferenceTypes);
                IndexList(nodeSet.Nodes.DataTypes);
                IndexList(nodeSet.Nodes.VariableTypes);
                IndexList(nodeSet.Nodes.ObjectTypes);
                IndexList(nodeSet.Nodes.Variables);
                IndexList(nodeSet.Nodes.Methods);
                IndexList(nodeSet.Nodes.Objects);
                IndexList(nodeSet.Nodes.Views);
            }

            // Discover and register any namespace URIs referenced by nodes that
            // are not already in the context (e.g. cross-model TypeDefinition refs).
            foreach (var node in m_sequence!)
            {
                RegisterNsuUri(node.NodeId);
                RegisterNsuUri(node.BrowseName);
                RegisterNsuUri(node.ParentId);
                RegisterNsuUri(node.TypeId);
                RegisterNsuUri(node.ModellingRuleId);

                if (node is Json.UAMethod m)
                    RegisterNsuUri(m.MethodDeclarationId);
                if (node is Json.UAVariable v)
                    RegisterNsuUri(v.DataType);
                if (node is Json.UAVariableType vt)
                    RegisterNsuUri(vt.DataType);
                if (node is Json.UADataType dt && dt.Definition?.Fields != null)
                {
                    foreach (var field in dt.Definition.Fields)
                        RegisterNsuUri(field.DataType);
                }

                if (node.References != null)
                {
                    foreach (var r in node.References)
                    {
                        RegisterNsuUri(r.ReferenceTypeId);
                        RegisterNsuUri(r.TargetId);
                    }
                }
            }

            // Ensure any newly discovered namespace URIs are added as RequiredModels
            // so the address space validator accepts them on reload.
            var allRegisteredUris = new HashSet<string>(m_context!.NamespaceUris!.ToArray() ?? []);

            foreach (var model in m_models!.Values)
            {
                var existingReqs = new HashSet<string> { model.ModelUri! };
                if (model.RequiredModels != null)
                    foreach (var req in model.RequiredModels)
                        if (req.ModelUri != null) existingReqs.Add(req.ModelUri);

                foreach (var uri in allRegisteredUris)
                {
                    if (uri == CoreNamespaceUri) continue; // core is always implicit
                    if (existingReqs.Contains(uri)) continue;
                    model.RequiredModels ??= new List<Json.ModelReference>();
                    model.RequiredModels.Add(new Json.ModelReference { ModelUri = uri });
                }
            }
        }

        private void RegisterNsuUri(string? value)
        {
            if (value != null && value.StartsWith("nsu="))
            {
                var semi = value.IndexOf(';');
                if (semi > 4)
                {
                    var uri = value.Substring(4, semi - 4);
                    m_context!.NamespaceUris.GetIndexOrAppend(uri);
                }
            }
        }

    }
}
