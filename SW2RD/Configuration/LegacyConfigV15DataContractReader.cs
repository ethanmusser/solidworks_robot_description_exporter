/*
Copyright (c) 2026 Ethan J. Musser

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using SW2RD.URDF;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace SW2RD.Configuration
{
    /// <summary>
    /// Backward-reader for the v1.3-v1.5 DataContract XML configuration stored
    /// under "URDF Export Configuration (v1.5)" SolidWorks attributes. It keeps
    /// legacy XML deserialization in the Configuration namespace while active
    /// saves use Config JSON.
    ///
    /// Legacy single-link trees are migrated on read into the
    /// WorldNode-rooted shape: the global
    /// origin coord-sys is lifted from the old base link onto a synthesized
    /// WorldNode, the old base link is demoted to a single Welded top-level
    /// body, and world-level visual / collision / sites start empty.
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

        /// <summary>
        /// Reads the legacy XML and returns the root of the migrated tree -
        /// a <see cref="WorldNode"/> with one Welded top-level body wrapping
        /// the legacy base link. The world's global-origin coord-sys is
        /// inherited from the legacy base link's joint coord-sys so the
        /// existing STL anchor / LocalizeJoint behavior is preserved.
        /// </summary>
        public static LinkNode ReadBaseNode(string xml)
        {
            Link link = ReadLink(xml);
            return WrapLegacyBaseLinkInWorldNode(link);
        }

        /// <summary>
        /// Promotes a legacy single-tree base Link into a WorldNode-rooted
        /// LinkNode tree. The world inherits the legacy base link's joint
        /// coord-sys as its global origin (identity world->body offset, so
        /// MJCF emits SuppressTransform=true and today's output is preserved
        /// byte-for-byte). Exposed as `internal` so the equivalent migration
        /// in <c>SerialNode.BuildLinkNodeFromSerialNode</c> can share it.
        /// </summary>
        internal static WorldNode WrapLegacyBaseLinkInWorldNode(Link legacyBaseLink)
        {
            WorldNode worldNode = new WorldNode();
            string globalOrigin = legacyBaseLink?.Joint?.CoordinateSystemName ?? "";
            worldNode.GlobalOriginCoordinateSystemName = globalOrigin;

            if (legacyBaseLink == null)
            {
                return worldNode;
            }

            legacyBaseLink.Parent = worldNode.Link;
            legacyBaseLink.WorldAttachment = SW2RD.Core.WorldAttachmentModel.Welded;
            worldNode.Link.Children.Add(legacyBaseLink);
            worldNode.Nodes.Add(new LinkNode(legacyBaseLink));
            return worldNode;
        }
    }
}
