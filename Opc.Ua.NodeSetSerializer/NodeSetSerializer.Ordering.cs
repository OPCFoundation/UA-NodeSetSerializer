using Json = Opc.Ua.JsonNodeSet.Model;

namespace NodeSetTool
{
    /// <summary>
    /// Annex I.2 Node ordering.
    ///
    /// <para>A conforming decoder reads a UANodeSet sequentially and registers each Node as soon as
    /// it has read the mandatory NodeId / NodeClass / BrowseName that open every Node object. The
    /// ordering rules exist so that, by the time a field naming another Node is read, that Node has
    /// already been registered:</para>
    ///
    /// <list type="bullet">
    /// <item>the eight NodeContainer properties put every type Node ahead of every instance Node;</item>
    /// <item>within the four type lists, a supertype precedes its subtypes (see SortBySuperType);</item>
    /// <item>a Node's children are nested inside it, so they are registered before the parent's own
    /// References are read.</item>
    /// </list>
    ///
    /// <para>The rules govern the fields that identify a Node — ParentId, TypeId, ModellingRuleId,
    /// DataType, MethodDeclarationId, RoleId and the DataType of a DataTypeDefinition field. They do
    /// not govern <see cref="Json.UANode.References"/>: a Reference names an arbitrary Node, so no
    /// emission order can make every Reference read backwards, and requiring it would declare most
    /// of the file. A Reference from a parent to its own child is treated the same as a Reference to
    /// any other Node.</para>
    ///
    /// <para>The supertype rule overrides that exemption. HasSubtype is a Reference, but the
    /// supertypes of a model form a tree rather than an arbitrary graph, so unlike the rest they
    /// can always be ordered — and are, by SortBySuperType. Being satisfied by ordering, they never
    /// need declaring, which is why the supertype exception costs nothing here.</para>
    ///
    /// <para>What no ordering can fix is a cycle — two Nodes that name each other through the fields
    /// above, or one that names another "backwards" across the fixed NodeClass partition. Those are
    /// the "recursive relationships" of the schema: the Nodes involved are pre-declared in
    /// <see cref="Json.UANodeSet.Declarations"/> as bare NodeId / NodeClass / BrowseName stubs, which
    /// registers them before the body is read. Only then may <see cref="Json.UANodeSet.Ordered"/> be
    /// set.</para>
    /// </summary>
    public partial class NodeSetSerializer
    {
        /// <summary>
        /// Returns the stubs for every in-document Node that some earlier Node names before it is
        /// defined, in document order, or null when the ordering already resolves everything.
        /// </summary>
        /// <remarks>
        /// The result holds for the flattened JSONL layout as well as the nested one. The two could
        /// only differ over a field emitted after Children that names one of the Node's own
        /// descendants — nested, the subtree is read first; flattened, it is not. The fields emitted
        /// after Children are DataType and MethodDeclarationId, and a ChildList holds only Objects,
        /// Variables and Methods, so a DataType can never be a descendant. Add a field there that
        /// can name one and the two layouts stop agreeing.
        /// </remarks>
        private static List<Json.UANode>? BuildDeclarations(Json.UANodeSet nodeset)
        {
            var index = new Dictionary<string, int>();
            var subtreeEnd = new Dictionary<string, int>();
            var sequence = new List<Json.UANode>();

            foreach (var node in EnumerateTopLevel(nodeset))
            {
                IndexSubtree(node, sequence, index, subtreeEnd);
            }

            // Keyed by NodeId so a Node named by several others is declared once.
            var forward = new HashSet<string>();

            foreach (var node in sequence)
            {
                if (node.NodeId == null) continue;

                var start = index[node.NodeId];
                var end = subtreeEnd[node.NodeId];

                // Fields emitted ahead of Children (Order < 18): only Nodes already registered when
                // this Node begins will do.
                Require(node.ParentId, start);
                Require(node.TypeId, start);
                Require(node.ModellingRuleId, start);

                if (node.RolePermissions != null)
                {
                    foreach (var permission in node.RolePermissions) Require(permission.RoleId, start);
                }

                // References are deliberately not checked. A Reference names an arbitrary Node —
                // one in another model, one further down the file, one that does not resolve at all
                // — and no emission order can make an arbitrary graph read backwards. A parent
                // referencing its own child is no different in this respect from a parent
                // referencing anything else, so it gets no special treatment and declares nothing.
                // The one Reference that is ordered, HasSubtype, is satisfied by SortBySuperType
                // placing the supertype first, so it never reaches this list either.
                switch (node)
                {
                    case Json.UAVariable variable: Require(variable.DataType, end); break;
                    case Json.UAVariableType variableType: Require(variableType.DataType, end); break;
                    case Json.UAMethod method: Require(method.MethodDeclarationId, end); break;

                    case Json.UADataType dataType:
                        {
                            if (dataType.Definition?.Fields != null)
                            {
                                foreach (var field in dataType.Definition.Fields) Require(field.DataType, end);
                            }

                            break;
                        }
                }
            }

            if (forward.Count == 0)
            {
                return null;
            }

            // Declared in document order so the list reads the same way the body does.
            return forward
                .OrderBy(id => index[id])
                .Select(id => sequence[index[id]])
                .Select(node => new Json.UANode()
                {
                    NodeId = node.NodeId,
                    NodeClass = node.NodeClass,
                    BrowseName = node.BrowseName
                })
                .ToList();

            // A target is only a forward reference when it is defined in this document (targets in
            // other models are resolved from those models, not from Declarations) and appears after
            // the point at which the referring field is read.
            void Require(string? targetId, int registeredThrough)
            {
                if (targetId == null) return;
                if (!index.TryGetValue(targetId, out var position)) return;
                if (position <= registeredThrough) return;

                forward.Add(targetId);
            }
        }

        private static void IndexSubtree(
            Json.UANode node,
            List<Json.UANode> sequence,
            Dictionary<string, int> index,
            Dictionary<string, int> subtreeEnd)
        {
            var position = sequence.Count;
            sequence.Add(node);

            if (node.NodeId != null) index[node.NodeId] = position;

            foreach (var child in EnumerateChildren(node.Children))
            {
                IndexSubtree(child, sequence, index, subtreeEnd);
            }

            if (node.NodeId != null) subtreeEnd[node.NodeId] = sequence.Count - 1;
        }

        /// <summary>The top-level Nodes in the order the eight NodeContainer properties are emitted.</summary>
        internal static IEnumerable<Json.UANode> EnumerateTopLevel(Json.UANodeSet nodeset)
        {
            if (nodeset.Nodes == null) yield break;

            foreach (var node in Each(nodeset.Nodes.ReferenceTypes)) yield return node;
            foreach (var node in Each(nodeset.Nodes.DataTypes)) yield return node;
            foreach (var node in Each(nodeset.Nodes.VariableTypes)) yield return node;
            foreach (var node in Each(nodeset.Nodes.ObjectTypes)) yield return node;
            foreach (var node in Each(nodeset.Nodes.Variables)) yield return node;
            foreach (var node in Each(nodeset.Nodes.Methods)) yield return node;
            foreach (var node in Each(nodeset.Nodes.Objects)) yield return node;
            foreach (var node in Each(nodeset.Nodes.Views)) yield return node;

            static IEnumerable<Json.UANode> Each<T>(List<T>? nodes) where T : Json.UANode
                => nodes ?? Enumerable.Empty<T>();
        }
    }
}
