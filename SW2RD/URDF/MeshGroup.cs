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

using SolidWorks.Interop.sldworks;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SW2RD.URDF
{
    // A named bag of SolidWorks components that contributes a single mesh to the
    // exported model. Each link has zero or more visual groups and zero or more
    // collision groups; one MeshGroup -> one STL file -> one <mesh> asset and one
    // <geom> (MJCF) / one <visual> or <collision> element (URDF). Splitting a
    // concave shape across multiple groups gives MuJoCo a union of convex hulls
    // (the same idea works for URDF consumers like Bullet/ODE/Drake).
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class MeshGroup
    {
        [DataMember]
        public string Name;

        // Persistent reference IDs for this group's components. Survives save/load
        // of the SW configuration; resolved to live Component2 instances on demand.
        [DataMember(IsRequired = false)]
        public List<byte[]> ComponentPIDs;

        // Runtime-only set of components, populated from ComponentPIDs after the
        // SolidWorks document has been opened.
        public List<Component2> Components;

        // Runtime-only mesh filename (e.g. "package://<pkg>/meshes/foo.STL" for
        // URDF, or "foo.STL" for MJCF) populated by the export step. The URDF /
        // MJCF writers consume this when emitting <mesh filename=.../> entries.
        public string MeshFilename;

        public MeshGroup()
        {
            Name = "";
            ComponentPIDs = new List<byte[]>();
            Components = new List<Component2>();
            MeshFilename = "";
        }

        public MeshGroup(string name)
        {
            Name = name ?? "";
            ComponentPIDs = new List<byte[]>();
            Components = new List<Component2>();
            MeshFilename = "";
        }

        // Default name for a single visual group when migrating a legacy config
        // that only stored one flat list of visual components.
        public static string DefaultVisualName(string linkName)
        {
            string trimmed = string.IsNullOrWhiteSpace(linkName) ? "link" : linkName.Trim();
            return trimmed + "_visual";
        }

        // Default name for a single collision group when migrating a legacy
        // config or when the user adds a first collision group implicitly.
        public static string DefaultCollisionName(string linkName)
        {
            string trimmed = string.IsNullOrWhiteSpace(linkName) ? "link" : linkName.Trim();
            return trimmed + "_collision";
        }

        public MeshGroup Clone()
        {
            MeshGroup copy = new MeshGroup
            {
                Name = Name,
                MeshFilename = MeshFilename,
                ComponentPIDs = (ComponentPIDs != null)
                    ? new List<byte[]>(ComponentPIDs)
                    : new List<byte[]>(),
                Components = (Components != null)
                    ? new List<Component2>(Components)
                    : new List<Component2>(),
            };
            return copy;
        }
    }
}
