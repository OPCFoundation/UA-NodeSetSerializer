namespace Opc.Ua
{
    /// <summary>
    /// Minimal replacement for OPCFoundation ServiceMessageContext.
    /// Holds the namespace and server URI tables used when parsing/formatting NodeIds.
    /// </summary>
    internal class ServiceMessageContext
    {
        public StringTable NamespaceUris { get; } = new();
        public StringTable ServerUris { get; } = new();
    }

    /// <summary>
    /// An ordered table of URI strings. Index 0 is implicitly "http://opcfoundation.org/UA/"
    /// and is never stored in the table.
    /// </summary>
    internal class StringTable
    {
        private readonly List<string> _table = new();

        /// <summary>
        /// Returns the index for the given URI, appending it if not found.
        /// Index 0 is reserved for the OPC UA namespace and is not stored.
        /// </summary>
        private const string OpcUaCoreNamespace = "http://opcfoundation.org/UA/";

        public int GetIndexOrAppend(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                return 0;

            // Index 0 is reserved for the OPC UA core namespace — never store it
            if (uri == OpcUaCoreNamespace)
                return 0;

            for (int i = 0; i < _table.Count; i++)
            {
                if (_table[i] == uri)
                    return i + 1;
            }

            _table.Add(uri);
            return _table.Count;
        }

        /// <summary>
        /// Gets the URI for the given index. Index 0 returns null.
        /// </summary>
        public string? GetString(int index)
        {
            if (index <= 0 || index > _table.Count)
                return null;
            return _table[index - 1];
        }

        /// <summary>
        /// Gets the 1-based index for the given URI, or -1 if not found.
        /// </summary>
        public int GetIndex(string uri)
        {
            for (int i = 0; i < _table.Count; i++)
            {
                if (_table[i] == uri)
                    return i + 1;
            }
            return -1;
        }

        /// <summary>
        /// Returns the stored URIs as an array (excludes the implicit index-0 namespace).
        /// </summary>
        public string[]? ToArray() => _table.Count > 0 ? _table.ToArray() : null;
    }
}
