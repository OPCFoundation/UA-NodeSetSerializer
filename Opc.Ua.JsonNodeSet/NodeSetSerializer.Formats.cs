using System.IO;

namespace NodeSetTool
{
    /// <summary>
    /// Registration and dispatch for encodings contributed by other assemblies. See
    /// <see cref="INodeSetFormat"/> for the contract and why registration is explicit.
    /// </summary>
    public partial class NodeSetSerializer
    {
        // Registered once at startup by the host and read on every operation thereafter. A plain
        // list rather than a dictionary: registration order is the resolution order, and the
        // collection is small enough that a scan costs nothing next to parsing a NodeSet.
        private static readonly List<INodeSetFormat> s_formats = new();
        private static readonly object s_formatsLock = new();

        /// <summary>
        /// Attaches an encoding. Call once per format during host startup, before any serializer
        /// is used. Registering the same <see cref="INodeSetFormat.Id"/> twice replaces the
        /// earlier registration, so a host may override a format it also references.
        /// </summary>
        public static void AddFormat(INodeSetFormat format)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));

            lock (s_formatsLock)
            {
                s_formats.RemoveAll(f => string.Equals(f.Id, format.Id, StringComparison.OrdinalIgnoreCase));
                s_formats.Add(format);
            }
        }

        /// <summary>Encodings attached with <see cref="AddFormat"/>, in registration order.</summary>
        public static IReadOnlyCollection<INodeSetFormat> RegisteredFormats
        {
            get { lock (s_formatsLock) { return s_formats.ToArray(); } }
        }

        private static INodeSetFormat[] Formats()
        {
            lock (s_formatsLock) { return s_formats.ToArray(); }
        }

        private static INodeSetFormat? FindFormat(string id) =>
            Array.Find(Formats(), f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

        // ---- dispatch hooks consumed by Load / SaveAsFormat -------------------------------------

        private bool TryLoadRegisteredByExtension(string filePath)
        {
            foreach (var format in Formats())
            {
                if (format.CanRead && format.MatchesExtension(filePath))
                {
                    format.Read(filePath, this);
                    return true;
                }
            }

            return false;
        }

        private bool TryLoadRegisteredByContent(string filePath)
        {
            foreach (var format in Formats())
            {
                // A sniff is a guess over arbitrary bytes; a format that throws on a file it does
                // not recognize must not abort the whole dispatch chain.
                try
                {
                    if (format.CanRead && format.MatchesContent(filePath))
                    {
                        format.Read(filePath, this);
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Not this format's file. Keep looking.
                }
            }

            return false;
        }

        private bool TrySaveRegistered(string format, Stream stream, int maxNodesPerFile)
        {
            var handler = FindFormat(format);

            if (handler == null || !handler.CanWrite)
            {
                return false;
            }

            handler.Write(this, stream, maxNodesPerFile);
            return true;
        }
    }
}
