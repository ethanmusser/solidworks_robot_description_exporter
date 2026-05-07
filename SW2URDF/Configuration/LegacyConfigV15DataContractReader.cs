using SW2URDF.URDF;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace SW2URDF.Configuration
{
    /// <summary>
    /// Backward-reader for the v1.3-v1.5 DataContract XML configuration stored
    /// under "URDF Export Configuration (v1.5)" SolidWorks attributes. It keeps
    /// legacy XML deserialization in the Configuration namespace while Phase 2
    /// moves the active save format to ConfigV2 JSON.
    /// </summary>
    public static class LegacyConfigV15DataContractReader
    {
        public static Link ReadLink(string xml)
        {
            using (MemoryStream stream = new MemoryStream(Encoding.ASCII.GetBytes(xml ?? "")))
            {
                DataContractSerializer serializer = new DataContractSerializer(typeof(Link));
                Link link = (Link)serializer.ReadObject(stream);
                return link.Clone();
            }
        }

        public static LinkNode ReadBaseNode(string xml)
        {
            Link link = ReadLink(xml);
            return new LinkNode(link);
        }
    }
}
