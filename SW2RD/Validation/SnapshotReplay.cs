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

using System;
using System.IO;
using System.Xml;
using SW2RD.Input;
using SW2RD.MJCF;
using SW2RD.URDF;

namespace SW2RD.Validation
{
    /// <summary>
    /// Replays an <see cref="ExportSnapshot"/> through the appropriate format
    /// writer, SW-free, to produce the same XML a live export would. Used by the
    /// golden tests (compare against committed <c>expected.*</c>) and by the
    /// regeneration tool (overwrite <c>expected.*</c> when a writer change is
    /// intentional).
    /// </summary>
    internal static class SnapshotReplay
    {
        public const string UrdfFormat = "URDF";
        public const string MjcfFormat = "MJCF";

        /// <summary>
        /// Renders the snapshot to an indented XML string using the same writer
        /// the export pipeline uses.
        /// </summary>
        public static string Render(ExportSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (snapshot.Tree == null)
            {
                throw new InvalidOperationException("Snapshot has no KinematicTree to render.");
            }

            XmlWriterSettings settings = new XmlWriterSettings { Indent = true };
            using (StringWriter stringWriter = new StringWriter())
            {
                using (XmlWriter writer = XmlWriter.Create(stringWriter, settings))
                {
                    if (string.Equals(snapshot.Format, MjcfFormat, StringComparison.OrdinalIgnoreCase))
                    {
                        MJCFModel model = MJCFBuilder.Build(
                            snapshot.Tree,
                            string.IsNullOrEmpty(snapshot.MeshDir) ? "../meshes/" : snapshot.MeshDir,
                            snapshot.Auxiliary,
                            snapshot.MjcfRotationFormat,
                            snapshot.MjcfAngleUnit);
                        model.WriteMJCF(writer);
                    }
                    else
                    {
                        URDFBuilder.Write(snapshot.Tree, writer);
                    }
                }
                return stringWriter.ToString();
            }
        }
    }
}
