using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Opc.Ua.JsonNodeSet.Model;

namespace Opc.Ua.JsonNodeSet;

/// <summary>
/// Part 6 Annex I ChangeSet processing.
///
/// <para>A ChangeSet is a UANodeSet with <c>ChangeSet = true</c> whose Nodes are Insert, Update or
/// Delete operations against an existing model rather than a model definition. An archive may carry
/// a different Operation per file so that a sequence of changes is applied in a fixed order; any
/// error rolls the whole sequence back.</para>
///
/// <para>Applying an Update needs to tell "field absent" from "field present and JSON null" — the
/// first leaves the current value alone, the second clears an optional field and is an error on a
/// mandatory one. A deserialized POCO cannot express that difference, so the Update path works from
/// the raw JSON and uses <see cref="JsonConvert.PopulateObject(string, object)"/>, which assigns
/// only the properties actually present in the document.</para>
/// </summary>
public partial class AddressSpace
{
    /// <summary>
    /// PopulateObject reuses an existing collection and appends to it by default, which would make
    /// a supplied Children / RolePermissions / Definition merge with the current value. Annex I.10,
    /// I.19, I.21 and I.22 all require replacement, so every populated value is replaced outright.
    /// References are the one additive field and never go through this path.
    /// </summary>
    private static readonly JsonSerializerSettings UpdateSettings = new()
    {
        ObjectCreationHandling = ObjectCreationHandling.Replace,
    };

    /// <summary>Mandatory fields (Annex I.7). A JSON null for any of these is an error.</summary>
    private static readonly HashSet<string> MandatoryFields = new(StringComparer.Ordinal)
    {
        nameof(UANode.NodeId), nameof(UANode.NodeClass), nameof(UANode.BrowseName),
    };

    /// <summary>
    /// Fields marked RO in Annex I.7 / I.10. They are ignored by an Update rather than rejected,
    /// so they are stripped from the document before it is populated onto the existing Node.
    /// </summary>
    private static readonly HashSet<string> ReadOnlyFields = new(StringComparer.Ordinal)
    {
        nameof(UANode.NodeId), nameof(UANode.NodeClass), nameof(UANode.BrowseName),
        nameof(UANode.SymbolicName), nameof(UANode.ParentId), nameof(UANode.TypeId),
        nameof(UANode.DesignToolOnly), nameof(UAMethod.MethodDeclarationId),
    };

    /// <summary>The outcome of applying one or more ChangeSet files.</summary>
    public sealed class ChangeSetResult
    {
        public int Inserted { get; internal set; }
        public int Updated { get; internal set; }
        public int Deleted { get; internal set; }

        /// <summary>
        /// Non-fatal problems. Annex I.20: changes that leave dangling References are warnings
        /// rather than errors.
        /// </summary>
        public List<string> Warnings { get; } = new();
    }

    /// <summary>Raised when a ChangeSet cannot be applied. No change has been made.</summary>
    public sealed class ChangeSetException : Exception
    {
        public ChangeSetException(IReadOnlyList<string> errors)
            : base($"ChangeSet rejected: {string.Join("; ", errors)}")
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>Applies a single ChangeSet document.</summary>
    public ChangeSetResult ApplyChangeSet(JObject file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return ApplyChangeSet(new[] { file });
    }

    /// <summary>Applies a single ChangeSet document supplied as JSON text.</summary>
    public ChangeSetResult ApplyChangeSet(string json)
        => ApplyChangeSet(JObject.Parse(json));

    /// <summary>
    /// Applies a sequence of ChangeSet documents in order, as an archive does. Every file is
    /// validated against the state projected from the files before it; if any file would fail the
    /// whole sequence is rejected and nothing is changed.
    /// </summary>
    public ChangeSetResult ApplyChangeSet(IReadOnlyList<JObject> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var errors = DryRun(files);
        if (errors.Count > 0) throw new ChangeSetException(errors);

        var result = new ChangeSetResult();
        var snapshot = Snapshot();

        try
        {
            foreach (var file in files) ApplyFile(file, result);
        }
        catch
        {
            // DryRun has already cleared every error the spec defines, so reaching here means an
            // unexpected failure part-way through. Annex I.3 requires a complete rollback.
            Restore(snapshot);
            throw;
        }

        CollectDanglingReferenceWarnings(result);
        return result;
    }

    // ---------- validation ----------

    /// <summary>
    /// Walks every file against a projected view of the address space — the current NodeIds plus
    /// the effect of each preceding file — and returns every error found. Nothing is mutated.
    /// </summary>
    private List<string> DryRun(IReadOnlyList<JObject> files)
    {
        var errors = new List<string>();
        var knownUris = GetKnownNamespaceUris();

        // NodeId -> ParentId, projected forward across the files so cascades can be resolved.
        var projected = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var node in _sequence)
        {
            if (node.NodeId != null) projected[node.NodeId] = node.ParentId;
        }

        for (int fi = 0; fi < files.Count; fi++)
        {
            var file = files[fi];
            var operation = ReadOperation(file);
            var where = files.Count > 1 ? $"file {fi + 1}: " : string.Empty;

            foreach (var nodeJson in EnumerateNodeJson(file))
            {
                var nodeId = (string?)nodeJson[nameof(UANode.NodeId)];

                if (string.IsNullOrEmpty(nodeId))
                {
                    errors.Add($"{where}a Node has no NodeId.");
                    continue;
                }

                foreach (var field in MandatoryFields)
                {
                    if (nodeJson.TryGetValue(field, out var v) && v.Type == JTokenType.Null)
                        errors.Add($"{where}'{nodeId}' sets mandatory field {field} to null.");
                }

                switch (operation)
                {
                    case OperationType.Insert:
                        if (projected.ContainsKey(nodeId))
                        {
                            errors.Add($"{where}Insert of '{nodeId}' but a Node with that NodeId already exists.");
                        }
                        else
                        {
                            // The public AddNode path validates namespaces; an Insert has to do the
                            // same, and has to do it here so a bad Node is rejected before any file
                            // in the sequence is applied.
                            try
                            {
                                ValidateNode(DeserializeNode(nodeJson), knownUris, errors);
                            }
                            catch (Exception e)
                            {
                                errors.Add($"{where}'{nodeId}' could not be read: {e.Message}");
                            }

                            ProjectInsert(nodeJson, nodeId, null, projected);
                        }
                        break;

                    case OperationType.Update:
                        if (!projected.ContainsKey(nodeId))
                            errors.Add($"{where}Update of '{nodeId}' but no Node with that NodeId exists.");
                        break;

                    case OperationType.Delete:
                        if (!projected.ContainsKey(nodeId))
                        {
                            errors.Add($"{where}Delete of '{nodeId}' but no Node with that NodeId exists.");
                        }
                        else
                        {
                            foreach (var id in ProjectedSubtree(nodeId, projected)) projected.Remove(id);
                        }
                        break;
                }
            }
        }

        return errors;
    }

    /// <summary>Records an inserted Node and everything nested under it in the projected view.</summary>
    private static void ProjectInsert(JObject nodeJson, string nodeId, string? parentId, Dictionary<string, string?> projected)
    {
        projected[nodeId] = parentId ?? (string?)nodeJson[nameof(UANode.ParentId)];

        foreach (var childJson in EnumerateChildJson(nodeJson))
        {
            var childId = (string?)childJson[nameof(UANode.NodeId)];
            if (!string.IsNullOrEmpty(childId)) ProjectInsert(childJson, childId, nodeId, projected);
        }
    }

    /// <summary>The NodeId plus every Node transitively owned by it, per the projected view.</summary>
    private static List<string> ProjectedSubtree(string nodeId, Dictionary<string, string?> projected)
    {
        var result = new List<string> { nodeId };
        var frontier = new List<string> { nodeId };

        while (frontier.Count > 0)
        {
            var next = new List<string>();
            foreach (var (id, parent) in projected)
            {
                if (parent != null && frontier.Contains(parent) && !result.Contains(id))
                {
                    result.Add(id);
                    next.Add(id);
                }
            }
            frontier = next;
        }

        return result;
    }

    // ---------- application ----------

    private void ApplyFile(JObject file, ChangeSetResult result)
    {
        var operation = ReadOperation(file);

        foreach (var nodeJson in EnumerateNodeJson(file))
        {
            var nodeId = (string)nodeJson[nameof(UANode.NodeId)]!;

            switch (operation)
            {
                case OperationType.Insert: ApplyInsert(nodeJson); result.Inserted++; break;
                case OperationType.Update: ApplyUpdate(nodeJson, nodeId); result.Updated++; break;
                case OperationType.Delete: result.Deleted += ApplyDelete(nodeId); break;
            }
        }
    }

    private void ApplyInsert(JObject nodeJson)
    {
        var node = DeserializeNode(nodeJson);
        AddNodeInternal(node, null);
        InvalidateChangeSetCaches();
    }

    /// <summary>
    /// Annex I.9 Update. Fields present in the document replace the current value; absent fields are
    /// left alone; a null clears an optional field. References merge rather than replace, so they
    /// are handled separately from the populate step.
    /// </summary>
    private void ApplyUpdate(JObject nodeJson, string nodeId)
    {
        var node = _nodes[nodeId];

        var patch = (JObject)nodeJson.DeepClone();

        // Annex I.9: fields that are not updateable are ignored rather than rejected.
        foreach (var field in ReadOnlyFields) patch.Remove(field);

        // Annex I.20: References are additive, so they never go through PopulateObject, whose array
        // handling would replace them.
        var hasReferences = patch.TryGetValue(nameof(UANode.References), out var referencesToken);
        patch.Remove(nameof(UANode.References));

        // Annex I.10 / I.19: a ChildList replaces the existing children wholesale, which means the
        // outgoing children have to leave the index before the node is repopulated.
        var replacesChildren = patch.ContainsKey(nameof(UANode.Children));

        UnindexOwnReferences(node);
        if (replacesChildren && node.Children != null) RemoveChildrenFromIndex(node.Children);

        JsonConvert.PopulateObject(patch.ToString(), node, UpdateSettings);

        if (hasReferences) ApplyReferenceChange(node, referencesToken!);

        IndexOwnReferences(node);
        if (replacesChildren && node.Children != null) AddChildren(node.Children, node.NodeId!);

        InvalidateChangeSetCaches();
    }

    /// <summary>
    /// Annex I.20. A null References field deletes every existing Reference; otherwise the supplied
    /// References are added to the existing set, skipping duplicates.
    /// </summary>
    private static void ApplyReferenceChange(UANode node, JToken referencesToken)
    {
        if (referencesToken.Type == JTokenType.Null)
        {
            node.References = null;
            return;
        }

        var incoming = referencesToken.ToObject<List<Reference>>() ?? new List<Reference>();
        node.References ??= new List<Reference>();

        foreach (var reference in incoming)
        {
            bool duplicate = node.References.Any(e =>
                e.ReferenceTypeId == reference.ReferenceTypeId
                && e.TargetId == reference.TargetId
                && (e.IsForward ?? true) == (reference.IsForward ?? true));

            if (!duplicate) node.References.Add(reference);
        }
    }

    /// <summary>Annex I.9 Delete: the Node and every Node whose ParentId names it.</summary>
    private int ApplyDelete(string nodeId)
    {
        var subtree = OwnedSubtree(nodeId);
        var removed = 0;

        // Deepest first, so a parent is never removed while its children are still indexed.
        for (int i = subtree.Count - 1; i >= 0; i--)
        {
            if (RemoveNode(subtree[i])) removed++;
        }

        InvalidateChangeSetCaches();
        return removed;
    }

    /// <summary>The NodeId plus every Node transitively owned by it, per the live address space.</summary>
    private List<string> OwnedSubtree(string nodeId)
    {
        var result = new List<string> { nodeId };

        for (int i = 0; i < result.Count; i++)
        {
            var parent = result[i];
            foreach (var node in _sequence)
            {
                if (node.NodeId != null && node.ParentId == parent && !result.Contains(node.NodeId))
                    result.Add(node.NodeId);
            }
        }

        return result;
    }

    // ---------- indexing ----------

    /// <summary>
    /// Adds or removes only the reference index entries that originate from this Node's own
    /// References list, together with their mirrored counterparts. References declared by other
    /// Nodes that happen to target this one are left untouched.
    /// </summary>
    private void IndexOwnReferences(UANode node) => EachOwnReference(node, add: true);

    private void UnindexOwnReferences(UANode node) => EachOwnReference(node, add: false);

    private void EachOwnReference(UANode node, bool add)
    {
        if (node.References == null || node.NodeId == null) return;

        foreach (var r in node.References)
        {
            if (r.TargetId == null || r.ReferenceTypeId == null) continue;

            bool isForward = r.IsForward ?? true;
            var own = isForward ? _forwardRefs : _inverseRefs;
            var mirror = isForward ? _inverseRefs : _forwardRefs;

            if (add)
            {
                AddIfNotDuplicate(own, node.NodeId, new ReferenceEntry
                {
                    SourceNodeId = node.NodeId,
                    ReferenceTypeId = r.ReferenceTypeId,
                    TargetNodeId = r.TargetId,
                    IsForward = isForward,
                });

                AddIfNotDuplicate(mirror, r.TargetId, new ReferenceEntry
                {
                    SourceNodeId = r.TargetId,
                    ReferenceTypeId = r.ReferenceTypeId,
                    TargetNodeId = node.NodeId,
                    IsForward = !isForward,
                });
            }
            else
            {
                RemoveEntry(own, node.NodeId, r.ReferenceTypeId, r.TargetId);
                RemoveEntry(mirror, r.TargetId, r.ReferenceTypeId, node.NodeId);
            }
        }
    }

    private static void RemoveEntry(Dictionary<string, List<ReferenceEntry>> index,
        string key, string referenceTypeId, string targetNodeId)
    {
        if (!index.TryGetValue(key, out var list)) return;
        list.RemoveAll(e => e.ReferenceTypeId == referenceTypeId && e.TargetNodeId == targetNodeId);
        if (list.Count == 0) index.Remove(key);
    }

    private void RemoveChildrenFromIndex(ChildList children)
    {
        foreach (var child in EnumerateChildNodes(children))
        {
            if (child.NodeId != null) RemoveNode(child.NodeId);
        }
    }

    private static IEnumerable<UANode> EnumerateChildNodes(ChildList children)
    {
        foreach (var c in (IEnumerable<UANode>?)children.Objects ?? Array.Empty<UANode>()) yield return c;
        foreach (var c in (IEnumerable<UANode>?)children.Variables ?? Array.Empty<UANode>()) yield return c;
        foreach (var c in (IEnumerable<UANode>?)children.Methods ?? Array.Empty<UANode>()) yield return c;
    }

    private void InvalidateChangeSetCaches()
    {
        InvalidateSupertypeCache();
        _encodingToDataType = null;   // rebuilt on demand by EnsureVariantIndexes
    }

    private void CollectDanglingReferenceWarnings(ChangeSetResult result)
    {
        foreach (var node in _sequence)
        {
            if (node.References == null) continue;

            foreach (var r in node.References)
            {
                if (r.TargetId == null || _nodes.ContainsKey(r.TargetId)) continue;
                if (!r.TargetId.StartsWith(NsuPrefix) && !r.TargetId.Contains(';')) continue; // core / external
                result.Warnings.Add($"'{node.NodeId}' references '{r.TargetId}', which is not present.");
            }
        }
    }

    // ---------- snapshot / rollback ----------

    private sealed class SpaceSnapshot
    {
        public List<UANode> Roots { get; init; } = new();
        public Dictionary<string, ModelDefinition> Models { get; init; } = new();
    }

    /// <summary>
    /// Deep-clones the top-level Nodes. Children come with their parent because they are nested in
    /// the ChildList, which is the same shape the serializer emits.
    /// </summary>
    private SpaceSnapshot Snapshot()
    {
        var snapshot = new SpaceSnapshot();

        foreach (var node in _sequence)
        {
            if (node.NodeId == null) continue;
            if (node.ParentId != null && _nodes.ContainsKey(node.ParentId)) continue;
            snapshot.Roots.Add((UANode)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(node), node.GetType())!);
        }

        foreach (var (uri, model) in _models)
        {
            snapshot.Models[uri] = JsonConvert.DeserializeObject<ModelDefinition>(JsonConvert.SerializeObject(model))!;
        }

        return snapshot;
    }

    private void Restore(SpaceSnapshot snapshot)
    {
        _nodes.Clear();
        _sequence.Clear();
        _forwardRefs.Clear();
        _inverseRefs.Clear();
        _models.Clear();

        foreach (var (uri, model) in snapshot.Models) _models[uri] = model;
        foreach (var node in snapshot.Roots) AddNodeInternal(node, null);

        InvalidateChangeSetCaches();
    }

    // ---------- JSON helpers ----------

    private static OperationType ReadOperation(JObject file)
    {
        // Annex I.4: Operation is only meaningful when ChangeSet is TRUE, and ChangeSet = FALSE is
        // equivalent to Insert.
        if ((bool?)file[nameof(UANodeSet.ChangeSet)] != true) return OperationType.Insert;

        var token = file[nameof(UANodeSet.Operation)];
        if (token == null || token.Type == JTokenType.Null) return OperationType.Insert;

        return token.Type == JTokenType.String
            ? Enum.Parse<OperationType>((string)token!, ignoreCase: true)
            : (OperationType)(int)token;
    }

    /// <summary>
    /// Every top-level Node in the document, in NodeContainer order. The container properties are
    /// enumerated rather than named so this survives a rename of the arrays.
    /// </summary>
    private static IEnumerable<JObject> EnumerateNodeJson(JObject file)
    {
        if (file[nameof(UANodeSet.Nodes)] is not JObject container) yield break;

        foreach (var property in container.Properties())
        {
            if (property.Value is not JArray array) continue;
            foreach (var item in array)
            {
                if (item is JObject node) yield return node;
            }
        }
    }

    private static IEnumerable<JObject> EnumerateChildJson(JObject nodeJson)
    {
        if (nodeJson[nameof(UANode.Children)] is not JObject children) yield break;

        foreach (var property in children.Properties())
        {
            if (property.Value is not JArray array) continue;
            foreach (var item in array)
            {
                if (item is JObject child) yield return child;
            }
        }
    }

    /// <summary>Deserializes a Node into the concrete type named by its NodeClass.</summary>
    private static UANode DeserializeNode(JObject nodeJson)
    {
        var nodeClass = (NodeClass?)(int?)nodeJson[nameof(UANode.NodeClass)] ?? NodeClass.Unspecified;

        Type type = nodeClass switch
        {
            NodeClass.UAObject => typeof(UAObject),
            NodeClass.UAVariable => typeof(UAVariable),
            NodeClass.UAMethod => typeof(UAMethod),
            NodeClass.UAObjectType => typeof(UAObjectType),
            NodeClass.UAVariableType => typeof(UAVariableType),
            NodeClass.UAReferenceType => typeof(UAReferenceType),
            NodeClass.UADataType => typeof(UADataType),
            NodeClass.UAView => typeof(UAView),
            _ => throw new InvalidOperationException(
                $"Node '{(string?)nodeJson[nameof(UANode.NodeId)]}' has no usable NodeClass."),
        };

        return (UANode)nodeJson.ToObject(type)!;
    }
}
