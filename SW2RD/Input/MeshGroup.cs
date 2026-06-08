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

namespace SW2RD.Input
{
    // A saved SolidWorks component reference (persistent ID plus the component
    // instance name and document path captured at save time). Used to preserve
    // references that failed to resolve on load so that a subsequent save does
    // not silently erase them, and so the PM can surface them for the user to
    // replace or remove. The instance name also drives the load-time fallback
    // that re-binds a stale persist reference to the still-present instance; the
    // path is preserved for display only (it cannot distinguish instances of the
    // same part file, so it never drives an auto-rebind).
    public class ComponentRef
    {
        public byte[] Pid;
        public string Name;
        public string Path;

        public ComponentRef()
        {
            Name = "";
            Path = "";
        }

        public ComponentRef(byte[] pid, string name, string path)
        {
            Pid = pid;
            Name = name ?? "";
            Path = path ?? "";
        }

        public ComponentRef Clone()
        {
            return new ComponentRef(
                (Pid != null) ? (byte[])Pid.Clone() : null,
                Name,
                Path);
        }

        // Best-effort human label for warnings / UI. Prefers the instance
        // name, falls back to the file basename, then to a sentinel.
        public string DisplayLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(Name))
                {
                    return Name;
                }
                if (!string.IsNullOrEmpty(Path))
                {
                    return System.IO.Path.GetFileName(Path);
                }
                return "(unknown component)";
            }
        }
    }

    // A named bag of SolidWorks components that contributes a single mesh to the
    // exported model. Each link has zero or more visual groups and zero or more
    // collision groups; one MeshGroup -> one STL file -> one <mesh> asset and one
    // <geom> (MJCF) / one <visual> or <collision> element (URDF). Splitting a
    // concave shape across multiple groups gives MuJoCo a union of convex hulls
    // (the same idea works for URDF consumers like Bullet/ODE/Drake).
    public class MeshGroup
    {
        public string Name;

        // Persistent reference IDs for this group's components. Survives save/load
        // of the SW configuration; resolved to live Component2 instances on demand.
        public List<byte[]> ComponentPIDs;

        // Component instance names (Component2.Name2) and document paths captured
        // at save time, index-aligned with ComponentPIDs. They let the load path
        // re-bind a stale persist reference to the still-present component, and
        // they survive (as ComponentReferenceModel.DisplayName / Path) through the
        // canonical Config JSON. May be shorter than ComponentPIDs for configs
        // written before these fields existed - readers MUST index-guard.
        public List<string> ComponentNames;

        public List<string> ComponentPaths;

        // Runtime-only set of components, populated from ComponentPIDs after the
        // SolidWorks document has been opened.
        public List<Component2> Components;

        // Runtime-only: references whose persistent ID (and name/path fallback)
        // failed to resolve on load. Preserved here so RetrieveSWComponentPIDs
        // merges them back into ComponentPIDs on save (no silent data loss) and
        // so the PM can show them to the user for replacement / removal.
        public List<ComponentRef> UnresolvedComponentRefs;

        // Runtime-only mesh filename (e.g. "package://<pkg>/meshes/foo.STL" for
        // URDF, or "foo.STL" for MJCF) populated by the export step. The URDF /
        // MJCF writers consume this when emitting <mesh filename=.../> entries.
        public string MeshFilename;

        public MeshGroup()
        {
            Name = "";
            ComponentPIDs = new List<byte[]>();
            ComponentNames = new List<string>();
            ComponentPaths = new List<string>();
            Components = new List<Component2>();
            UnresolvedComponentRefs = new List<ComponentRef>();
            MeshFilename = "";
        }

        public MeshGroup(string name)
        {
            Name = name ?? "";
            ComponentPIDs = new List<byte[]>();
            ComponentNames = new List<string>();
            ComponentPaths = new List<string>();
            Components = new List<Component2>();
            UnresolvedComponentRefs = new List<ComponentRef>();
            MeshFilename = "";
        }

        // Default base name for a visual group. Intentionally link-INDEPENDENT
        // ("visual", and "visual_2", "visual_3", ... via NextDefaultGroupName)
        // because the export pipeline already prefixes the link name when it
        // builds the mesh / geom name (ChooseVisualMeshBaseName ->
        // "<link>_<group>"). Embedding the link name here too produced the
        // doubled "<link>_<link>_visual" geom names; the export prefix alone
        // keeps the name unique across links while staying short.
        public static string DefaultVisualName()
        {
            return "visual";
        }

        // Default base name for a collision group. Link-INDEPENDENT for the
        // same reason as DefaultVisualName.
        public static string DefaultCollisionName()
        {
            return "collision";
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
                ComponentNames = (ComponentNames != null)
                    ? new List<string>(ComponentNames)
                    : new List<string>(),
                ComponentPaths = (ComponentPaths != null)
                    ? new List<string>(ComponentPaths)
                    : new List<string>(),
                Components = (Components != null)
                    ? new List<Component2>(Components)
                    : new List<Component2>(),
                UnresolvedComponentRefs = CloneRefs(UnresolvedComponentRefs),
            };
            return copy;
        }

        private static List<ComponentRef> CloneRefs(List<ComponentRef> source)
        {
            List<ComponentRef> result = new List<ComponentRef>();
            if (source == null)
            {
                return result;
            }
            foreach (ComponentRef r in source)
            {
                result.Add((r != null) ? r.Clone() : null);
            }
            return result;
        }
    }
}
