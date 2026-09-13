using System.IO.Compression;
using System.Formats.Tar;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Opc.Ua.JsonNodeSet.Model;

namespace Opc.Ua.JsonNodeSet;

/// <summary>Something about the file that breaks an Annex I.2 ordering rule.</summary>
public sealed class OrderViolation
{
    public OrderViolation(string rule, string location, string message)
    {
        Rule = rule;
        Location = location;
        Message = message;
    }

    /// <summary>Short name of the rule broken, for grouping the output.</summary>
    public string Rule { get; }

    /// <summary>Where in the document, e.g. "Nodes.DataTypes[3].Children.Variables[0]".</summary>
    public string Location { get; }

    public string Message { get; }

    public override string ToString() => $"[{Rule}] {Location}: {Message}";
}

/// <summary>The outcome of checking one file.</summary>
public sealed class OrderReport
{
    public OrderReport(string filePath, bool orderedAsserted, int nodeCount, int declarationCount, IReadOnlyList<OrderViolation> violations)
    {
        FilePath = filePath;
        OrderedAsserted = orderedAsserted;
        NodeCount = nodeCount;
        DeclarationCount = declarationCount;
        Violations = violations;
    }

    public string FilePath { get; }

    /// <summary>Whether the document claims <c>Ordered</c>. A compliant file may still not claim it.</summary>
    public bool OrderedAsserted { get; }

    public int NodeCount { get; }
    public int DeclarationCount { get; }
    public IReadOnlyList<OrderViolation> Violations { get; }

    public bool Compliant => Violations.Count == 0;
}

/// <summary>
/// Checks a JSON NodeSet against the Annex I.2 ordering rules, reading the document as written
/// rather than as deserialized — property order and container order survive only in the raw JSON.
///
/// <para>The rules checked are:</para>
/// <list type="bullet">
/// <item><b>PropertyOrder</b> — every object's fields appear in the normative order. The expected
/// order is taken from the model classes themselves (their <c>JsonProperty(Order)</c> attributes),
/// so it cannot drift from what the writer emits.</item>
/// <item><b>ContainerOrder</b> — the eight NodeContainer properties appear in NodeClass order, which
/// is what puts every type Node ahead of every instance Node.</item>
/// <item><b>SuperTypeOrder</b> — within a type container, a supertype precedes its subtypes.</item>
/// <item><b>ForwardReference</b> — no identifying field (ParentId, TypeId, ModellingRuleId,
/// DataType, MethodDeclarationId, RoleId, or a DataTypeDefinition field's DataType) names a Node
/// defined later in the document unless that Node is pre-declared in Declarations. The References
/// of a Node are exempt: they name arbitrary Nodes, so no emission order can make them all read
/// backwards. HasSubtype is the exception, and it is checked by SuperTypeOrder rather than
/// here.</item>
/// <item><b>Declaration</b> — Declarations carry only the mandatory fields, and every one of them
/// is a Node the document actually defines.</item>
/// </list>
///
/// <para>The rules govern the JSON encoding, so XML input is rejected: the XML NodeSet has no
/// NodeContainer partition and no Declarations, and converting it produces ordered JSON by
/// construction.</para>
/// </summary>
public static class NodeSetOrderValidator
{
    private const string HasSubtype = "i=45";

    private static readonly IContractResolver s_resolver = new DefaultContractResolver();

    private static readonly string[] s_typeContainers =
    {
        nameof(UANodeSetNodes.ReferenceTypes),
        nameof(UANodeSetNodes.DataTypes),
        nameof(UANodeSetNodes.VariableTypes),
        nameof(UANodeSetNodes.ObjectTypes)
    };

    private static readonly Dictionary<int, Type> s_nodeTypes = new()
    {
        [1] = typeof(UAObject),
        [2] = typeof(UAVariable),
        [4] = typeof(UAMethod),
        [8] = typeof(UAObjectType),
        [16] = typeof(UAVariableType),
        [32] = typeof(UAReferenceType),
        [64] = typeof(UADataType),
        [128] = typeof(UAView),
    };

    /// <summary>
    /// True when the file is one of the encodings the ordering rules apply to.
    ///
    /// <para>The JSONL layout is excluded for now. The same rules govern it, but not in the same
    /// terms — it has no NodeContainer partition to check the order of, its Nodes are one per line
    /// rather than nested, and the checker below reads a whole document into a JObject, which is the
    /// one thing that layout exists to avoid.</para>
    /// </summary>
    public static bool IsSupported(string filePath)
    {
        if (filePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Anything a registered format claims is checked in that format's own terms, not here —
        // which is what excludes JSONL and JSON-LD when those assemblies are present, without this
        // file needing to name them. A build with no formats registered has nothing to exclude.
        foreach (var format in NodeSetTool.NodeSetSerializer.RegisteredFormats)
        {
            if (format.MatchesExtension(filePath))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the file is a gzipped archive rather than a single JSON document. Decided by the
    /// gzip magic number, not the extension, because the archives ship as both .uanodeset and .gz.
    /// </summary>
    public static bool IsArchive(string filePath)
    {
        using var stream = File.OpenRead(filePath);

        return stream.ReadByte() == 0x1F && stream.ReadByte() == 0x8B;
    }

    /// <summary>Checks a .json document or a .uanodeset archive.</summary>
    public static OrderReport Validate(string filePath)
    {
        return IsArchive(filePath) ? ValidateArchive(filePath) : ValidateDocument(filePath);
    }

    private static OrderReport ValidateDocument(string filePath)
    {
        var root = JObject.Parse(File.ReadAllText(filePath));
        var violations = new List<OrderViolation>();

        CheckObject(root, typeof(UANodeSet), "", violations);
        CheckModels(root, "", violations);
        CheckAllNodes(root, "", violations);

        var context = new DecodeContext(violations);
        context.AddDocument(root, "");
        context.Run();

        return new OrderReport(
            filePath,
            root.Value<bool?>(nameof(UANodeSet.Ordered)) == true,
            context.DefinedCount,
            context.DeclaredCount,
            violations);
    }

    private static OrderReport ValidateArchive(string filePath)
    {
        var violations = new List<OrderViolation>();
        var entries = ReadArchive(filePath);

        if (!entries.TryGetValue(Manifest.FileName, out var manifestBytes))
        {
            violations.Add(new OrderViolation("Manifest", filePath,
                $"The archive has no '{Manifest.FileName}' entry, so the order the files are processed in is undefined."));

            return new OrderReport(filePath, false, 0, 0, violations);
        }

        var manifest = JObject.Parse(Text(manifestBytes));
        CheckObject(manifest, typeof(Manifest), Manifest.FileName, violations);
        CheckModels(manifest, Manifest.FileName, violations);

        var context = new DecodeContext(violations);
        context.AddDeclarations(manifest, Manifest.FileName);

        var named = manifest[nameof(Manifest.Files)]?.Values<string>().OfType<string>().ToList() ?? new List<string>();
        var unread = new HashSet<string>(entries.Keys, StringComparer.Ordinal);
        unread.Remove(Manifest.FileName);

        foreach (var name in named)
        {
            if (!entries.TryGetValue(name, out var bytes))
            {
                violations.Add(new OrderViolation("Manifest", Manifest.FileName,
                    $"names '{name}', which is not in the archive."));
                continue;
            }

            unread.Remove(name);

            var file = JObject.Parse(Text(bytes));
            CheckObject(file, typeof(UANodeSet), name, violations);
            CheckAllNodes(file, name, violations);
            context.AddDocument(file, name);
        }

        foreach (var name in unread.OrderBy(x => x, StringComparer.Ordinal))
        {
            violations.Add(new OrderViolation("Manifest", name,
                "is in the archive but not named by the manifest."));
        }

        context.Run();

        return new OrderReport(
            filePath,
            manifest.Value<bool?>(nameof(Manifest.Ordered)) == true,
            context.DefinedCount,
            context.DeclaredCount,
            violations);
    }

    #region Property order

    /// <summary>
    /// The field names of a model class in the order the writer emits them. Derived from the
    /// Newtonsoft contract so the expectation is the model, not a second copy of the schema.
    /// </summary>
    private static List<string> ExpectedOrder(Type type)
    {
        var contract = (JsonObjectContract)s_resolver.ResolveContract(type);
        return contract.Properties.Where(p => !p.Ignored).Select(p => p.PropertyName!).ToList();
    }

    private static void CheckObject(JObject obj, Type type, string location, List<OrderViolation> violations)
    {
        var expected = ExpectedOrder(type);
        var present = obj.Properties().Select(p => p.Name).ToList();

        var unknown = present.Where(name => !expected.Contains(name)).ToList();

        if (unknown.Count > 0)
        {
            violations.Add(new OrderViolation("PropertyOrder", Describe(location),
                $"{type.Name} has fields the schema does not define: {String.Join(", ", unknown)}."));
        }

        var known = present.Where(expected.Contains).ToList();
        var ranks = known.Select(name => expected.IndexOf(name)).ToList();

        if (!IsAscending(ranks))
        {
            var rule = type == typeof(UANodeSetNodes) ? "ContainerOrder" : "PropertyOrder";

            violations.Add(new OrderViolation(rule, Describe(location),
                $"{type.Name} fields are out of order: found {String.Join(", ", known)}; " +
                $"expected {String.Join(", ", expected.Where(known.Contains))}."));
        }
    }

    private static bool IsAscending(List<int> values)
    {
        for (int ii = 1; ii < values.Count; ii++)
        {
            if (values[ii] < values[ii - 1]) return false;
        }

        return true;
    }

    private static void CheckModels(JObject root, string location, List<OrderViolation> violations)
    {
        if (root[nameof(UANodeSet.SPDX)] is JObject spdx)
        {
            CheckObject(spdx, typeof(SpdxDeclaration), Join(location, nameof(UANodeSet.SPDX)), violations);
        }

        var models = root[nameof(UANodeSet.Models)] as JArray;

        for (int ii = 0; ii < (models?.Count ?? 0); ii++)
        {
            if (models![ii] is not JObject model) continue;

            var path = Join(location, $"{nameof(UANodeSet.Models)}[{ii}]");
            CheckObject(model, typeof(ModelDefinition), path, violations);

            CheckList(model, nameof(ModelDefinition.RequiredModels), typeof(ModelReference), path, violations);
            CheckList(model, nameof(ModelDefinition.DefaultRolePermissions), typeof(RolePermission), path, violations);
        }
    }

    private static void CheckList(JObject owner, string property, Type itemType, string location, List<OrderViolation> violations)
    {
        if (owner[property] is not JArray items) return;

        for (int ii = 0; ii < items.Count; ii++)
        {
            if (items[ii] is JObject item)
            {
                CheckObject(item, itemType, Join(location, $"{property}[{ii}]"), violations);
            }
        }
    }

    private static void CheckNode(JObject node, string location, List<OrderViolation> violations)
    {
        var nodeClass = node.Value<int?>(nameof(UANode.NodeClass));

        if (nodeClass == null || !s_nodeTypes.TryGetValue(nodeClass.Value, out var type))
        {
            violations.Add(new OrderViolation("PropertyOrder", Describe(location),
                $"NodeClass is {(nodeClass?.ToString() ?? "absent")}, which is not a concrete NodeClass."));
            return;
        }

        CheckObject(node, type, location, violations);

        foreach (var property in new[] { nameof(UANode.DisplayName), nameof(UANode.Description), nameof(UAReferenceType.InverseName) })
        {
            if (node[property] is JObject text)
            {
                CheckObject(text, typeof(LocalizedText), Join(location, property), violations);
            }
        }

        if (node[nameof(UAVariable.Value)] is JObject value)
        {
            CheckObject(value, typeof(Variant), Join(location, nameof(UAVariable.Value)), violations);
        }

        CheckList(node, nameof(UANode.References), typeof(Reference), location, violations);
        CheckList(node, nameof(UANode.RolePermissions), typeof(RolePermission), location, violations);

        if (node[nameof(UADataType.Definition)] is JObject definition)
        {
            var path = Join(location, nameof(UADataType.Definition));
            CheckObject(definition, typeof(DataTypeDefinition), path, violations);
            CheckList(definition, nameof(DataTypeDefinition.Fields), typeof(DataTypeField), path, violations);
        }

        if (node[nameof(UANode.Children)] is JObject children)
        {
            var path = Join(location, nameof(UANode.Children));
            CheckObject(children, typeof(ChildList), path, violations);

            foreach (var (child, childPath) in EnumerateChildren(children, path))
            {
                CheckNode(child, childPath, violations);
            }
        }
    }

    #endregion

    #region Decode simulation

    /// <summary>
    /// Replays the document the way a decoder reads it: register each Node as its NodeId is read,
    /// and complain about any field naming a Node that is defined but not yet registered.
    /// </summary>
    private sealed class DecodeContext
    {
        private readonly List<OrderViolation> m_violations;
        private readonly List<(JObject Root, string File)> m_documents = new();
        private readonly HashSet<string> m_defined = new(StringComparer.Ordinal);
        private readonly HashSet<string> m_registered = new(StringComparer.Ordinal);
        private readonly List<(string NodeId, string File)> m_declared = new();

        public DecodeContext(List<OrderViolation> violations)
        {
            m_violations = violations;
        }

        public int DefinedCount => m_defined.Count;
        public int DeclaredCount => m_declared.Count;

        public void AddDocument(JObject root, string file)
        {
            m_documents.Add((root, file));
            AddDeclarations(root, file);
        }

        public void AddDeclarations(JObject root, string file)
        {
            if (root[nameof(UANodeSet.Declarations)] is not JArray declarations) return;

            for (int ii = 0; ii < declarations.Count; ii++)
            {
                if (declarations[ii] is not JObject declaration) continue;

                var location = Join(file, $"{nameof(UANodeSet.Declarations)}[{ii}]");
                CheckObject(declaration, typeof(UANode), location, m_violations);

                var extra = declaration.Properties().Select(p => p.Name)
                    .Where(name => name is not (nameof(UANode.NodeId) or nameof(UANode.NodeClass) or nameof(UANode.BrowseName)))
                    .ToList();

                if (extra.Count > 0)
                {
                    m_violations.Add(new OrderViolation("Declaration", Describe(location),
                        $"carries fields beyond the mandatory ones: {String.Join(", ", extra)}."));
                }

                if (declaration.Value<string>(nameof(UANode.NodeId)) is { } nodeId)
                {
                    m_declared.Add((nodeId, location));
                    m_registered.Add(nodeId);
                }
            }
        }

        public void Run()
        {
            foreach (var (root, file) in m_documents) Collect(root, file);
            foreach (var (nodeId, location) in m_declared)
            {
                if (!m_defined.Contains(nodeId))
                {
                    m_violations.Add(new OrderViolation("Declaration", Describe(location),
                        $"declares {nodeId}, which this NodeSet does not define."));
                }
            }

            foreach (var (root, file) in m_documents) Read(root, file);
        }

        private void Collect(JObject root, string file)
        {
            foreach (var (node, _) in EnumerateTopLevel(root, file)) CollectNode(node);
        }

        private void CollectNode(JObject node)
        {
            if (node.Value<string>(nameof(UANode.NodeId)) is { } nodeId) m_defined.Add(nodeId);

            if (node[nameof(UANode.Children)] is JObject children)
            {
                foreach (var (child, _) in EnumerateChildren(children, "")) CollectNode(child);
            }
        }

        private void Read(JObject root, string file)
        {
            string? lastInstanceContainer = null;

            foreach (var (node, location, container) in EnumerateTopLevelWithContainer(root, file))
            {
                if (s_typeContainers.Contains(container))
                {
                    if (lastInstanceContainer != null)
                    {
                        m_violations.Add(new OrderViolation("ContainerOrder", Describe(location),
                            $"type container {container} appears after instance container {lastInstanceContainer}."));
                        lastInstanceContainer = null; // report once per transition
                    }
                }
                else
                {
                    lastInstanceContainer = container;
                }

                ReadNode(node, location);
            }

            CheckSuperTypeOrder(root, file);
        }

        private void ReadNode(JObject node, string location)
        {
            var nodeId = node.Value<string>(nameof(UANode.NodeId));
            if (nodeId != null) m_registered.Add(nodeId);

            var context = $"{node.Value<string>(nameof(UANode.BrowseName))} [{nodeId}]";

            // Fields emitted ahead of Children.
            Reference(node, nameof(UANode.ParentId), location, context);
            Reference(node, nameof(UANode.TypeId), location, context);
            Reference(node, nameof(UANode.ModellingRuleId), location, context);

            if (node[nameof(UANode.RolePermissions)] is JArray permissions)
            {
                foreach (var permission in permissions.OfType<JObject>())
                {
                    Reference(permission, nameof(RolePermission.RoleId), location, context);
                }
            }

            if (node[nameof(UANode.Children)] is JObject children)
            {
                foreach (var (child, childPath) in EnumerateChildren(children, Join(location, nameof(UANode.Children))))
                {
                    ReadNode(child, childPath);
                }
            }

            // References are exempt: they name arbitrary Nodes, so no emission order can make them
            // all read backwards. Only the fields that identify a Node are checked. HasSubtype is
            // the exception and CheckSuperTypeOrder covers it.
            Reference(node, nameof(UAVariable.DataType), location, context);
            Reference(node, nameof(UAMethod.MethodDeclarationId), location, context);

            if (node[nameof(UADataType.Definition)]?[nameof(DataTypeDefinition.Fields)] is JArray fields)
            {
                foreach (var field in fields.OfType<JObject>())
                {
                    Reference(field, nameof(DataTypeField.DataType), location, context);
                }
            }
        }

        private void Reference(JObject owner, string property, string location, string context)
        {
            if (owner.Value<string>(property) is not { } target) return;

            // Only Nodes this NodeSet defines need ordering; the rest come from other models.
            if (!m_defined.Contains(target) || m_registered.Contains(target)) return;

            m_violations.Add(new OrderViolation("ForwardReference", Describe(location),
                $"{context} names {target} via {property} before it is defined. " +
                $"Order it earlier or add it to Declarations."));
        }

        private void CheckSuperTypeOrder(JObject root, string file)
        {
            if (root[nameof(UANodeSet.Nodes)] is not JObject nodes) return;

            foreach (var container in s_typeContainers)
            {
                if (nodes[container] is not JArray items) continue;

                var position = new Dictionary<string, int>(StringComparer.Ordinal);

                for (int ii = 0; ii < items.Count; ii++)
                {
                    if (items[ii] is JObject node && node.Value<string>(nameof(UANode.NodeId)) is { } id)
                    {
                        position[id] = ii;
                    }
                }

                for (int ii = 0; ii < items.Count; ii++)
                {
                    if (items[ii] is not JObject node) continue;
                    if (node[nameof(UANode.References)] is not JArray references) continue;

                    foreach (var reference in references.OfType<JObject>())
                    {
                        if (reference.Value<string>(nameof(Model.Reference.ReferenceTypeId)) != HasSubtype) continue;
                        if (reference.Value<bool?>(nameof(Model.Reference.IsForward)) != false) continue;

                        var superType = reference.Value<string>(nameof(Model.Reference.TargetId));

                        if (superType != null && position.TryGetValue(superType, out var at) && at > ii)
                        {
                            m_violations.Add(new OrderViolation("SuperTypeOrder",
                                Describe(Join(file, $"{nameof(UANodeSet.Nodes)}.{container}[{ii}]")),
                                $"{node.Value<string>(nameof(UANode.BrowseName))} precedes its supertype {superType}."));
                        }
                    }
                }
            }
        }
    }

    #endregion

    #region Enumeration

    private static IEnumerable<(JObject Node, string Location)> EnumerateTopLevel(JObject root, string file)
        => EnumerateTopLevelWithContainer(root, file).Select(x => (x.Node, x.Location));

    /// <summary>Top-level Nodes in the order the document lists the containers, not the schema's.</summary>
    private static IEnumerable<(JObject Node, string Location, string Container)> EnumerateTopLevelWithContainer(JObject root, string file)
    {
        if (root[nameof(UANodeSet.Nodes)] is not JObject nodes) yield break;

        foreach (var container in nodes.Properties())
        {
            if (container.Value is not JArray items) continue;

            for (int ii = 0; ii < items.Count; ii++)
            {
                if (items[ii] is JObject node)
                {
                    yield return (node, Join(file, $"{nameof(UANodeSet.Nodes)}.{container.Name}[{ii}]"), container.Name);
                }
            }
        }
    }

    private static IEnumerable<(JObject Child, string Location)> EnumerateChildren(JObject children, string location)
    {
        foreach (var bucket in new[] { nameof(ChildList.Objects), nameof(ChildList.Variables), nameof(ChildList.Methods) })
        {
            if (children[bucket] is not JArray items) continue;

            for (int ii = 0; ii < items.Count; ii++)
            {
                if (items[ii] is JObject child)
                {
                    yield return (child, Join(location, $"{bucket}[{ii}]"));
                }
            }
        }
    }

    /// <summary>Walks every Node in the document so property order is checked on all of them.</summary>
    private static void CheckAllNodes(JObject root, string file, List<OrderViolation> violations)
    {
        if (root[nameof(UANodeSet.Nodes)] is JObject nodes)
        {
            CheckObject(nodes, typeof(UANodeSetNodes), Join(file, nameof(UANodeSet.Nodes)), violations);
        }

        foreach (var (node, location) in EnumerateTopLevel(root, file))
        {
            CheckNode(node, location, violations);
        }
    }

    #endregion

    private static Dictionary<string, byte[]> ReadArchive(string filePath)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using var fs = File.OpenRead(filePath);
        using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        TarEntry? entry;

        while ((entry = tar.GetNextEntry()) != null)
        {
            if (entry.DataStream == null) continue;

            using var ms = new MemoryStream();
            entry.DataStream.CopyTo(ms);
            entries[entry.Name] = ms.ToArray();
        }

        return entries;
    }

    private static string Text(byte[] bytes) => new System.Text.UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

    private static string Join(string location, string part)
        => String.IsNullOrEmpty(location) ? part : $"{location}/{part}";

    private static string Describe(string location) => String.IsNullOrEmpty(location) ? "(document)" : location;
}
