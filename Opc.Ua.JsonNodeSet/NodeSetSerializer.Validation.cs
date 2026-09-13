using Xml = Opc.Ua.Export;
using Json = Opc.Ua.JsonNodeSet.Model;
using Opc.Ua.JsonNodeSet;

namespace NodeSetTool
{
    public partial class NodeSetSerializer
    {
        private const string CoreNamespaceUriValue = "http://opcfoundation.org/UA/";

        // ---- Load-time validation ---------------------------------------

        private static void ValidateLoadedXml(Xml.UANodeSet input)
        {
            if (input.NamespaceUris != null)
            {
                for (int i = 0; i < input.NamespaceUris.Length; i++)
                {
                    CanonicalUri.Validate(input.NamespaceUris[i], $"NamespaceUris[{i}]");
                }
            }

            if (input.ServerUris != null)
            {
                for (int i = 0; i < input.ServerUris.Length; i++)
                {
                    CanonicalUri.Validate(input.ServerUris[i], $"ServerUris[{i}]");
                }
            }

            if (input.Models != null)
            {
                for (int i = 0; i < input.Models.Length; i++)
                {
                    ValidateModelTableEntry(input.Models[i], $"Models[{i}]");
                }
            }

            if (input.Aliases != null)
            {
                for (int i = 0; i < input.Aliases.Length; i++)
                {
                    var alias = input.Aliases[i];
                    if (alias?.Value != null)
                    {
                        ValidateNodeIdEmbeddedUris(alias.Value, $"Aliases[{i}].Value");
                    }
                }
            }

            if (input.Items != null)
            {
                for (int i = 0; i < input.Items.Length; i++)
                {
                    ValidateXmlNode(input.Items[i], $"Items[{i}]");
                }
            }
        }

        private static void ValidateModelTableEntry(Xml.ModelTableEntry model, string location)
        {
            if (model == null) return;
            CanonicalUri.Validate(model.ModelUri, $"{location}.ModelUri");
            if (!string.IsNullOrEmpty(model.XmlSchemaUri))
            {
                CanonicalUri.Validate(model.XmlSchemaUri, $"{location}.XmlSchemaUri");
            }
            if (model.RequiredModel != null)
            {
                for (int i = 0; i < model.RequiredModel.Length; i++)
                {
                    var req = model.RequiredModel[i];
                    if (req == null) continue;
                    CanonicalUri.Validate(req.ModelUri, $"{location}.RequiredModel[{i}].ModelUri");
                    if (!string.IsNullOrEmpty(req.XmlSchemaUri))
                    {
                        CanonicalUri.Validate(req.XmlSchemaUri, $"{location}.RequiredModel[{i}].XmlSchemaUri");
                    }
                }
            }
        }

        private static void ValidateXmlNode(Xml.UANode node, string location)
        {
            if (node == null) return;
            ValidateNodeIdEmbeddedUris(node.NodeId, $"{location}.NodeId");
            ValidateNodeIdEmbeddedUris(node.BrowseName, $"{location}.BrowseName");

            if (node is Xml.UAInstance inst)
            {
                ValidateNodeIdEmbeddedUris(inst.ParentNodeId, $"{location}.ParentNodeId");
            }

            if (node is Xml.UAVariable v)
            {
                ValidateNodeIdEmbeddedUris(v.DataType, $"{location}.DataType");
            }
            if (node is Xml.UAVariableType vt)
            {
                ValidateNodeIdEmbeddedUris(vt.DataType, $"{location}.DataType");
            }
            if (node is Xml.UAMethod m)
            {
                ValidateNodeIdEmbeddedUris(m.MethodDeclarationId, $"{location}.MethodDeclarationId");
            }

            if (node.References != null)
            {
                for (int i = 0; i < node.References.Length; i++)
                {
                    var r = node.References[i];
                    if (r == null) continue;
                    ValidateNodeIdEmbeddedUris(r.ReferenceType, $"{location}.References[{i}].ReferenceType");
                    ValidateNodeIdEmbeddedUris(r.Value, $"{location}.References[{i}].Value");
                }
            }
        }

        internal static void ValidateLoadedJson(Json.UANodeSet nodeset)
        {
            if (nodeset.Models != null)
            {
                for (int i = 0; i < nodeset.Models.Count; i++)
                {
                    ValidateModelDefinition(nodeset.Models[i], $"Models[{i}]");
                }
            }

            if (nodeset.Nodes != null)
            {
                ValidateJsonNodeList(nodeset.Nodes.ReferenceTypes, "ReferenceTypes");
                ValidateJsonNodeList(nodeset.Nodes.DataTypes, "DataTypes");
                ValidateJsonNodeList(nodeset.Nodes.VariableTypes, "VariableTypes");
                ValidateJsonNodeList(nodeset.Nodes.ObjectTypes, "ObjectTypes");
                ValidateJsonNodeList(nodeset.Nodes.Variables, "Variables");
                ValidateJsonNodeList(nodeset.Nodes.Methods, "Methods");
                ValidateJsonNodeList(nodeset.Nodes.Objects, "Objects");
                ValidateJsonNodeList(nodeset.Nodes.Views, "Views");
            }
        }

        private static void ValidateJsonNodeList<T>(System.Collections.Generic.List<T>? nodes, string listName) where T : Json.UANode
        {
            if (nodes == null) return;
            for (int i = 0; i < nodes.Count; i++)
            {
                ValidateJsonNode(nodes[i], $"{listName}[{i}]");
            }
        }

        // ---- Save-time validation ---------------------------------------

        internal void ValidateForSave()
        {
            if (m_context != null)
            {
                var nsArr = m_context.NamespaceUris?.ToArray();
                if (nsArr != null)
                {
                    for (int i = 0; i < nsArr.Length; i++)
                    {
                        CanonicalUri.Validate(nsArr[i], $"NamespaceUris[{i}]");
                    }
                }

                var svArr = m_context.ServerUris?.ToArray();
                if (svArr != null)
                {
                    for (int i = 0; i < svArr.Length; i++)
                    {
                        CanonicalUri.Validate(svArr[i], $"ServerUris[{i}]");
                    }
                }
            }

            if (m_models != null)
            {
                int mi = 0;
                foreach (var model in m_models.Values)
                {
                    ValidateModelDefinition(model, $"Models[{mi}]");
                    mi++;
                }
            }

            if (m_aliases != null)
            {
                int ai = 0;
                foreach (var kv in m_aliases)
                {
                    ValidateNodeIdEmbeddedUris(kv.Value, $"Aliases[{ai}].Value");
                    ai++;
                }
            }

            if (m_sequence != null)
            {
                for (int i = 0; i < m_sequence.Count; i++)
                {
                    ValidateJsonNode(m_sequence[i], $"Nodes[{i}]");
                }
            }
        }

        private static void ValidateModelDefinition(Json.ModelDefinition model, string location)
        {
            if (model == null) return;
            CanonicalUri.Validate(model.ModelUri, $"{location}.ModelUri");
            if (!string.IsNullOrEmpty(model.XmlSchemaUri))
            {
                CanonicalUri.Validate(model.XmlSchemaUri, $"{location}.XmlSchemaUri");
            }
            if (model.RequiredModels != null)
            {
                for (int i = 0; i < model.RequiredModels.Count; i++)
                {
                    var req = model.RequiredModels[i];
                    if (req == null) continue;
                    CanonicalUri.Validate(req.ModelUri, $"{location}.RequiredModels[{i}].ModelUri");
                    if (!string.IsNullOrEmpty(req.XmlSchemaUri))
                    {
                        CanonicalUri.Validate(req.XmlSchemaUri, $"{location}.RequiredModels[{i}].XmlSchemaUri");
                    }
                }
            }
        }

        internal static void ValidateJsonNode(Json.UANode node, string location)
        {
            if (node == null) return;
            ValidateNodeIdEmbeddedUris(node.NodeId, $"{location}.NodeId");
            ValidateNodeIdEmbeddedUris(node.BrowseName, $"{location}.BrowseName");
            ValidateNodeIdEmbeddedUris(node.ParentId, $"{location}.ParentId");
            ValidateNodeIdEmbeddedUris(node.TypeId, $"{location}.TypeId");
            ValidateNodeIdEmbeddedUris(node.ModellingRuleId, $"{location}.ModellingRuleId");

            if (node is Json.UAVariable v)
            {
                ValidateNodeIdEmbeddedUris(v.DataType, $"{location}.DataType");
            }
            if (node is Json.UAVariableType vt)
            {
                ValidateNodeIdEmbeddedUris(vt.DataType, $"{location}.DataType");
            }
            if (node is Json.UAMethod m)
            {
                ValidateNodeIdEmbeddedUris(m.MethodDeclarationId, $"{location}.MethodDeclarationId");
            }
            if (node is Json.UADataType dt && dt.Definition?.Fields != null)
            {
                for (int i = 0; i < dt.Definition.Fields.Count; i++)
                {
                    var f = dt.Definition.Fields[i];
                    if (f?.DataType != null)
                    {
                        ValidateNodeIdEmbeddedUris(f.DataType, $"{location}.Definition.Fields[{i}].DataType");
                    }
                }
            }

            if (node.References != null)
            {
                for (int i = 0; i < node.References.Count; i++)
                {
                    var r = node.References[i];
                    if (r == null) continue;
                    ValidateNodeIdEmbeddedUris(r.ReferenceTypeId, $"{location}.References[{i}].ReferenceType");
                    ValidateNodeIdEmbeddedUris(r.TargetId, $"{location}.References[{i}].TargetId");
                }
            }

            if (node.RolePermissions != null)
            {
                for (int i = 0; i < node.RolePermissions.Count; i++)
                {
                    var rp = node.RolePermissions[i];
                    if (rp?.RoleId != null)
                    {
                        ValidateNodeIdEmbeddedUris(rp.RoleId, $"{location}.RolePermissions[{i}].RoleId");
                    }
                }
            }

            if (node.Children != null)
            {
                int ci = 0;
                if (node.Children.Objects != null)
                {
                    foreach (var c in node.Children.Objects)
                    {
                        ValidateJsonNode(c, $"{location}.Children[{ci++}]");
                    }
                }
                if (node.Children.Variables != null)
                {
                    foreach (var c in node.Children.Variables)
                    {
                        ValidateJsonNode(c, $"{location}.Children[{ci++}]");
                    }
                }
                if (node.Children.Methods != null)
                {
                    foreach (var c in node.Children.Methods)
                    {
                        ValidateJsonNode(c, $"{location}.Children[{ci++}]");
                    }
                }
            }
        }

        // ---- Shared: NodeId / BrowseName / ExpandedNodeId URI extraction ----

        /// <summary>
        /// Validates any URI embedded inside a NodeId-shaped string. Handles:
        ///   nsu=&lt;uri&gt;;...
        ///   svu=&lt;uri&gt;;...
        ///   svu=&lt;uri&gt;;nsu=&lt;uri&gt;;...
        /// Strings without an embedded URI (e.g. "i=22", "ns=2;i=5", "MyAlias") are ignored.
        /// </summary>
        private static void ValidateNodeIdEmbeddedUris(string? value, string location)
        {
            if (string.IsNullOrEmpty(value)) return;

            var rest = value;

            if (rest.StartsWith("svu=", StringComparison.Ordinal))
            {
                var semi = rest.IndexOf(';', 4);
                if (semi < 0) return; // malformed — let downstream parser surface it
                var uri = rest.Substring(4, semi - 4);
                CanonicalUri.Validate(uri, $"{location} (svu)");
                rest = rest.Substring(semi + 1);
            }

            if (rest.StartsWith("nsu=", StringComparison.Ordinal))
            {
                var semi = rest.IndexOf(';', 4);
                if (semi < 0) return;
                var uri = rest.Substring(4, semi - 4);
                CanonicalUri.Validate(uri, $"{location} (nsu)");
            }
        }
    }
}
