using System.Xml;
using System.Xml.Serialization;

namespace Opc.Ua.Export
{
    public partial class UANodeSet
    {
        public static UANodeSet? Read(Stream stream)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
            var serializer = new XmlSerializer(typeof(UANodeSet));
            using var reader = XmlReader.Create(stream, settings);
            return (UANodeSet?)serializer.Deserialize(reader);
        }

        /// <param name="headerComment">
        /// Body of a comment to write between the XML declaration and the root element — the
        /// licence/copyright header, which XML carries as a comment and JSON as a structured field.
        /// </param>
        public void Write(Stream stream, string? headerComment = null)
        {
            var settings = new XmlWriterSettings
            {
                IndentChars = "  ",
                Indent = true,
                Encoding = System.Text.Encoding.UTF8,
                CloseOutput = false
            };

            var serializer = new XmlSerializer(typeof(UANodeSet));
            using var writer = XmlWriter.Create(stream, settings);

            if (!string.IsNullOrEmpty(headerComment))
            {
                writer.WriteStartDocument();
                writer.WriteComment(headerComment);
            }

            // Pre-declare the namespaces we want at the root. The `uax` prefix
            // is the OPC UA Types schema (Part 6 §5.3) used inside Variable
            // Value bodies — declaring it at the root keeps every nested
            // <uax:Boolean> / <uax:ExtensionObject> from carrying its own
            // xmlns attribute, matching the canonical NodeSet layout shipped
            // by the OPC Foundation. xsi / xsd are the standard schema
            // metadata prefixes XmlSerializer would emit anyway; including
            // them explicitly so the namespaces argument doesn't suppress
            // them.
            var ns = new XmlSerializerNamespaces();
            ns.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
            ns.Add("xsd", "http://www.w3.org/2001/XMLSchema");
            ns.Add("uax", "http://opcfoundation.org/UA/2008/02/Types.xsd");
            ns.Add("", "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd");

            serializer.Serialize(writer, this, ns);
        }
    }
}