using System.IO;
using System.Text;
using System.Xml;

namespace SW2RD.MJCF
{
    // Lightweight wrapper around XmlWriter that mirrors the URDFWriter pattern.
    // Owns no state of its own beyond the underlying writer: callers write each
    // element by invoking WriteMJCF on the data classes directly.
    internal class MJCFWriter
    {
        public readonly string SaveLocation;
        public XmlWriter writer;

        public MJCFWriter(string saveLocation)
        {
            SaveLocation = saveLocation;
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                NewLineOnAttributes = false,
                IndentChars = "  ",
            };
            writer = XmlWriter.Create(saveLocation, settings);
        }

        public void Close()
        {
            writer.Flush();
            writer.Close();
        }
    }
}
