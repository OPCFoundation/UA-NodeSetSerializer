using Opc.Ua.JsonNodeSet.Model;

namespace Opc.Ua.JsonNodeSet
{
    public class ReferenceEntry
    {
        public string SourceNodeId { get; set; } = null!;
        public string ReferenceTypeId { get; set; } = null!;
        public string TargetNodeId { get; set; } = null!;
        public bool IsForward { get; set; }
    }

    /// <summary>
    /// Represents an instance declaration from a type hierarchy,
    /// wrapping the source UANode with context about which reference and type it came from.
    /// </summary>
    public class InstanceDeclaration : UANode
    {
        /// <summary>The reference type from the type node to this declaration.</summary>
        public string? ReferenceTypeId { get; set; }

        /// <summary>The type node that defined this instance declaration.</summary>
        public string? SourceTypeNodeId { get; set; }

        /// <summary>The original UANode from the address space.</summary>
        public UANode? SourceNode
        {
            get => _sourceNode;
            set
            {
                _sourceNode = value;
                if (value == null) return;
                // Copy relevant fields from source
                NodeId = value.NodeId;
                NodeClass = value.NodeClass;
                BrowseName = value.BrowseName;
                DisplayName = value.DisplayName;
                Description = value.Description;
                ParentId = value.ParentId;
                TypeId = value.TypeId;
                ModellingRuleId = value.ModellingRuleId;
                IsAbstract = value.IsAbstract;
            }
        }
        private UANode? _sourceNode;
    }

    public partial class AddressSpace
    {
        private const string NsuPrefix = "nsu=";
        private const string CoreNamespaceUri = "http://opcfoundation.org/UA/";
        private const string HasSubtypeId = "i=45";

        private readonly Dictionary<string, UANode> _nodes = new();
        private readonly Dictionary<string, List<ReferenceEntry>> _forwardRefs = new();
        private readonly Dictionary<string, List<ReferenceEntry>> _inverseRefs = new();
        private readonly Dictionary<string, ModelDefinition> _models = new();
        private readonly List<UANode> _sequence = new();
        private Dictionary<string, string?>? _supertypeCache;

        public int NodeCount => _nodes.Count;
        public IReadOnlyDictionary<string, ModelDefinition> Models => _models;
        public IReadOnlyList<UANode> Nodes => _sequence;

        public void AddModel(ModelDefinition model)
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model.ModelUri == null)
                throw new ArgumentException("ModelDefinition.ModelUri must not be null.", nameof(model));

            // Stage 1: Validate that all RequiredModels are already registered or are the core namespace
            if (model.RequiredModels != null)
            {
                var missing = new List<string>();
                foreach (var req in model.RequiredModels)
                {
                    if (req.ModelUri == null) continue;
                    if (req.ModelUri == CoreNamespaceUri) continue;
                    if (_models.ContainsKey(req.ModelUri)) continue;
                    missing.Add(req.ModelUri);
                }
                if (missing.Count > 0)
                    throw new InvalidOperationException(
                        $"Model '{model.ModelUri}' requires unknown model(s): {string.Join(", ", missing)}");
            }

            _models[model.ModelUri] = model;
        }

        public void AddNode(UANode node, string? parentNodeId = null)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (node.NodeId == null)
                throw new ArgumentException("UANode.NodeId must not be null.", nameof(node));

            // Validate namespaces before mutating state
            var errors = new List<string>();
            var knownUris = GetKnownNamespaceUris();
            ValidateNode(node, knownUris, errors);
            if (errors.Count > 0)
                throw new InvalidOperationException(
                    $"Unknown namespace(s) referenced: {string.Join("; ", errors)}");

            AddNodeInternal(node, parentNodeId);
        }

        public bool RemoveNode(string nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node))
                return false;

            // Recursively remove children first
            if (node.Children != null)
            {
                RemoveChildren(node.Children);
            }

            _nodes.Remove(nodeId);
            _sequence.Remove(node);
            InvalidateSupertypeCache();

            // Clean up forward references from this node
            if (_forwardRefs.TryGetValue(nodeId, out var fwdList))
            {
                foreach (var entry in fwdList)
                {
                    if (_inverseRefs.TryGetValue(entry.TargetNodeId, out var targetInv))
                    {
                        targetInv.RemoveAll(e => e.TargetNodeId == nodeId && e.ReferenceTypeId == entry.ReferenceTypeId);
                        if (targetInv.Count == 0) _inverseRefs.Remove(entry.TargetNodeId);
                    }
                }
                _forwardRefs.Remove(nodeId);
            }

            // Clean up inverse references from this node
            if (_inverseRefs.TryGetValue(nodeId, out var invList))
            {
                foreach (var entry in invList)
                {
                    if (_forwardRefs.TryGetValue(entry.TargetNodeId, out var targetFwd))
                    {
                        targetFwd.RemoveAll(e => e.TargetNodeId == nodeId && e.ReferenceTypeId == entry.ReferenceTypeId);
                        if (targetFwd.Count == 0) _forwardRefs.Remove(entry.TargetNodeId);
                    }
                }
                _inverseRefs.Remove(nodeId);
            }

            return true;
        }

        public UANode? Read(string nodeId)
        {
            _nodes.TryGetValue(nodeId, out var node);
            return node;
        }

        public List<ReferenceEntry> Browse(string nodeId,
            string? referenceTypeId = null,
            bool includeForward = true,
            bool includeInverse = true)
        {
            var results = new List<ReferenceEntry>();

            if (includeForward && _forwardRefs.TryGetValue(nodeId, out var fwd))
            {
                foreach (var entry in fwd)
                {
                    if (referenceTypeId == null || entry.ReferenceTypeId == referenceTypeId)
                        results.Add(entry);
                }
            }

            if (includeInverse && _inverseRefs.TryGetValue(nodeId, out var inv))
            {
                foreach (var entry in inv)
                {
                    if (referenceTypeId == null || entry.ReferenceTypeId == referenceTypeId)
                        results.Add(entry);
                }
            }

            return results;
        }

        public List<ReferenceEntry> BrowseWithSubtypes(string nodeId,
            string? referenceTypeId = null,
            bool includeForward = true,
            bool includeInverse = true)
        {
            var results = new List<ReferenceEntry>();

            if (includeForward && _forwardRefs.TryGetValue(nodeId, out var fwd))
            {
                foreach (var entry in fwd)
                {
                    if (referenceTypeId == null || IsTypeOf(entry.ReferenceTypeId, referenceTypeId))
                        results.Add(entry);
                }
            }

            if (includeInverse && _inverseRefs.TryGetValue(nodeId, out var inv))
            {
                foreach (var entry in inv)
                {
                    if (referenceTypeId == null || IsTypeOf(entry.ReferenceTypeId, referenceTypeId))
                        results.Add(entry);
                }
            }

            return results;
        }

        public bool IsTypeOf(string nodeId, string targetTypeId)
        {
            ArgumentNullException.ThrowIfNull(nodeId);
            ArgumentNullException.ThrowIfNull(targetTypeId);

            EnsureSupertypeCache();

            var current = nodeId;
            while (current != null)
            {
                if (current == targetTypeId)
                    return true;

                _supertypeCache!.TryGetValue(current, out current);
            }

            return false;
        }

        public UANodeSet GetNodeSet(string modelUri)
        {
            var nodeSet = new UANodeSet();

            if (_models.TryGetValue(modelUri, out var model))
            {
                // Enrich RequiredModels with version/publication info from registered models
                if (model.RequiredModels != null)
                {
                    foreach (var req in model.RequiredModels)
                    {
                        if (req.ModelUri != null && _models.TryGetValue(req.ModelUri, out var registeredModel))
                        {
                            req.VarVersion ??= registeredModel.VarVersion;
                            req.PublicationDate ??= registeredModel.PublicationDate;
                        }
                    }
                }

                nodeSet.Models = new List<ModelDefinition> { model };
            }
            else
            {
                nodeSet.Models = new List<ModelDefinition>();
            }

            var prefix = $"nsu={modelUri};";
            var isCoreNamespace = string.Equals(modelUri, CoreNamespaceUri, StringComparison.OrdinalIgnoreCase);
            var filtered = new List<UANode>();
            var filteredIds = new HashSet<string>();

            foreach (var node in _sequence)
            {
                if (node.NodeId == null) continue;

                // Does this node belong to the model being exported?
                // Core namespace nodes may be stored without an nsu= prefix (e.g. "i=123").
                var belongsToModel = node.NodeId.StartsWith(prefix)
                    || (isCoreNamespace && !node.NodeId.StartsWith(NsuPrefix));
                if (!belongsToModel) continue;

                // Skip nodes that will be emitted nested under an in-model parent.
                // A node whose parent lives in a *different* namespace — e.g. an
                // instance Organized under the core Objects folder (i=85) — has no
                // in-model parent to nest under, so it must be emitted as a
                // top-level node. (Excluding every parented node here is what
                // dropped top-level objects/variables from the export.)
                var parentInModel = node.ParentId != null
                    && (node.ParentId.StartsWith(prefix)
                        || (isCoreNamespace && !node.ParentId.StartsWith(NsuPrefix)));
                if (parentInModel) continue;

                filtered.Add(node);
                filteredIds.Add(node.NodeId);
            }

            nodeSet.Nodes = new UANodeSetNodes();

            foreach (var node in filtered)
            {
                switch (node)
                {
                    case UAReferenceType rt: (nodeSet.Nodes.ReferenceTypes ??= new()).Add(rt); break;
                    case UADataType dt: (nodeSet.Nodes.DataTypes ??= new()).Add(dt); break;
                    case UAVariableType vt: (nodeSet.Nodes.VariableTypes ??= new()).Add(vt); break;
                    case UAObjectType ot: (nodeSet.Nodes.ObjectTypes ??= new()).Add(ot); break;
                    case UAVariable vn: (nodeSet.Nodes.Variables ??= new()).Add(vn); break;
                    case UAMethod mn: (nodeSet.Nodes.Methods ??= new()).Add(mn); break;
                    case UAObject on: (nodeSet.Nodes.Objects ??= new()).Add(on); break;
                    case UAView wn: (nodeSet.Nodes.Views ??= new()).Add(wn); break;
                }
            }

            return nodeSet;
        }

        public void AddNodeSet(UANodeSet nodeSet)
        {
            ArgumentNullException.ThrowIfNull(nodeSet);

            // Stage 1: Validate and register models
            if (nodeSet.Models != null)
            {
                // Build the set of URIs that will be known after this batch:
                // existing models + all models in this nodeset + core namespace
                var batchUris = new HashSet<string> { CoreNamespaceUri };
                foreach (var kvp in _models)
                    batchUris.Add(kvp.Key);
                foreach (var model in nodeSet.Models)
                    if (model?.ModelUri != null)
                        batchUris.Add(model.ModelUri);

                // Strip any RequiredModel URIs that are not in the batch set so that
                // stale cross-references (e.g. a type model that has an instance model URI
                // in its RequiredModels due to an AddReference from the UI) do not block
                // loading. ValidateNode still enforces namespace correctness at the node level.
                foreach (var model in nodeSet.Models)
                {
                    if (model?.RequiredModels == null) continue;
                    model.RequiredModels.RemoveAll(req =>
                        req.ModelUri != null && !batchUris.Contains(req.ModelUri));
                }

                // All valid — register them
                foreach (var model in nodeSet.Models)
                    if (model?.ModelUri != null)
                        _models[model.ModelUri] = model;
            }

            // Validate all nodes before mutating node state
            if (nodeSet.Nodes != null)
            {
                var errors = new List<string>();
                var knownUris = GetKnownNamespaceUris();

                void ValidateList(IEnumerable<UANode>? nodes)
                {
                    if (nodes == null) return;
                    foreach (var node in nodes)
                        ValidateNode(node, knownUris, errors);
                }

                ValidateList(nodeSet.Nodes.ReferenceTypes);
                ValidateList(nodeSet.Nodes.DataTypes);
                ValidateList(nodeSet.Nodes.VariableTypes);
                ValidateList(nodeSet.Nodes.ObjectTypes);
                ValidateList(nodeSet.Nodes.Variables);
                ValidateList(nodeSet.Nodes.Methods);
                ValidateList(nodeSet.Nodes.Objects);
                ValidateList(nodeSet.Nodes.Views);

                if (errors.Count > 0)
                    throw new InvalidOperationException(
                        $"Unknown namespace(s) referenced: {string.Join("; ", errors)}");

                void AddTopLevel(IEnumerable<UANode>? nodes)
                {
                    if (nodes == null) return;
                    foreach (var node in nodes)
                        AddNodeInternal(node, null);
                }

                AddTopLevel(nodeSet.Nodes.ReferenceTypes);
                AddTopLevel(nodeSet.Nodes.DataTypes);
                AddTopLevel(nodeSet.Nodes.VariableTypes);
                AddTopLevel(nodeSet.Nodes.ObjectTypes);
                AddTopLevel(nodeSet.Nodes.Variables);
                AddTopLevel(nodeSet.Nodes.Methods);
                AddTopLevel(nodeSet.Nodes.Objects);
                AddTopLevel(nodeSet.Nodes.Views);
            }
        }

        public List<string> GetModelUris()
        {
            return new List<string>(_models.Keys);
        }

        public bool RemoveModel(string modelUri)
        {
            if (!_models.ContainsKey(modelUri))
                return false;

            // Collect all node IDs belonging to this model
            var prefix = $"nsu={modelUri};";
            var nodeIds = new HashSet<string>(
                _nodes.Keys.Where(id => id.StartsWith(prefix)));

            // Check if any nodes from OTHER models depend on nodes in this model,
            // either via explicit forward references or namespace fields (TypeId, DataType, etc.).
            // Collect the set of dependent namespace URIs.
            var dependentNamespaces = new HashSet<string>();
            foreach (var node in _sequence)
            {
                if (node.NodeId != null && nodeIds.Contains(node.NodeId))
                    continue; // skip the model's own nodes

                var nodeNs = ExtractNsu(node.NodeId ?? "") ?? "";

                // Check explicit forward references targeting nodes in this model
                if (node.References != null)
                {
                    foreach (var r in node.References)
                    {
                        if (r.TargetId == null) continue;
                        bool isForward = r.IsForward ?? true;
                        if (isForward && nodeIds.Contains(r.TargetId))
                            dependentNamespaces.Add(nodeNs);
                    }
                }

                // Check namespace fields (TypeId, DataType, BrowseName, etc.)
                CollectDependentNamespace(node.NodeId, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.BrowseName, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.ParentId, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.TypeId, modelUri, nodeNs, dependentNamespaces);
                CollectDependentNamespace(node.ModellingRuleId, modelUri, nodeNs, dependentNamespaces);

                if (node is UAVariable v)
                    CollectDependentNamespace(v.DataType, modelUri, nodeNs, dependentNamespaces);
                if (node is UAVariableType vt)
                    CollectDependentNamespace(vt.DataType, modelUri, nodeNs, dependentNamespaces);

                if (node.References != null)
                    foreach (var r in node.References)
                        CollectDependentNamespace(r.ReferenceTypeId, modelUri, nodeNs, dependentNamespaces);

                if (node.Children != null)
                    CollectChildrenDependencies(node.Children, modelUri, nodeIds, dependentNamespaces);
            }

            if (dependentNamespaces.Count > 0)
                throw new InvalidOperationException(
                    $"Cannot remove model '{modelUri}'\nIt is used by\n{string.Join("\n", dependentNamespaces)}");

            // Remove all nodes belonging to this model
            foreach (var nodeId in nodeIds)
            {
                RemoveNode(nodeId);
            }

            _models.Remove(modelUri);
            return true;
        }

        /// <summary>
        /// Pre-computes DataTypeForm for all UADataType nodes based on inheritance.
        /// Rules (checked in order):
        ///   1. Inherits from Union → "Union"
        ///   2. Inherits from Structure → "Structure"
        ///   3. Inherits from Enumeration → "Enumeration"
        ///   4. Inherits from UInteger AND is flagged IsOptionSet or has Fields → "OptionSet"
        ///   5. Otherwise → null (not a structured form)
        /// </summary>
        public void ComputeDataTypeForms()
        {
            var structureId = FindWellKnownNode("i=22");
            var enumerationId = FindWellKnownNode("i=29");
            var unionId = FindWellKnownNode("i=12756");
            var uintegerId = FindWellKnownNode("i=28");

            foreach (var node in _sequence)
            {
                if (node is not UADataType dt) continue;
                if (node.NodeId == null) continue;

                string? form = null;

                if (unionId != null && IsTypeOf(node.NodeId, unionId))
                    form = "Union";
                else if (structureId != null && IsTypeOf(node.NodeId, structureId))
                    form = "Structure";
                else if (enumerationId != null && IsTypeOf(node.NodeId, enumerationId))
                    form = "Enumeration";
                else if (uintegerId != null && IsTypeOf(node.NodeId, uintegerId) &&
                         (dt.Definition?.IsOptionSet == true ||
                          (dt.Definition?.Fields != null && dt.Definition.Fields.Count > 0)))
                    // The explicit flag comes first so a freshly created OptionSet — which
                    // has no fields yet — is still recognised as one; without it the form
                    // would only appear once a field existed, and the editor gates adding
                    // fields on the form. Fields alone still count, because plenty of
                    // published nodesets carry the bits without setting IsOptionSet.
                    form = "OptionSet";

                dt.DataTypeForm = form;
            }
        }

        private string? FindWellKnownNode(string shortId)
        {
            var nsuId = $"nsu={CoreNamespaceUri};{shortId}";
            if (_nodes.ContainsKey(nsuId)) return nsuId;
            if (_nodes.ContainsKey(shortId)) return shortId;
            return null;
        }

        #region Namespace Validation

        private HashSet<string> GetKnownNamespaceUris()
        {
            var known = new HashSet<string> { CoreNamespaceUri };

            foreach (var kvp in _models)
            {
                known.Add(kvp.Key);

                if (kvp.Value.RequiredModels != null)
                {
                    foreach (var req in kvp.Value.RequiredModels)
                    {
                        if (req.ModelUri != null)
                            known.Add(req.ModelUri);
                    }
                }
            }

            return known;
        }

        private static void ValidateNode(UANode node, HashSet<string> knownUris, List<string> errors)
        {
            ValidateNsuField(node.NodeId, "NodeId", node, knownUris, errors);
            ValidateNsuField(node.BrowseName, "BrowseName", node, knownUris, errors);
            ValidateNsuField(node.ParentId, "ParentId", node, knownUris, errors);
            ValidateNsuField(node.TypeId, "TypeId", node, knownUris, errors);
            ValidateNsuField(node.ModellingRuleId, "ModellingRuleId", node, knownUris, errors);

            if (node is UAVariable v)
                ValidateNsuField(v.DataType, "DataType", node, knownUris, errors);
            if (node is UAVariableType vt)
                ValidateNsuField(vt.DataType, "DataType", node, knownUris, errors);

            if (node.References != null)
            {
                foreach (var r in node.References)
                {
                    ValidateNsuField(r.ReferenceTypeId, "Reference.ReferenceTypeId", node, knownUris, errors);
                    // Reference.TargetId is intentionally not validated — references to
                    // nodes in unknown/not-yet-loaded namespaces are allowed.
                }
            }

            if (node.Children != null)
            {
                ValidateChildren(node.Children, knownUris, errors);
            }
        }

        private static void ValidateChildren(ChildList children, HashSet<string> knownUris, List<string> errors)
        {
            if (children.Objects != null)
                foreach (var child in children.Objects)
                    ValidateNode(child, knownUris, errors);
            if (children.Variables != null)
                foreach (var child in children.Variables)
                    ValidateNode(child, knownUris, errors);
            if (children.Methods != null)
                foreach (var child in children.Methods)
                    ValidateNode(child, knownUris, errors);
        }

        private static void ValidateNsuField(string? value, string fieldName, UANode node, HashSet<string> knownUris, List<string> errors)
        {
            if (value == null) return;

            var uri = ExtractNsu(value);
            if (uri != null && !knownUris.Contains(uri))
            {
                errors.Add($"Node '{node.NodeId}' {fieldName} references unknown namespace '{uri}'");
            }
        }

        private static string? ExtractNsu(string value)
        {
            if (!value.StartsWith(NsuPrefix, StringComparison.Ordinal))
                return null;

            int semi = value.IndexOf(';', NsuPrefix.Length);
            if (semi < 0)
                return null;

            return value.Substring(NsuPrefix.Length, semi - NsuPrefix.Length);
        }

        private static void CollectDependentNamespace(string? value, string targetUri, string nodeNs, HashSet<string> result)
        {
            if (value == null) return;
            var uri = ExtractNsu(value);
            if (uri != null && uri == targetUri)
                result.Add(nodeNs);
        }

        private static void CollectChildrenDependencies(ChildList children, string modelUri, HashSet<string> nodeIds, HashSet<string> result)
        {
            void Check(UANode node)
            {
                var nodeNs = ExtractNsu(node.NodeId ?? "") ?? "";

                if (node.References != null)
                {
                    foreach (var r in node.References)
                    {
                        if (r.TargetId == null) continue;
                        bool isForward = r.IsForward ?? true;
                        if (isForward && nodeIds.Contains(r.TargetId))
                            result.Add(nodeNs);
                    }
                }

                CollectDependentNamespace(node.NodeId, modelUri, nodeNs, result);
                CollectDependentNamespace(node.BrowseName, modelUri, nodeNs, result);
                CollectDependentNamespace(node.ParentId, modelUri, nodeNs, result);
                CollectDependentNamespace(node.TypeId, modelUri, nodeNs, result);
                CollectDependentNamespace(node.ModellingRuleId, modelUri, nodeNs, result);

                if (node is UAVariable v)
                    CollectDependentNamespace(v.DataType, modelUri, nodeNs, result);
                if (node is UAVariableType vt)
                    CollectDependentNamespace(vt.DataType, modelUri, nodeNs, result);

                if (node.References != null)
                    foreach (var r in node.References)
                        CollectDependentNamespace(r.ReferenceTypeId, modelUri, nodeNs, result);

                if (node.Children != null)
                    CollectChildrenDependencies(node.Children, modelUri, nodeIds, result);
            }

            if (children.Objects != null)
                foreach (var child in children.Objects) Check(child);
            if (children.Variables != null)
                foreach (var child in children.Variables) Check(child);
            if (children.Methods != null)
                foreach (var child in children.Methods) Check(child);
        }


        #endregion

        #region Supertype Cache

        private void InvalidateSupertypeCache()
        {
            _supertypeCache = null;
        }

        private void EnsureSupertypeCache()
        {
            if (_supertypeCache != null) return;

            _supertypeCache = new Dictionary<string, string?>();

            foreach (var nodeId in _nodes.Keys)
            {
                if (_supertypeCache.ContainsKey(nodeId)) continue;

                // The supertype is the target of an inverse HasSubtype reference from this node.
                // In the reference model: node has inverse HasSubtype → target means node is subtype of target.
                string? supertype = null;

                if (_inverseRefs.TryGetValue(nodeId, out var invRefs))
                {
                    foreach (var entry in invRefs)
                    {
                        if (entry.ReferenceTypeId == HasSubtypeId)
                        {
                            supertype = entry.TargetNodeId;
                            break;
                        }
                    }
                }

                _supertypeCache[nodeId] = supertype;
            }
        }

        #endregion

        #region Internal Mutation

        private void AddNodeInternal(UANode node, string? parentNodeId)
        {
            if (node?.NodeId == null)
                throw new ArgumentException("UANode and UANode.NodeId must not be null.");

            if (parentNodeId != null && node.ParentId == null)
            {
                node.ParentId = parentNodeId;
            }

            _nodes[node.NodeId] = node;
            _sequence.Add(node);
            InvalidateSupertypeCache();

            if (node.References != null)
            {
                foreach (var r in node.References)
                {
                    if (r.TargetId == null || r.ReferenceTypeId == null) continue;

                    bool isForward = r.IsForward ?? true;

                    var entry = new ReferenceEntry
                    {
                        SourceNodeId = node.NodeId,
                        ReferenceTypeId = r.ReferenceTypeId,
                        TargetNodeId = r.TargetId,
                        IsForward = isForward
                    };

                    if (isForward)
                    {
                        AddIfNotDuplicate(_forwardRefs, node.NodeId, entry);
                        var inverse = new ReferenceEntry
                        {
                            SourceNodeId = r.TargetId,
                            ReferenceTypeId = r.ReferenceTypeId,
                            TargetNodeId = node.NodeId,
                            IsForward = false
                        };
                        AddIfNotDuplicate(_inverseRefs, r.TargetId, inverse);
                    }
                    else
                    {
                        AddIfNotDuplicate(_inverseRefs, node.NodeId, entry);
                        var forward = new ReferenceEntry
                        {
                            SourceNodeId = r.TargetId,
                            ReferenceTypeId = r.ReferenceTypeId,
                            TargetNodeId = node.NodeId,
                            IsForward = true
                        };
                        AddIfNotDuplicate(_forwardRefs, r.TargetId, forward);
                    }
                }
            }

            if (node.Children != null)
            {
                AddChildren(node.Children, node.NodeId);
            }
        }

        private void AddChildren(ChildList children, string parentNodeId)
        {
            if (children.Variables != null)
            {
                foreach (var child in children.Variables)
                    AddNodeInternal(child, parentNodeId);
            }
            if (children.Objects != null)
            {
                foreach (var child in children.Objects)
                    AddNodeInternal(child, parentNodeId);
            }
            if (children.Methods != null)
            {
                foreach (var child in children.Methods)
                    AddNodeInternal(child, parentNodeId);
            }
        }

        public void AddReference(string sourceNodeId, string referenceTypeId, string targetNodeId, bool isForward)
        {
            if (!_nodes.TryGetValue(sourceNodeId, out var sourceNode))
                throw new KeyNotFoundException($"Source node '{sourceNodeId}' not found.");

            // A design-tool-only node is a standalone marker that is invisible in
            // the address space: no references may originate from it.
            if (sourceNode.DesignToolOnly == true)
                throw new InvalidOperationException(
                    $"Node '{sourceNodeId}' is design-tool-only; references are not allowed.");

            // Add to the UANode.References list (for serialization)
            sourceNode.References ??= new List<Reference>();
            sourceNode.References.Add(new Reference
            {
                ReferenceTypeId = referenceTypeId,
                TargetId = targetNodeId,
                IsForward = isForward,
            });

            // Index in forward/inverse dictionaries
            var entry = new ReferenceEntry
            {
                SourceNodeId = sourceNodeId,
                ReferenceTypeId = referenceTypeId,
                TargetNodeId = targetNodeId,
                IsForward = isForward,
            };

            if (isForward)
            {
                AddIfNotDuplicate(_forwardRefs, sourceNodeId, entry);
                AddIfNotDuplicate(_inverseRefs, targetNodeId, new ReferenceEntry
                {
                    SourceNodeId = targetNodeId,
                    ReferenceTypeId = referenceTypeId,
                    TargetNodeId = sourceNodeId,
                    IsForward = false,
                });
            }
            else
            {
                AddIfNotDuplicate(_inverseRefs, sourceNodeId, entry);
                AddIfNotDuplicate(_forwardRefs, targetNodeId, new ReferenceEntry
                {
                    SourceNodeId = targetNodeId,
                    ReferenceTypeId = referenceTypeId,
                    TargetNodeId = sourceNodeId,
                    IsForward = true,
                });
            }

            InvalidateSupertypeCache();
        }

        public bool RemoveReference(string sourceNodeId, string referenceTypeId, string targetNodeId, bool isForward)
        {
            // A reference can be authored on either endpoint: the source's
            // References with IsForward=isForward, or the target's References
            // with IsForward=!isForward. CreateChildNode for instance authors
            // the parent→child HasComponent on the child as an inverse, so
            // deleting from the parent's view must also scrub the target's
            // list — otherwise serialization or the DB still holds it.
            var hasSource = _nodes.TryGetValue(sourceNodeId, out var sourceNode);
            var hasTarget = _nodes.TryGetValue(targetNodeId, out var targetNode);
            if (!hasSource && !hasTarget)
                return false;

            sourceNode?.References?.RemoveAll(r =>
                r.ReferenceTypeId == referenceTypeId &&
                r.TargetId == targetNodeId &&
                (r.IsForward ?? true) == isForward);

            targetNode?.References?.RemoveAll(r =>
                r.ReferenceTypeId == referenceTypeId &&
                r.TargetId == sourceNodeId &&
                (r.IsForward ?? true) == !isForward);

            // Remove from index
            if (isForward)
            {
                RemoveFromRefDict(_forwardRefs, sourceNodeId, referenceTypeId, targetNodeId, true);
                RemoveFromRefDict(_inverseRefs, targetNodeId, referenceTypeId, sourceNodeId, false);
            }
            else
            {
                RemoveFromRefDict(_inverseRefs, sourceNodeId, referenceTypeId, targetNodeId, false);
                RemoveFromRefDict(_forwardRefs, targetNodeId, referenceTypeId, sourceNodeId, true);
            }

            InvalidateSupertypeCache();
            return true;
        }

        private static void RemoveFromRefDict(Dictionary<string, List<ReferenceEntry>> dict, string key,
            string referenceTypeId, string targetNodeId, bool isForward)
        {
            if (!dict.TryGetValue(key, out var list)) return;
            list.RemoveAll(e =>
                e.ReferenceTypeId == referenceTypeId &&
                e.TargetNodeId == targetNodeId &&
                e.IsForward == isForward);
            if (list.Count == 0) dict.Remove(key);
        }

        #endregion

        #region Instantiation

        /// <summary>
        /// Instantiate a type under a parent node. Creates the instance with all mandatory
        /// children from the type hierarchy. NodeIds are allocated using the provided function.
        /// Returns the list of all created nodes (root instance + mandatory children, recursive).
        /// </summary>
        /// <param name="typeNodeId">The ObjectType or VariableType to instantiate.</param>
        /// <param name="parentNodeId">The parent node under which the instance is created.</param>
        /// <param name="modelUri">The namespace URI for the new instance.</param>
        /// <param name="browseName">BrowseName for the root instance (without namespace prefix).</param>
        /// <param name="displayName">DisplayName for the root instance.</param>
        /// <param name="allocateNodeId">Function that returns the next available numeric NodeId for the given namespace.</param>
        /// <param name="referenceTypeId">Reference from parent to instance (default: HasComponent i=47).</param>
        /// <param name="modellingRuleId">ModellingRule for the instance (default: Mandatory i=78). Null for top-level instances.</param>
        public List<UANode> Instantiate(
            string typeNodeId,
            string parentNodeId,
            string modelUri,
            string browseName,
            string? displayName,
            Func<string, string> allocateNodeId,
            string referenceTypeId = "i=47",
            string? modellingRuleId = null)
        {
            var typeNode = Read(typeNodeId)
                ?? throw new ArgumentException($"Type node '{typeNodeId}' not found.");

            if (typeNode.NodeClass != NodeClass.UAObjectType && typeNode.NodeClass != NodeClass.UAVariableType)
                throw new ArgumentException($"Node '{typeNodeId}' is not an ObjectType or VariableType.");

            var createdNodes = new List<UANode>();

            // Create the root instance
            var instanceId = $"nsu={modelUri};i={allocateNodeId(modelUri)}";
            var qualifiedBrowseName = $"nsu={modelUri};{browseName}";
            var display = displayName ?? browseName;

            UANode instance;
            if (typeNode.NodeClass == NodeClass.UAObjectType)
            {
                instance = new UAObject
                {
                    NodeId = instanceId,
                    NodeClass = NodeClass.UAObject,
                    BrowseName = qualifiedBrowseName,
                    DisplayName = MakeLocalizedText(display),
                    ParentId = parentNodeId,
                    TypeId = typeNodeId,
                    ModellingRuleId = modellingRuleId,
                    References = new List<Reference>
                    {
                        new() { ReferenceTypeId = referenceTypeId, TargetId = parentNodeId, IsForward = false },
                        new() { ReferenceTypeId = "i=40", TargetId = typeNodeId, IsForward = true },
                    }
                };
            }
            else
            {
                var vtNode = typeNode as UAVariableType;
                instance = new UAVariable
                {
                    NodeId = instanceId,
                    NodeClass = NodeClass.UAVariable,
                    BrowseName = qualifiedBrowseName,
                    DisplayName = MakeLocalizedText(display),
                    ParentId = parentNodeId,
                    TypeId = typeNodeId,
                    ModellingRuleId = modellingRuleId,
                    DataType = vtNode?.DataType,
                    ValueRank = vtNode?.ValueRank,
                    ArrayDimensions = vtNode?.ArrayDimensions,
                    Value = ResolveDefaultValue(parentNodeId, qualifiedBrowseName, typeNodeId),
                    References = new List<Reference>
                    {
                        new() { ReferenceTypeId = referenceTypeId, TargetId = parentNodeId, IsForward = false },
                        new() { ReferenceTypeId = "i=40", TargetId = typeNodeId, IsForward = true },
                    }
                };
            }

            if (modellingRuleId != null)
            {
                instance.References!.Add(new Reference
                {
                    ReferenceTypeId = "i=37", // HasModellingRule
                    TargetId = modellingRuleId,
                    IsForward = true,
                });
            }

            AddNode(instance, parentNodeId);
            createdNodes.Add(instance);

            // Instantiate mandatory children from the type hierarchy.
            // If modellingRuleId is set, the root is an instance declaration — children keep their ModellingRules.
            // If null, the root is a standalone instance — children should NOT have ModellingRules.
            bool isInstanceDeclaration = modellingRuleId != null;
            InstantiateMandatoryChildren(typeNodeId, instanceId, modelUri, allocateNodeId, createdNodes, isInstanceDeclaration);

            return createdNodes;
        }

        /// <summary>
        /// Adds the mandatory children of <paramref name="typeNodeId"/> directly under
        /// <paramref name="parentNodeId"/>, without creating an extra wrapper node.
        /// Use this after a caller has already created the instance node itself
        /// (e.g. from CreateChildNode) and just needs the type's mandatory children
        /// populated underneath it.
        ///
        /// Whether the new children keep their ModellingRules is derived from the
        /// parent's context: if the parent lives inside an ObjectType/VariableType
        /// tree (or is itself an instance declaration), the children are instance
        /// declarations and keep their ModellingRules; otherwise they are standalone
        /// instance children and their ModellingRules are stripped.
        /// </summary>
        /// <param name="includeOptionalTopLevel">
        /// When true, the type's direct (top-level) Optional declarations are also
        /// instantiated, not just Mandatory ones. Recursion into descendants stays
        /// mandatory-only. Used by Add Interface, which materializes every member of
        /// the interface; the default (false) preserves plain instantiation.
        /// </param>
        public List<UANode> ExpandMandatoryChildren(
            string parentNodeId,
            string typeNodeId,
            string modelUri,
            Func<string, string> allocateNodeId,
            bool includeOptionalTopLevel = false)
        {
            var typeNode = Read(typeNodeId)
                ?? throw new ArgumentException($"Type node '{typeNodeId}' not found.");
            if (typeNode.NodeClass != NodeClass.UAObjectType && typeNode.NodeClass != NodeClass.UAVariableType)
                throw new ArgumentException($"Node '{typeNodeId}' is not an ObjectType or VariableType.");

            var parentNode = Read(parentNodeId)
                ?? throw new ArgumentException($"Parent node '{parentNodeId}' not found.");

            // A design-tool-only node takes no children: instantiation rules are
            // skipped entirely, so no mandatory children are materialized.
            if (parentNode.DesignToolOnly == true)
                return new List<UANode>();

            var createdNodes = new List<UANode>();
            InstantiateMandatoryChildren(
                typeNodeId, parentNodeId, modelUri, allocateNodeId, createdNodes,
                isInstanceDeclaration: IsInsideTypeTree(parentNode),
                includeOptional: includeOptionalTopLevel);
            return createdNodes;
        }

        /// <summary>
        /// Adds the mandatory descendants of an instance declaration under an already-created
        /// instance of that declaration, recursively.
        ///
        /// <see cref="ExpandMandatoryChildren"/> sees only a TypeDefinition, so it misses the
        /// children the owning type authored directly under the declaration — including the
        /// ones a subtype inherits when it overrides that declaration. This overload follows
        /// the declaration node itself, so those descendants are materialized too.
        /// </summary>
        public List<UANode> ExpandMandatoryChildrenFromDeclaration(
            string parentNodeId,
            string declarationNodeId,
            string modelUri,
            Func<string, string> allocateNodeId)
        {
            if (Read(declarationNodeId) == null)
                throw new ArgumentException($"Declaration node '{declarationNodeId}' not found.");

            var parentNode = Read(parentNodeId)
                ?? throw new ArgumentException($"Parent node '{parentNodeId}' not found.");

            // A design-tool-only node takes no children: instantiation rules are
            // skipped entirely, so no mandatory children are materialized.
            if (parentNode.DesignToolOnly == true)
                return new List<UANode>();

            var createdNodes = new List<UANode>();
            InstantiateMandatoryChildrenFromSource(
                declarationNodeId, parentNodeId, modelUri, allocateNodeId, createdNodes,
                isInstanceDeclaration: IsInsideTypeTree(parentNode));
            return createdNodes;
        }

        /// <summary>
        /// Walks up the parent chain and returns true if the node, or any
        /// ancestor, is an ObjectType or VariableType — i.e. the node lives
        /// inside a type tree and its descendants are instance declarations.
        ///
        /// The check intentionally does NOT short-circuit on a non-empty
        /// ModellingRuleId: a stale rule on a regular instance (e.g. left
        /// over from a prior bug) would otherwise misclassify its descendants
        /// as instance declarations.
        /// </summary>
        public bool IsInsideTypeTree(UANode? node)
        {
            var visited = new HashSet<string>();
            while (node != null && node.NodeId != null && visited.Add(node.NodeId))
            {
                if (node.NodeClass == NodeClass.UAObjectType || node.NodeClass == NodeClass.UAVariableType)
                    return true;
                if (string.IsNullOrEmpty(node.ParentId)) return false;
                node = Read(node.ParentId);
            }
            return false;
        }

        /// <summary>
        /// Collects instance declarations from the type hierarchy and creates mandatory children.
        /// When isInstanceDeclaration is true, children keep their ModellingRules.
        /// When false (standalone instance), ModellingRules are stripped from children.
        /// </summary>
        private void InstantiateMandatoryChildren(
            string typeNodeId,
            string parentInstanceId,
            string modelUri,
            Func<string, string> allocateNodeId,
            List<UANode> createdNodes,
            bool isInstanceDeclaration,
            bool includeOptional = false)
        {
            var declarations = GetInstanceDeclarations(typeNodeId);
            // Only true Mandatory (i=78) children are materialized. Placeholders
            // (MandatoryPlaceholder i=11510, OptionalPlaceholder i=11508) are
            // cardinality patterns/templates, never concrete instance children, so
            // they are NOT instantiated — including when nested under a mandatory
            // child during recursion.
            var selected = declarations.Where(d =>
                d.ModellingRuleId == "i=78" ||                       // Mandatory
                (includeOptional && d.ModellingRuleId == "i=80")     // Optional (Add Interface)
            ).ToList();

            foreach (var decl in selected)
            {
                var bn = StripNsuPrefix(decl.BrowseName ?? "");
                var display = GetNodeDisplayText(decl) ?? bn;

                var childId = $"nsu={modelUri};i={allocateNodeId(modelUri)}";
                // Preserve the original BrowseName namespace from the declaration
                var qualBn = decl.BrowseName ?? bn;
                var refTypeId = decl.ReferenceTypeId ?? "i=47";

                // Only keep ModellingRule for instance declarations, not standalone instances
                var childModellingRuleId = isInstanceDeclaration ? decl.ModellingRuleId : null;

                UANode child;
                if (decl.NodeClass == NodeClass.UAVariable || decl.NodeClass == NodeClass.UAVariableType)
                {
                    // Extract Value, DataType, ValueRank, ArrayDimensions from source
                    // (source may be UAVariable or UAVariableType)
                    var srcVar = decl.SourceNode as UAVariable;
                    var srcVarType = decl.SourceNode as UAVariableType;
                    child = new UAVariable
                    {
                        NodeId = childId,
                        NodeClass = NodeClass.UAVariable,
                        BrowseName = qualBn,
                        DisplayName = MakeLocalizedText(display),
                        ParentId = parentInstanceId,
                        TypeId = decl.TypeId,
                        ModellingRuleId = childModellingRuleId,
                        // The declaration that won the merge need not be the one carrying the
                        // default value: an override re-declares the child without it.
                        Value = srcVar?.Value ?? srcVarType?.Value
                            ?? ResolveDefaultValue(parentInstanceId, qualBn, decl.TypeId),
                        DataType = srcVar?.DataType ?? srcVarType?.DataType,
                        ValueRank = srcVar?.ValueRank ?? srcVarType?.ValueRank,
                        ArrayDimensions = srcVar?.ArrayDimensions ?? srcVarType?.ArrayDimensions,
                        References = new List<Reference>
                        {
                            new() { ReferenceTypeId = refTypeId, TargetId = parentInstanceId, IsForward = false },
                        }
                    };
                    if (decl.TypeId != null)
                        child.References.Add(new Reference { ReferenceTypeId = "i=40", TargetId = decl.TypeId, IsForward = true });
                }
                else if (decl.NodeClass == NodeClass.UAMethod)
                {
                    child = new UAMethod
                    {
                        NodeId = childId,
                        NodeClass = NodeClass.UAMethod,
                        BrowseName = qualBn,
                        DisplayName = MakeLocalizedText(display),
                        ParentId = parentInstanceId,
                        ModellingRuleId = childModellingRuleId,
                        References = new List<Reference>
                        {
                            new() { ReferenceTypeId = refTypeId, TargetId = parentInstanceId, IsForward = false },
                        }
                    };
                }
                else
                {
                    child = new UAObject
                    {
                        NodeId = childId,
                        NodeClass = NodeClass.UAObject,
                        BrowseName = qualBn,
                        DisplayName = MakeLocalizedText(display),
                        ParentId = parentInstanceId,
                        TypeId = decl.TypeId,
                        ModellingRuleId = childModellingRuleId,
                        References = new List<Reference>
                        {
                            new() { ReferenceTypeId = refTypeId, TargetId = parentInstanceId, IsForward = false },
                        }
                    };
                    if (decl.TypeId != null)
                        child.References.Add(new Reference { ReferenceTypeId = "i=40", TargetId = decl.TypeId, IsForward = true });
                }

                if (childModellingRuleId != null)
                {
                    child.References!.Add(new Reference
                    {
                        ReferenceTypeId = "i=37",
                        TargetId = childModellingRuleId,
                        IsForward = true,
                    });
                }

                AddNode(child, parentInstanceId);
                createdNodes.Add(child);

                // Recurse for children: use the source node's NodeId first (it may have
                // overridden children), then the TypeDefinition fills in remaining defaults.
                // GetInstanceDeclarations walks the type chain: source node → TypeDef → supertypes,
                // so the source node's overrides win via first-seen-wins.
                // Methods are included: their InputArguments/OutputArguments are mandatory
                // property children authored directly on the declaration (Methods are
                // untyped, so decl.TypeId is null and recursion is purely source-driven).
                if (decl.NodeClass == NodeClass.UAObject || decl.NodeClass == NodeClass.UAVariable
                    || decl.NodeClass == NodeClass.UAMethod)
                {
                    var sourceNodeId = decl.SourceNode?.NodeId;
                    if (sourceNodeId != null)
                    {
                        // Get children from the source node itself (may have overrides)
                        // plus any inherited from its TypeDefinition
                        InstantiateMandatoryChildrenFromSource(
                            sourceNodeId, childId, modelUri,
                            allocateNodeId, createdNodes, isInstanceDeclaration);
                    }
                    else if (decl.TypeId != null)
                    {
                        InstantiateMandatoryChildren(decl.TypeId, childId, modelUri, allocateNodeId, createdNodes, isInstanceDeclaration);
                    }
                }
            }
        }

        /// <summary>
        /// Collects the effective declarations of a source node — its TypeDefinition's
        /// defaults merged with everything authored on the node and on the supertypes'
        /// copies of it — and instantiates the mandatory ones.
        /// </summary>
        private void InstantiateMandatoryChildrenFromSource(
            string sourceNodeId,
            string parentInstanceId,
            string modelUri,
            Func<string, string> allocateNodeId,
            List<UANode> createdNodes,
            bool isInstanceDeclaration)
        {
            // Filter to mandatory and instantiate. Placeholders (MandatoryPlaceholder
            // i=11510, OptionalPlaceholder i=11508) are templates, not concrete
            // children, so they are excluded here too.
            var mandatory = GetEffectiveInstanceDeclarations(sourceNodeId)
                .Where(d => d.ModellingRuleId == "i=78").ToList();

            // Reuse the same instantiation logic
            foreach (var decl in mandatory)
            {
                var bn = StripNsuPrefix(decl.BrowseName ?? "");
                var display = GetNodeDisplayText(decl) ?? bn;
                var childId = $"nsu={modelUri};i={allocateNodeId(modelUri)}";
                var qualBn = decl.BrowseName ?? bn;
                var refTypeId = decl.ReferenceTypeId ?? "i=47";
                var childModellingRuleId = isInstanceDeclaration ? decl.ModellingRuleId : null;

                UANode child;
                if (decl.NodeClass == NodeClass.UAVariable || decl.NodeClass == NodeClass.UAVariableType)
                {
                    var srcVar = decl.SourceNode as UAVariable;
                    var srcVarType = decl.SourceNode as UAVariableType;
                    child = new UAVariable
                    {
                        NodeId = childId,
                        NodeClass = NodeClass.UAVariable,
                        BrowseName = qualBn,
                        DisplayName = MakeLocalizedText(display),
                        ParentId = parentInstanceId,
                        TypeId = decl.TypeId,
                        ModellingRuleId = childModellingRuleId,
                        // The declaration that won the merge need not be the one carrying the
                        // default value: an override re-declares the child without it.
                        Value = srcVar?.Value ?? srcVarType?.Value
                            ?? ResolveDefaultValue(parentInstanceId, qualBn, decl.TypeId),
                        DataType = srcVar?.DataType ?? srcVarType?.DataType,
                        ValueRank = srcVar?.ValueRank ?? srcVarType?.ValueRank,
                        ArrayDimensions = srcVar?.ArrayDimensions ?? srcVarType?.ArrayDimensions,
                        References = new List<Reference>
                        {
                            new() { ReferenceTypeId = refTypeId, TargetId = parentInstanceId, IsForward = false },
                        }
                    };
                    if (decl.TypeId != null)
                        child.References.Add(new Reference { ReferenceTypeId = "i=40", TargetId = decl.TypeId, IsForward = true });
                }
                else if (decl.NodeClass == NodeClass.UAMethod)
                {
                    child = new UAMethod
                    {
                        NodeId = childId,
                        NodeClass = NodeClass.UAMethod,
                        BrowseName = qualBn,
                        DisplayName = MakeLocalizedText(display),
                        ParentId = parentInstanceId,
                        ModellingRuleId = childModellingRuleId,
                        References = new List<Reference>
                        {
                            new() { ReferenceTypeId = refTypeId, TargetId = parentInstanceId, IsForward = false },
                        }
                    };
                }
                else
                {
                    child = new UAObject
                    {
                        NodeId = childId,
                        NodeClass = NodeClass.UAObject,
                        BrowseName = qualBn,
                        DisplayName = MakeLocalizedText(display),
                        ParentId = parentInstanceId,
                        TypeId = decl.TypeId,
                        ModellingRuleId = childModellingRuleId,
                        References = new List<Reference>
                        {
                            new() { ReferenceTypeId = refTypeId, TargetId = parentInstanceId, IsForward = false },
                        }
                    };
                    if (decl.TypeId != null)
                        child.References.Add(new Reference { ReferenceTypeId = "i=40", TargetId = decl.TypeId, IsForward = true });
                }

                if (childModellingRuleId != null)
                {
                    child.References!.Add(new Reference
                    {
                        ReferenceTypeId = "i=37",
                        TargetId = childModellingRuleId,
                        IsForward = true,
                    });
                }

                AddNode(child, parentInstanceId);
                createdNodes.Add(child);

                // Recurse. Methods are included so their InputArguments/OutputArguments
                // (mandatory property children authored on the declaration) materialize;
                // Methods are untyped, so decl.TypeId is null and recursion is source-driven.
                if (decl.NodeClass == NodeClass.UAObject || decl.NodeClass == NodeClass.UAVariable
                    || decl.NodeClass == NodeClass.UAMethod)
                {
                    var srcNodeId = decl.SourceNode?.NodeId;
                    if (srcNodeId != null)
                    {
                        InstantiateMandatoryChildrenFromSource(
                            srcNodeId, childId, modelUri,
                            allocateNodeId, createdNodes, isInstanceDeclaration);
                    }
                    else if (decl.TypeId != null)
                    {
                        InstantiateMandatoryChildren(decl.TypeId, childId, modelUri, allocateNodeId, createdNodes, isInstanceDeclaration);
                    }
                }
            }
        }

        /// <summary>
        /// Collects instance declarations from the type hierarchy.
        /// Walks from the given type up through its supertypes.
        /// Subtype declarations take priority (first seen wins by stripped BrowseName).
        /// Only returns children with a ModellingRule set.
        /// </summary>
        public List<InstanceDeclaration> GetInstanceDeclarations(string typeNodeId)
        {
            // Walk UP the supertype chain
            var typeChain = new List<string> { typeNodeId };
            var current = typeNodeId;
            var visited = new HashSet<string> { current };
            while (true)
            {
                var parentRefs = Browse(current, "i=45", includeForward: false, includeInverse: true);
                if (parentRefs.Count == 0) break;
                current = parentRefs[0].TargetNodeId;
                if (!visited.Add(current)) break;
                typeChain.Add(current);
            }

            // Collect children; subtype children take priority (first seen by stripped BrowseName)
            var childMap = new Dictionary<string, InstanceDeclaration>(StringComparer.OrdinalIgnoreCase);

            foreach (var typeId in typeChain)
            {
                var refs = BrowseWithSubtypes(typeId, "i=33", includeForward: true, includeInverse: false);
                foreach (var r in refs)
                {
                    // Skip HasSubtype references
                    if (r.ReferenceTypeId == "i=45" || IsTypeOf(r.ReferenceTypeId, "i=45"))
                        continue;

                    var childNode = Read(r.TargetNodeId);
                    if (childNode == null) continue;
                    if (string.IsNullOrEmpty(childNode.ModellingRuleId)) continue;

                    var key = StripNsuPrefix(childNode.BrowseName ?? "").ToLowerInvariant();
                    if (childMap.ContainsKey(key)) continue;

                    childMap[key] = new InstanceDeclaration
                    {
                        SourceNode = childNode,
                        ReferenceTypeId = r.ReferenceTypeId,
                        SourceTypeNodeId = typeId,
                    };
                }
            }

            return childMap.Values.ToList();
        }

        /// <summary>
        /// Collects the effective instance declarations for <paramref name="nodeId"/> — a node
        /// that is itself an instance declaration, or an instance of one.
        ///
        /// A child's declarations do not all come from its TypeDefinition: a type may author
        /// extra children directly under one of its own instance declarations. When a subtype
        /// overrides such an inherited child it re-declares only the child node itself, so the
        /// grandchildren the supertype authored under its copy stay on the supertype. Taking
        /// the TypeDefinition alone (or the override node alone) therefore loses them.
        ///
        /// The effective set is the TypeDefinition's declarations, overlaid with the children
        /// authored on the matching declaration in every type that contributes one, least
        /// specific first — so the most specific authoring wins by stripped BrowseName.
        /// </summary>
        public List<InstanceDeclaration> GetEffectiveInstanceDeclarations(string nodeId)
        {
            var node = Read(nodeId);
            if (node == null) return new List<InstanceDeclaration>();

            var map = new Dictionary<string, InstanceDeclaration>(StringComparer.OrdinalIgnoreCase);

            // Base: what the node's own TypeDefinition declares. That walk already covers
            // the TypeDefinition's supertypes.
            if (!string.IsNullOrEmpty(node.TypeId))
            {
                foreach (var decl in GetInstanceDeclarations(node.TypeId!))
                    map[DeclarationKey(decl.BrowseName)] = decl;
            }

            // Then everything authored at this node's BrowseName path, from the least
            // specific anchor to the most specific.
            foreach (var (typeChain, path) in CollectDeclarationAnchors(node))
            {
                foreach (var typeId in typeChain)
                {
                    var declarationNode = ResolveBrowsePath(typeId, path);
                    if (declarationNode != null)
                        OverlayAuthoredChildren(map, declarationNode);
                }
            }

            // The node's own children are the most specific authoring of all. (Redundant
            // when the node lives in a type tree — the last anchor resolves back to it —
            // but it is what carries the declarations of a standalone instance.)
            OverlayAuthoredChildren(map, node);

            return map.Values.ToList();
        }

        /// <summary>
        /// Resolves the default Value a child named <paramref name="browseName"/> starts with
        /// when it is instantiated under <paramref name="parentNodeId"/>.
        ///
        /// The value of a declaration deep inside a type tree can be authored in more than one
        /// place, so the search runs most specific first and takes the first Value it finds:
        ///   1. the same BrowseName path resolved from the root type, then from each of its
        ///      supertypes in turn — a subtype that overrides an inherited child re-declares
        ///      only the child itself, leaving the value behind on the supertype's copy, which
        ///      is what makes this walk necessary;
        ///   2. the same again for each enclosing anchor working inwards;
        ///   3. what the parent's own TypeDefinition (and its supertypes) declares;
        ///   4. failing all of those, the Value the child's own TypeDefinition
        ///      (<paramref name="typeDefinitionId"/>) carries, following that type's supertypes.
        ///
        /// Example: SimPumpType derives from PumpType and overrides
        /// Operational/Measurements/DifferentialPressure. Adding the optional EngineeringUnits
        /// property under that override takes its value from PumpType's
        /// Operational/Measurements/DifferentialPressure/EngineeringUnits.
        /// </summary>
        /// <returns>The default Value, or null when nothing in the hierarchy declares one.</returns>
        public Variant? ResolveDefaultValue(string parentNodeId, string browseName, string? typeDefinitionId)
        {
            var key = DeclarationKey(browseName);
            var parent = Read(parentNodeId);

            if (parent != null)
            {
                var anchors = new List<(List<string> TypeChain, List<string> Path)>();

                if (parent.NodeClass == NodeClass.UAObjectType || parent.NodeClass == NodeClass.UAVariableType)
                {
                    // A child added straight onto a type is declared by that type and its supertypes.
                    if (parent.NodeId != null)
                        anchors.Add((GetTypeChain(parent.NodeId), new List<string>()));
                }
                else
                {
                    // CollectDeclarationAnchors returns anchors nearest-first; the outermost one —
                    // the root type carrying the whole BrowseName path — is the most specific
                    // authoring, the same ordering GetEffectiveInstanceDeclarations applies.
                    var enclosing = CollectDeclarationAnchors(parent);
                    enclosing.Reverse();
                    anchors.AddRange(enclosing);

                    // Least specific: whatever the parent's own TypeDefinition declares.
                    if (!string.IsNullOrEmpty(parent.TypeId))
                        anchors.Add((GetTypeChain(parent.TypeId!), new List<string>()));
                }

                foreach (var (typeChain, path) in anchors)
                {
                    var childPath = new List<string>(path) { key };
                    // GetTypeChain is most-base first, so walk it backwards: the most derived
                    // type that declares a value for this path wins.
                    for (var i = typeChain.Count - 1; i >= 0; i--)
                    {
                        if (ResolveBrowsePath(typeChain[i], childPath) is UAVariable declared && declared.Value != null)
                            return declared.Value;
                    }
                }
            }

            if (!string.IsNullOrEmpty(typeDefinitionId))
            {
                var typeChain = GetTypeChain(typeDefinitionId!);
                for (var i = typeChain.Count - 1; i >= 0; i--)
                {
                    if (Read(typeChain[i]) is UAVariableType variableType && variableType.Value != null)
                        return variableType.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Walks up the parent chain of <paramref name="node"/> collecting the types that can
        /// author children for it, each paired with the stripped-BrowseName path leading from
        /// that type down to the node. Ancestors are collected nearest-first; applying them in
        /// that order lets the outermost — most specific — authoring win.
        /// </summary>
        private List<(List<string> TypeChain, List<string> Path)> CollectDeclarationAnchors(UANode node)
        {
            var anchors = new List<(List<string>, List<string>)>();
            var path = new List<string>();
            var visited = new HashSet<string>();
            var current = node;

            while (current?.NodeId != null && visited.Add(current.NodeId))
            {
                path.Insert(0, DeclarationKey(current.BrowseName));

                if (string.IsNullOrEmpty(current.ParentId)) break;
                var parent = Read(current.ParentId!);
                if (parent == null) break;

                var parentIsType = parent.NodeClass == NodeClass.UAObjectType
                    || parent.NodeClass == NodeClass.UAVariableType;

                // A type authors declarations directly; an instance authors them through
                // its TypeDefinition.
                var anchorTypeId = parentIsType ? parent.NodeId : parent.TypeId;
                if (!string.IsNullOrEmpty(anchorTypeId))
                    anchors.Add((GetTypeChain(anchorTypeId!), new List<string>(path)));

                // Nothing above a type can declare children for this node.
                if (parentIsType) break;
                current = parent;
            }

            return anchors;
        }

        /// <summary>
        /// The supertype chain of a type, most-base first and ending with the type itself,
        /// so callers can apply overrides in derivation order.
        /// </summary>
        private List<string> GetTypeChain(string typeNodeId)
        {
            var chain = new List<string> { typeNodeId };
            var visited = new HashSet<string> { typeNodeId };
            var current = typeNodeId;
            while (true)
            {
                var superRefs = Browse(current, HasSubtypeId, includeForward: false, includeInverse: true);
                if (superRefs.Count == 0) break;
                current = superRefs[0].TargetNodeId;
                if (!visited.Add(current)) break;
                chain.Insert(0, current);
            }
            return chain;
        }

        /// <summary>
        /// Follows a stripped-BrowseName path down from <paramref name="startNodeId"/> over
        /// hierarchical references. Returns null if any segment is missing.
        /// </summary>
        private UANode? ResolveBrowsePath(string startNodeId, IReadOnlyList<string> path)
        {
            var current = Read(startNodeId);
            foreach (var segment in path)
            {
                if (current?.NodeId == null) return null;
                current = FindHierarchicalChild(current.NodeId, segment);
            }
            return current;
        }

        private UANode? FindHierarchicalChild(string nodeId, string declarationKey)
        {
            foreach (var r in BrowseWithSubtypes(nodeId, "i=33", includeForward: true, includeInverse: false))
            {
                if (r.ReferenceTypeId == HasSubtypeId || IsTypeOf(r.ReferenceTypeId, HasSubtypeId)) continue;
                var child = Read(r.TargetNodeId);
                if (child != null && DeclarationKey(child.BrowseName) == declarationKey) return child;
            }
            return null;
        }

        /// <summary>
        /// Replaces entries in <paramref name="map"/> with the declarations authored directly
        /// under <paramref name="declarationNode"/>. Children without a ModellingRule are plain
        /// instance children rather than declarations, and are skipped.
        /// </summary>
        private void OverlayAuthoredChildren(Dictionary<string, InstanceDeclaration> map, UANode declarationNode)
        {
            if (declarationNode.NodeId == null) return;

            foreach (var r in BrowseWithSubtypes(declarationNode.NodeId, "i=33", includeForward: true, includeInverse: false))
            {
                if (r.ReferenceTypeId == HasSubtypeId || IsTypeOf(r.ReferenceTypeId, HasSubtypeId)) continue;

                var child = Read(r.TargetNodeId);
                if (child == null) continue;
                if (string.IsNullOrEmpty(child.ModellingRuleId)) continue;

                map[DeclarationKey(child.BrowseName)] = new InstanceDeclaration
                {
                    SourceNode = child,
                    ReferenceTypeId = r.ReferenceTypeId,
                    SourceTypeNodeId = declarationNode.NodeId,
                };
            }
        }

        private static string DeclarationKey(string? browseName)
            => StripNsuPrefix(browseName ?? "").ToLowerInvariant();

        private static string StripNsuPrefix(string value)
        {
            var semi = value.IndexOf(';');
            return semi >= 0 ? value[(semi + 1)..] : value;
        }

        private static string? GetNodeDisplayText(UANode node)
        {
            return node.DisplayName?.T?.FirstOrDefault()?.ElementAtOrDefault(1);
        }

        private static LocalizedText MakeLocalizedText(string text)
        {
            return new LocalizedText
            {
                T = new List<List<string>> { new() { "", text } }
            };
        }

        #endregion

        private void RemoveChildren(ChildList children)
        {
            if (children.Objects != null)
            {
                foreach (var child in children.Objects.ToList())
                    if (child.NodeId != null) RemoveNode(child.NodeId);
            }
            if (children.Variables != null)
            {
                foreach (var child in children.Variables.ToList())
                    if (child.NodeId != null) RemoveNode(child.NodeId);
            }
            if (children.Methods != null)
            {
                foreach (var child in children.Methods.ToList())
                    if (child.NodeId != null) RemoveNode(child.NodeId);
            }
        }

        private static void AddIfNotDuplicate(Dictionary<string, List<ReferenceEntry>> dict, string key, ReferenceEntry entry)
        {
            var list = GetOrCreateList(dict, key);
            foreach (var existing in list)
            {
                if (existing.SourceNodeId == entry.SourceNodeId &&
                    existing.ReferenceTypeId == entry.ReferenceTypeId &&
                    existing.TargetNodeId == entry.TargetNodeId &&
                    existing.IsForward == entry.IsForward)
                    return;
            }
            list.Add(entry);
        }

        private static List<T> GetOrCreateList<T>(Dictionary<string, List<T>> dict, string key)
        {
            if (!dict.TryGetValue(key, out var list))
            {
                list = new List<T>();
                dict[key] = list;
            }
            return list;
        }
    }
}
