using System.IO;

namespace NodeSetTool
{
    /// <summary>
    /// A NodeSet encoding contributed by another assembly.
    /// <para>
    /// The built-in encodings (XML, JSON, and the compressed archive) are implemented directly on
    /// <see cref="NodeSetSerializer"/>. Everything else — the prototype encodings of draft
    /// specifications — ships as its own assembly and is attached at startup with
    /// <see cref="NodeSetSerializer.AddFormat"/>. There is no probing or dynamic loading: a host
    /// that wants an encoding references the assembly and registers it, and a host that does not
    /// simply never mentions it.
    /// </para>
    /// <para>
    /// Implementations must be stateless and safe to share: one instance is registered for the
    /// lifetime of the process and used for every operation, so per-operation state belongs in a
    /// worker object created inside <see cref="Read"/> / <see cref="Write"/>.
    /// </para>
    /// </summary>
    public interface INodeSetFormat
    {
        /// <summary>Format name as it appears in <c>Save(format, …)</c>, e.g. <c>"jsonld"</c>.</summary>
        string Id { get; }

        /// <summary>File extension including the leading dot, e.g. <c>".jsonld"</c>.</summary>
        string FileExtension { get; }

        bool CanRead { get; }

        bool CanWrite { get; }

        /// <summary>
        /// Whether this format claims <paramref name="filePath"/> on its extension alone. Checked
        /// before the built-in extension rules for JSON and the archive, so a format may claim a
        /// compound extension such as <c>.jsonl.gz</c> without <c>.gz</c> capturing it first.
        /// </summary>
        bool MatchesExtension(string filePath);

        /// <summary>
        /// Whether the content of <paramref name="filePath"/> identifies this format, for files
        /// whose extension says nothing. Runs after the XML sniff and before the JSON sniff:
        /// a JSONL document is a sequence of JSON values rather than one, so the JSON check
        /// rejects it and it would otherwise fall through to the archive reader.
        /// Must not throw — return false for anything unreadable or unrecognized.
        /// </summary>
        bool MatchesContent(string filePath);

        /// <summary>Reads <paramref name="filePath"/> into <paramref name="target"/>.</summary>
        void Read(string filePath, NodeSetSerializer target);

        /// <summary>Writes <paramref name="source"/> to <paramref name="output"/>.</summary>
        void Write(NodeSetSerializer source, Stream output, int maxNodesPerFile);

        /// <summary>
        /// Writes <paramref name="source"/> to a file. Override only when the container depends on
        /// the file name — JSONL gzips when the path ends in <c>.gz</c>, which a caller handing over
        /// a bare stream cannot know. The default simply opens the file and calls
        /// <see cref="Write"/>.
        /// </summary>
        void WriteFile(NodeSetSerializer source, string filePath, int maxNodesPerFile)
        {
            using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
            Write(source, stream, maxNodesPerFile);
        }
    }
}
