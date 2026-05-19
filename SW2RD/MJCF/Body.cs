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

using System.Collections.Generic;
using System.Xml;

namespace SW2RD.MJCF
{
    // Recursive MJCF body element. The root body acts as the worldbody contents
    // (its name is conventionally the URDF base_link name) but is written inside
    // <worldbody> by MJCFModel.
    internal class Body
    {
        public string Name { get; set; }
        public double[] Position { get; set; } = new double[] { 0, 0, 0 };
        public double[] Quaternion { get; set; } = new double[] { 1, 0, 0, 0 };

        public MJCFInertial Inertial { get; set; }
        public MJCFJoint Joint { get; set; } // null for the root body or a fixed joint
        public List<Geom> Geoms { get; }
        public List<Site> Sites { get; }
        public List<Body> Children { get; }

        // If true, suppress pos/quat attributes on this <body>. This is what we do
        // for the worldbody-level emission of a top-level body whose offset from
        // the world is identity (the welded single-tree case): the worldbody
        // origin coincides with the body origin, so writing pos="0 0 0"
        // quat="1 0 0 0" would just be noise.
        public bool SuppressTransform { get; set; } = false;

        // If true, emit a <freejoint/> as the first child of this body
        // (after <inertial>). MJCF freejoints attach a body to world with
        // 6 DoF; we use it for top-level bodies marked
        // WorldAttachmentModel.Free. Mutually exclusive with
        // <see cref="Joint"/> in practice (top-level bodies have no
        // incoming kinematic joint).
        public bool HasFreeJoint { get; set; } = false;

        public Body()
        {
            Geoms = new List<Geom>();
            Sites = new List<Site>();
            Children = new List<Body>();
        }

        public void WriteMJCF(XmlWriter writer)
        {
            writer.WriteStartElement("body");
            if (!string.IsNullOrEmpty(Name))
            {
                writer.WriteAttributeString("name", Name);
            }
            if (!SuppressTransform)
            {
                writer.WriteAttributeString("pos", MJCFFormat.FormatTriple(Position));
                writer.WriteAttributeString("quat", MJCFFormat.FormatQuat(Quaternion));
            }

            // Order: inertial, freejoint, joint, geoms, sites, child bodies. This
            // mirrors the canonical MuJoCo example layout and keeps the diff
            // against URDF easy to follow. <freejoint/> goes BEFORE any
            // standard <joint> by MuJoCo convention.
            if (Inertial != null)
            {
                Inertial.WriteMJCF(writer);
            }
            if (HasFreeJoint)
            {
                writer.WriteStartElement("freejoint");
                writer.WriteEndElement();
            }
            if (Joint != null)
            {
                Joint.WriteMJCF(writer);
            }
            foreach (Geom geom in Geoms)
            {
                geom.WriteMJCF(writer);
            }
            foreach (Site site in Sites)
            {
                site.WriteMJCF(writer);
            }
            foreach (Body child in Children)
            {
                child.WriteMJCF(writer);
            }

            writer.WriteEndElement();
        }
    }
}
