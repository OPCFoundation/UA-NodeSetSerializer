namespace Opc.Ua.JsonNodeSet
{
    /// <summary>
    /// Validation and IRI encoding for canonical OPC UA NodeId / URI text.
    /// See <c>canonical-nodeid-encoding.md</c> for the full specification.
    /// </summary>
    public static class CanonicalUri
    {
        /// <summary>
        /// Encodes the UTF-8 bytes of <paramref name="text"/> as RFC 4648 §5 base64url
        /// (no padding). Used to make a canonical NodeId safe as an IRI local-part.
        /// </summary>
        public static string ToBase64Url(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Decodes an RFC 4648 §5 base64url string (with or without padding) to a UTF-8 string.
        /// </summary>
        public static string FromBase64Url(string base64Url)
        {
            if (base64Url == null) throw new ArgumentNullException(nameof(base64Url));
            var b64 = base64Url.Replace('-', '+').Replace('_', '/');
            switch (b64.Length % 4)
            {
                case 0: break;
                case 2: b64 += "=="; break;
                case 3: b64 += "="; break;
                default: throw new FormatException("Invalid base64url length.");
            }
            var bytes = Convert.FromBase64String(b64);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// True iff <paramref name="uri"/> is a valid URI under the spec:
        /// well-formed percent-encoding (valid <c>%HH</c> accepted, a bare or truncated
        /// <c>%</c> rejected), no <c>;</c>, ASCII only, RFC 3986, scheme is http/https/urn.
        /// </summary>
        public static bool IsValid(string? uri)
        {
            return TryGetInvalidReason(uri) == null;
        }

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        /// <summary>
        /// Throws <see cref="InvalidNodeSetUriException"/> if <paramref name="uri"/>
        /// is not a valid URI per the spec.
        /// </summary>
        /// <param name="uri">URI to validate.</param>
        /// <param name="location">Optional source-location hint for the error message.</param>
        public static void Validate(string? uri, string? location = null)
        {
            if (TryGetInvalidReason(uri) != null)
            {
                throw new InvalidNodeSetUriException(uri ?? string.Empty, location);
            }
        }

        private static string? TryGetInvalidReason(string? uri)
        {
            if (string.IsNullOrEmpty(uri)) return "empty";

            // Percent-encoding must already be well-formed: a '%' must be followed by two hex
            // digits. Valid "%HH" is accepted (URIs are expected to arrive correctly encoded);
            // a bare or truncated '%' is rejected as under-encoded.
            for (int i = 0; i < uri.Length; i++)
            {
                if (uri[i] != '%') continue;
                if (i + 2 >= uri.Length || !IsHexDigit(uri[i + 1]) || !IsHexDigit(uri[i + 2]))
                    return "malformed percent-encoding";
            }

            // ';' separates the URI from the identifier in canonical NodeId syntax
            // (nsu=URI;identifier). A literal ';' inside a URI is therefore disallowed here; the
            // import boundary is responsible for normalizing it to "__%3B" (deferred follow-up).
            if (uri.IndexOf(';') >= 0) return "contains ;";

            if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            {
                return "not absolute";
            }

            var scheme = parsed.Scheme;
            if (!string.Equals(scheme, "http", StringComparison.Ordinal) &&
                !string.Equals(scheme, "https", StringComparison.Ordinal) &&
                !string.Equals(scheme, "urn", StringComparison.Ordinal))
            {
                return "unsupported scheme";
            }

            // Reject any character that .NET silently normalized (would break the
            // "preserve bytes exactly as authored" guarantee). Uri.OriginalString is
            // the byte-faithful copy; anything else means non-ASCII or controls
            // slipped in.
            foreach (var c in uri)
            {
                if (c < 0x20 || c == 0x7F) return "contains control character";
                if (c > 0x7E) return "non-ASCII";
            }

            return null;
        }
    }

    /// <summary>
    /// Thrown when a URI in a NodeSet (or in an in-memory model being serialized)
    /// fails canonical-form validation. See <c>canonical-nodeid-encoding.md</c>.
    /// </summary>
    public class InvalidNodeSetUriException : Exception
    {
        public InvalidNodeSetUriException(string uri, string? location)
            : base(BuildMessage(uri, location))
        {
            Uri = uri;
            Location = location;
        }

        /// <summary>The exact rejected URI string, byte-for-byte from the source.</summary>
        public string Uri { get; }

        /// <summary>Optional source-location hint (file + structural path + line number).</summary>
        public string? Location { get; }

        private static string BuildMessage(string uri, string? location)
        {
            var msg = $"URI \"{uri}\" is not a valid URI.";
            if (!string.IsNullOrEmpty(location))
            {
                msg += $" Location: {location}";
            }
            return msg;
        }
    }
}
