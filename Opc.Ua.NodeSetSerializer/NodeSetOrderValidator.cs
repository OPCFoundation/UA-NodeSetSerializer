using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Opc.Ua.NodeSetSerializer.Model;

namespace Opc.Ua.NodeSetSerializer;

/// <summary>Something about the file that breaks an Annex I.2 rule.</summary>
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
    public OrderReport(string filePath, int nodeCount, int declarationCount, IReadOnlyList<OrderViolation> violations)
    {
        FilePath = filePath;
        NodeCount = nodeCount;
        DeclarationCount = declarationCount;
        Violations = violations;
    }

    public string FilePath { get; }

    public int NodeCount { get; }

    /// <summary>Stubs pre-registering a Node the document defines later. Always zero for JSON, which
    /// has no declarations — see the JSONL checker for the layout that does.</summary>
    public int DeclarationCount { get; }

    public IReadOnlyList<OrderViolation> Violations { get; }

    public bool Compliant => Violations.Count == 0;
}

/// <summary>
/// Checks that a JSON NodeSet writes its fields in the normative order, reading the document as
/// written rather than as deserialized — field order survives only in the raw JSON.
///
/// <para>The one rule checked is <b>PropertyOrder</b>: every object's fields appear in the order the
/// schema defines, and no object carries a field the schema does not. The expected order is taken
/// from the model classes themselves (their <c>JsonProperty(Order)</c> attributes), so it cannot
/// drift from what the writer emits.</para>
///
/// <para>Node <i>order</i> is not checked here, because a JSON NodeSet makes no promise about it.
/// The eight NodeContainer properties are the shape of the document, not a decoding order: a reader
/// loads the whole document and resolves NodeIds afterwards, so nothing has to read backwards. The
/// layout that does promise sequential decodability is JSONL, and it is checked in its own terms by
/// the JSONL assembly — see <c>JsonlOrderValidator</c>.</para>
///
/// <para>XML input is rejected: field order is a property of the JSON encoding.</para>
/// </summary>
public static class NodeSetOrderValidator
{
    private static readonly IContractResolver s_resolver = new DefaultContractResolver();

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
    /// True when the file is one of the encodings this checker applies to.
    ///
    /// <para>The JSONL layout is excluded. Its Nodes are one per line rather than nested, and the
    /// checker below reads a whole document into a JObject, which is the one thing that layout exists
    /// to avoid — so it is checked in its own terms by <c>JsonlOrderValidator</c>.</para>
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

    /// <summary>Checks a .json document.</summary>
    public static OrderReport Validate(string filePath)
    {
        var root = JObject.Parse(File.ReadAllText(filePath));
        var violations = new List<OrderViolation>();

        CheckObject(root, typeof(UANodeSet), "", violations);
        CheckModels(root, "", violations);
        CheckAllNodes(root, "", violations);

        return new OrderReport(filePath, CountNodes(root), 0, violations);
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

    #region Enumeration

    /// <summary>Top-level Nodes in the order the document lists the containers, not the schema's.</summary>
    private static IEnumerable<(JObject Node, string Location)> EnumerateTopLevel(JObject root, string file)
    {
        if (root[nameof(UANodeSet.Nodes)] is not JObject nodes) yield break;

        foreach (var container in nodes.Properties())
        {
            if (container.Value is not JArray items) continue;

            for (int ii = 0; ii < items.Count; ii++)
            {
                if (items[ii] is JObject node)
                {
                    yield return (node, Join(file, $"{nameof(UANodeSet.Nodes)}.{container.Name}[{ii}]"));
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

    /// <summary>Every Node in the document, nested ones included — reported, not checked.</summary>
    private static int CountNodes(JObject root)
    {
        var count = 0;

        foreach (var (node, _) in EnumerateTopLevel(root, "")) count += Count(node);

        return count;

        static int Count(JObject node)
        {
            var count = 1;

            if (node[nameof(UANode.Children)] is JObject children)
            {
                foreach (var (child, _) in EnumerateChildren(children, "")) count += Count(child);
            }

            return count;
        }
    }

    #endregion

    private static string Join(string location, string part)
        => String.IsNullOrEmpty(location) ? part : $"{location}/{part}";

    private static string Describe(string location) => String.IsNullOrEmpty(location) ? "(document)" : location;
}
