using System.Globalization;
using Newtonsoft.Json.Linq;
using Opc.Ua.NodeSetSerializer;
using Json = Opc.Ua.NodeSetSerializer.Model;

namespace NodeSetTool
{
    /// <summary>
    /// The Value half of <see cref="NodeSetSerializer.Compare(NodeSetSerializer)"/>: the Variant a
    /// Variable or a VariableType carries, compared as the information it holds rather than as the
    /// shape one encoding happens to write it in.
    ///
    /// <para>What the two encodings are allowed to disagree on:</para>
    /// <list type="bullet">
    ///   <item><b>An ExtensionObject's TypeId.</b> The XML encoding names the DataTypeEncoding Node
    ///     ("Default XML", "Default Binary"); the JSON one names the DataType itself. Either may
    ///     come back out of a conversion, so XML (with the XML encoding) → JSON (with the DataType)
    ///     → XML (with the DataType) compares equal. Resolved through the HasEncoding References in
    ///     the two documents, falling back to the embedded Core NodeSet for the standard DataTypes,
    ///     whose encodings no model file restates.</item>
    ///   <item><b>Structure field order</b>, which is a property of the encoding and not of the
    ///     value: fields are paired by name.</item>
    ///   <item><b>The shape a schema-free read produces.</b> Reading XML without the
    ///     DataTypeDefinition to hand cannot tell <c>12</c> from <c>"12"</c>, a one-element array
    ///     from a scalar, or a NodeId/LocalizedText field from a nested structure — see
    ///     <see cref="AddressSpace.ResolveVariants"/>, which is what removes the ambiguity when an
    ///     AddressSpace is available. A comparison that insisted on the resolved shape would report
    ///     a difference that is not in the document.</item>
    /// </list>
    ///
    /// <para>What it does not tolerate: a different number of array elements, a field on one side
    /// only, a different scalar, or a TypeId that resolves to a different DataType.</para>
    /// </summary>
    public partial class NodeSetSerializer
    {
        private const string HasEncodingReferenceTypeId = "i=38";

        /// <summary>
        /// Encoding NodeId → the DataType it encodes, for the two documents being compared. Built
        /// once per <see cref="Compare(NodeSetSerializer)"/>.
        /// </summary>
        private Dictionary<string, string>? m_encodingOwners;

        /// <summary>
        /// The same map for the Core NodeSet bundled with this assembly. Built on the first TypeId
        /// that the two documents cannot resolve between them and kept for the process, so a
        /// comparison that never meets a mismatched TypeId never pays for it.
        /// </summary>
        private static readonly Lazy<Dictionary<string, string>> s_coreEncodingOwners = new(() =>
        {
            var core = new NodeSetSerializer();
            using var stream = CoreNodeSetLoader.OpenEmbeddedNodeSet();
            core.LoadXml(stream);
            return EncodingOwners(core.m_sequence!);
        });

        private static Dictionary<string, string> EncodingOwners(params IEnumerable<Json.UANode>[] documents)
        {
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var document in documents)
            {
                foreach (var (sourceId, typeId, targetId) in ReferenceEdges(document))
                {
                    if (typeId == HasEncodingReferenceTypeId) owners[targetId] = sourceId;
                }
            }

            return owners;
        }

        /// <summary>The Value of the two Node classes that have one.</summary>
        private static Json.Variant? ValueOf(Json.UANode node) => node switch
        {
            Json.UAVariable variable => variable.Value,
            Json.UAVariableType variableType => variableType.Value,
            _ => null,
        };

        private static bool IsEmpty(Json.Variant? value) => value?.Value == null;

        private bool CompareValues(Json.UANode original, Json.UANode target)
        {
            var left = ValueOf(original);
            var right = ValueOf(target);

            if (IsEmpty(left) || IsEmpty(right))
            {
                // A Node with no Value and a Node with an empty one hold the same nothing.
                if (IsEmpty(left) && IsEmpty(right)) return true;

                m_errors.Add(new CompareError(original, "Value", Summarize(left), Summarize(right)));
                return false;
            }

            // Only when both sides state one: the JSON encodings carry UaType explicitly while a
            // schema-free XML read infers it, and an absent one is not a disagreement.
            if (left!.UaType != null && right!.UaType != null && left.UaType != right.UaType)
            {
                m_errors.Add(new CompareError(original, "Value.UaType", left.UaType, right.UaType));
                return false;
            }

            if (left.Dimensions != null && right!.Dimensions != null
                && !left.Dimensions.SequenceEqual(right.Dimensions))
            {
                m_errors.Add(new CompareError(original, "Value.Dimensions",
                    String.Join(",", left.Dimensions), String.Join(",", right.Dimensions)));
                return false;
            }

            var leftValue = Normalize(left.Value);
            var rightValue = Normalize(right!.Value);

            // XSD base64Binary ignores whitespace and an XML NodeSet wraps a long ByteString across
            // lines, so the same bytes arrive line-wrapped from one encoding and flat from the other.
            if ((left.UaType ?? right.UaType) == (int)Json.BuiltInType.ByteString)
            {
                leftValue = WithoutWhitespace(leftValue);
                rightValue = WithoutWhitespace(rightValue);
            }

            if (ValuesEqual(leftValue, rightValue)) return true;

            m_errors.Add(new CompareError(original, "Value", Summarize(left), Summarize(right)));
            return false;
        }

        /// <summary>
        /// The Variant's Value as JSON. A Value is a Part 6 JSON DOM already, except for the
        /// ExtensionObject and the array, which are held as objects.
        /// </summary>
        private static JToken Normalize(object? value)
        {
            switch (value)
            {
                case null: return JValue.CreateNull();
                case JToken token: return token;
                case Json.ExtensionObject eo: return JToken.FromObject(eo);
                case string s: return new JValue(s);
                // A ByteString is its base64 text, not a list of 8-bit numbers.
                case byte[] bytes: return new JValue(Convert.ToBase64String(bytes));
                case System.Collections.IEnumerable items:
                    {
                        var array = new JArray();
                        foreach (var item in items) array.Add(Normalize(item));
                        return array;
                    }
                default: return new JValue(value);
            }
        }

        /// <summary>Every string in the token with its whitespace removed. See the ByteString case.</summary>
        private static JToken WithoutWhitespace(JToken token)
        {
            switch (token)
            {
                case JArray array:
                    {
                        var stripped = new JArray();
                        foreach (var item in array) stripped.Add(WithoutWhitespace(item));
                        return stripped;
                    }

                case JValue { Value: string text }:
                    return new JValue(new string(text.Where(c => !Char.IsWhiteSpace(c)).ToArray()));

                default:
                    return token;
            }
        }

        private bool ValuesEqual(JToken left, JToken right)
        {
            if (IsNothing(left) || IsNothing(right)) return IsNothing(left) && IsNothing(right);

            // An empty array and an empty scalar. <Field></Field> read without the field's ValueRank
            // is the empty string; with it, no items at all. Same "no items" either way.
            if (left is JArray { Count: 0 } && IsEmptyText(right)) return true;
            if (right is JArray { Count: 0 } && IsEmptyText(left)) return true;

            // A one-element array and a scalar. <Field>x</Field> read without the field's ValueRank
            // is a scalar; with it, an array of one. Same value either way.
            if (left is JArray oneLeft && right is not JArray && oneLeft.Count == 1)
                return ValuesEqual(oneLeft[0], right);
            if (right is JArray oneRight && left is not JArray && oneRight.Count == 1)
                return ValuesEqual(left, oneRight[0]);

            if (left is JArray leftArray && right is JArray rightArray)
            {
                if (leftArray.Count != rightArray.Count) return false;

                for (var ii = 0; ii < leftArray.Count; ii++)
                {
                    if (!ValuesEqual(leftArray[ii], rightArray[ii])) return false;
                }

                return true;
            }

            if (left is JObject leftObject && right is JObject rightObject)
                return ObjectsEqual(leftObject, rightObject);

            // One side read with the schema and the other without it.
            if (left is JObject leftWrapper && Unwrap(leftWrapper) is { } unwrappedLeft)
                return ValuesEqual(unwrappedLeft, right);
            if (right is JObject rightWrapper && Unwrap(rightWrapper) is { } unwrappedRight)
                return ValuesEqual(left, unwrappedRight);

            return ScalarsEqual(left, right);
        }

        private bool ObjectsEqual(JObject left, JObject right)
        {
            var leftTypeId = left["UaTypeId"]?.ToString();
            var rightTypeId = right["UaTypeId"]?.ToString();

            if ((leftTypeId != null || rightTypeId != null) && !TypeIdsEqual(leftTypeId, rightTypeId))
                return false;

            foreach (var name in left.Properties().Select(p => p.Name)
                .Union(right.Properties().Select(p => p.Name), StringComparer.Ordinal))
            {
                if (name == "UaTypeId") continue;

                // Absent and 0 are the same encoding (JSON inline, body fields spread into the
                // wrapper), so they are compared as the number they stand for.
                if (name == "UaEncoding")
                {
                    if (EncodingOf(left) != EncodingOf(right)) return false;
                    continue;
                }

                if (!ValuesEqual(left[name] ?? JValue.CreateNull(), right[name] ?? JValue.CreateNull()))
                    return false;
            }

            return true;
        }

        private static byte EncodingOf(JObject wrapper) =>
            wrapper["UaEncoding"] is JValue value && value.Value != null
            && byte.TryParse(value.Value.ToString(), out var encoding) ? encoding : (byte)0;

        /// <summary>
        /// Two ExtensionObject TypeIds name the same type when they are the same NodeId, or when
        /// they resolve to the same DataType — which is what an encoding NodeId and the DataType it
        /// encodes do. See the remarks on this class.
        /// </summary>
        private bool TypeIdsEqual(string? left, string? right)
        {
            if (String.Equals(left, right, StringComparison.Ordinal)) return true;
            if (left == null || right == null) return false;

            return String.Equals(EncodedDataType(left), EncodedDataType(right), StringComparison.Ordinal);
        }

        private string EncodedDataType(string nodeId)
        {
            if (m_encodingOwners != null && m_encodingOwners.TryGetValue(nodeId, out var owner)) return owner;
            if (s_coreEncodingOwners.Value.TryGetValue(nodeId, out var coreOwner)) return coreOwner;
            return nodeId;
        }

        /// <summary>
        /// A schema-free XML read cannot tell a NodeId or a LocalizedText field from a nested
        /// structure, so it produces <c>{Identifier: "i=290"}</c> / <c>{Text: "..."}</c> where the
        /// schema-aware side has the bare string. Returns the string those two shapes stand for,
        /// and null for an object that means something else — a LocalizedText that also states a
        /// Locale says more than a bare string does and is not unwrapped.
        /// </summary>
        private static JToken? Unwrap(JObject wrapper)
        {
            if (wrapper.Count != 1) return null;

            var only = wrapper.Properties().First();
            return only.Name is "Identifier" or "Text" ? only.Value : null;
        }

        private static bool IsEmptyText(JToken token) =>
            token is JValue { Value: string text } && String.IsNullOrWhiteSpace(text);

        private static bool IsNothing(JToken? token) =>
            token == null || token.Type is JTokenType.Null or JTokenType.Undefined;

        private static bool ScalarsEqual(JToken left, JToken right)
        {
            if (left.Type == JTokenType.Boolean || right.Type == JTokenType.Boolean)
            {
                if (TryBoolean(left, out var leftBool) && TryBoolean(right, out var rightBool))
                    return leftBool == rightBool;
                return false;
            }

            // A .json file's reader turns an ISO timestamp into a DateTime while the XML one leaves
            // it a string, so the two sides of the same instant arrive as different token types.
            if (left.Type == JTokenType.Date || right.Type == JTokenType.Date)
            {
                if (TryDateTime(left, out var leftDate) && TryDateTime(right, out var rightDate))
                    return leftDate == rightDate;
                return false;
            }

            if (left.Type == JTokenType.Bytes || right.Type == JTokenType.Bytes)
                return String.Equals(Base64(left), Base64(right), StringComparison.Ordinal);

            // Part 6 writes Int64/UInt64 as JSON strings, and a schema-free read leaves every XML
            // scalar a string or a heuristically typed number, so a number and its decimal text are
            // the same value. Decimal first: it holds UInt64.MaxValue exactly, which double does not.
            if (TryDecimal(left, out var leftDecimal) && TryDecimal(right, out var rightDecimal))
                return leftDecimal == rightDecimal;

            if (TryDouble(left, out var leftDouble) && TryDouble(right, out var rightDouble))
                return leftDouble.Equals(rightDouble);

            return String.Equals(Text(left), Text(right), StringComparison.Ordinal);
        }

        private static bool TryBoolean(JToken token, out bool result)
        {
            if (token is JValue { Value: bool value }) { result = value; return true; }
            return Boolean.TryParse(Text(token), out result);
        }

        private static bool TryDateTime(JToken token, out DateTime result)
        {
            if (token is JValue { Value: DateTimeOffset offset }) { result = offset.UtcDateTime; return true; }

            if (token is JValue { Value: DateTime value })
            {
                // Part 6 §5.4.2.5 and the XML encoding both write a UtcTime, so a timestamp that
                // states no offset is UTC and not local — which is what DateTime.ToUniversalTime
                // would read an Unspecified Kind as.
                result = value.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
                    : value.ToUniversalTime();
                return true;
            }

            if (DateTimeOffset.TryParse(Text(token), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            {
                result = parsed.UtcDateTime;
                return true;
            }

            result = default;
            return false;
        }

        private static bool TryDecimal(JToken token, out decimal result)
        {
            if (token is JValue { Value: { } value })
            {
                if (value is decimal exact) { result = exact; return true; }

                if (value is not string && value is IConvertible and not bool and not DateTime)
                {
                    try { result = Convert.ToDecimal(value, CultureInfo.InvariantCulture); return true; }
                    catch (Exception e) when (e is OverflowException or InvalidCastException or FormatException) { }
                }
            }

            return Decimal.TryParse(Text(token), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryDouble(JToken token, out double result)
        {
            if (token is JValue { Value: { } value } && value is not string
                && value is IConvertible and not bool and not DateTime)
            {
                try { result = Convert.ToDouble(value, CultureInfo.InvariantCulture); return true; }
                catch (Exception e) when (e is OverflowException or InvalidCastException or FormatException) { }
            }

            return Double.TryParse(Text(token), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private static string Base64(JToken token) =>
            token is JValue { Value: byte[] bytes } ? Convert.ToBase64String(bytes) : Text(token);

        private static string Text(JToken token) =>
            token is JValue value
                ? Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? ""
                : token.ToString(Newtonsoft.Json.Formatting.None);

        // A Value can be a whole structure, and the report is one line.
        private const int MaxValueTextLength = 120;

        private static string Summarize(Json.Variant? value)
        {
            if (IsEmpty(value)) return "no Value";

            var text = Normalize(value!.Value).ToString(Newtonsoft.Json.Formatting.None);
            return text.Length <= MaxValueTextLength ? text : text[..MaxValueTextLength] + "...";
        }
    }
}
