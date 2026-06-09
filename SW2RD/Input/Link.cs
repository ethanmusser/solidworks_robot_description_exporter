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
using SW2RD.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace SW2RD.Input
{
    // The link class, the SolidWorks input/edit model node. It carries the
    // URDF link data plus SolidWorks state (components, persistent IDs, mesh
    // groups) that has no place in the canonical KinematicTree.
    public class Link
    {
        public Link Parent;

        public List<Link> Children;

        public string Name;

        public Inertial Inertial;

        // Visual is a "template" element that holds the link's shared <visual>
        // origin / material; the per-group mesh filenames live in VisualGroups.
        public Visual Visual;

        // Collision is a "template" element analogous to Visual. The per-group
        // collision mesh filenames live in CollisionGroups.
        public Collision Collision;

        public Joint Joint;

        public bool STLQualityFine;

        public bool isIncomplete;

        public bool isFixedFrame;

        public Component2 SWMainComponent;

        // VisualGroups carries one entry per <visual> mesh emitted for this
        // link. It is the source of truth at runtime; the legacy SWComponents /
        // VisualComponents accessors flatten across all groups.
        public List<MeshGroup> VisualGroups;

        // CollisionGroups carries one entry per <collision> mesh emitted for
        // this link. Empty means "no <collision> elements".
        public List<MeshGroup> CollisionGroups;

        // When true, the CollisionGroups list is ignored at export time and the
        // VisualGroups are reused as collision meshes.
        public const bool DefaultCollisionUsesVisual = true;

        public bool CollisionUsesVisual;

        // InertialComponents are used only when InertialSource == Custom.
        public List<Component2> InertialComponents;

        // Legacy single-list PIDs preserved so old configs (pre-multi-group)
        // keep round-tripping; MigrateLegacyComponents folds them into the
        // VisualGroups / CollisionGroups lists.
        public List<byte[]> SWComponentPIDs;

        public List<byte[]> CollisionComponentPIDs;

        public List<byte[]> InertialComponentPIDs;

        // Inertial component instance names / paths captured at save time,
        // index-aligned with InertialComponentPIDs. Mirror of the per-MeshGroup
        // ComponentNames / ComponentPaths so inertial references can re-bind by
        // name/path when a persist reference goes stale. May be shorter than
        // InertialComponentPIDs for legacy configs - readers MUST index-guard.
        public List<string> InertialComponentNames;

        public List<string> InertialComponentPaths;

        // Runtime-only: inertial references that failed to resolve on load,
        // preserved so a re-save does not erase them. Mirror of
        // MeshGroup.UnresolvedComponentRefs.
        public List<ComponentRef> UnresolvedInertialRefs;

        public byte[] SWMainComponentPID;

        // Drives which set of components ComputeInertialProperties consumes.
        public InertialSource InertialSource;

        // Named reference frames attached to this link/body. Exported to both
        // formats: an MJCF <site>, and in URDF an empty <link> joined to this
        // link by a fixed <joint>.
        public List<SiteSpec> Sites;

        // How a top-level body (immediate child of the world) attaches to the
        // world frame. Welded -> no joint emitted; Free -> MJCF <freejoint/>.
        public WorldAttachmentModel WorldAttachment;

        // VisualComponents is a flattened view across all visual groups. Setter
        // collapses to a single default group containing the supplied list.
        [SuppressMessage("Usage", "CA2227:Collection properties should be read-only",
            Justification = "Flattened view over VisualGroups; setter has documented replace-contents semantics.")]
        public List<Component2> VisualComponents
        {
            get
            {
                List<Component2> all = new List<Component2>();
                if (VisualGroups != null)
                {
                    foreach (MeshGroup g in VisualGroups)
                    {
                        if (g.Components != null)
                        {
                            all.AddRange(g.Components);
                        }
                    }
                }
                return all;
            }
            set
            {
                List<Component2> incoming = value ?? new List<Component2>();
                EnsureVisualGroupsInitialized();
                if (VisualGroups.Count == 0)
                {
                    VisualGroups.Add(new MeshGroup(MeshGroup.DefaultVisualName())
                    {
                        Components = new List<Component2>(incoming),
                    });
                }
                else
                {
                    // Replace the first group's components and discard any
                    // additional groups: the legacy "set all visual components"
                    // semantic flattens to a single group.
                    VisualGroups[0].Components = new List<Component2>(incoming);
                    while (VisualGroups.Count > 1)
                    {
                        VisualGroups.RemoveAt(VisualGroups.Count - 1);
                    }
                }
            }
        }

        // CollisionComponents is a flattened view across all collision groups.
        [SuppressMessage("Usage", "CA2227:Collection properties should be read-only",
            Justification = "Flattened view over CollisionGroups; setter has documented replace-contents semantics.")]
        public List<Component2> CollisionComponents
        {
            get
            {
                List<Component2> all = new List<Component2>();
                if (CollisionGroups != null)
                {
                    foreach (MeshGroup g in CollisionGroups)
                    {
                        if (g.Components != null)
                        {
                            all.AddRange(g.Components);
                        }
                    }
                }
                return all;
            }
            set
            {
                List<Component2> incoming = value ?? new List<Component2>();
                if (CollisionGroups == null)
                {
                    CollisionGroups = new List<MeshGroup>();
                }
                if (incoming.Count == 0)
                {
                    // Legacy callers used "set to empty" to mean "no collision
                    // mesh". Drop all groups so URDF consumers fall back to
                    // visual.
                    CollisionGroups.Clear();
                    return;
                }
                if (CollisionGroups.Count == 0)
                {
                    CollisionGroups.Add(new MeshGroup(MeshGroup.DefaultCollisionName())
                    {
                        Components = new List<Component2>(incoming),
                    });
                }
                else
                {
                    CollisionGroups[0].Components = new List<Component2>(incoming);
                    while (CollisionGroups.Count > 1)
                    {
                        CollisionGroups.RemoveAt(CollisionGroups.Count - 1);
                    }
                }
            }
        }

        // Backward-compatible alias for VisualComponents.
        [SuppressMessage("Usage", "CA2227:Collection properties should be read-only",
            Justification = "Legacy alias for VisualComponents; same flattened-view replace-contents semantics.")]
        public List<Component2> SWComponents
        {
            get => VisualComponents;
            set => VisualComponents = value;
        }

        public Link() : this(null)
        {
        }

        public Link(Link parent)
        {
            Parent = parent;
            Children = new List<Link>();
            VisualGroups = new List<MeshGroup>();
            CollisionGroups = new List<MeshGroup>();
            InertialComponents = new List<Component2>();
            SWComponentPIDs = new List<byte[]>();
            CollisionComponentPIDs = new List<byte[]>();
            InertialComponentPIDs = new List<byte[]>();
            InertialComponentNames = new List<string>();
            InertialComponentPaths = new List<string>();
            UnresolvedInertialRefs = new List<ComponentRef>();
            Sites = new List<SiteSpec>();
            CollisionUsesVisual = DefaultCollisionUsesVisual;
            InertialSource = InertialSource.Visual;
            Name = "";

            Inertial = new Inertial();
            Visual = new Visual();
            Collision = new Collision();
            Joint = new Joint();

            isFixedFrame = false;
        }

        public Link Clone()
        {
            Link cloned = new Link
            {
                Name = Name,
                Inertial = Inertial.Clone(),
                Visual = Visual.Clone(),
                Collision = Collision.Clone(),
                Joint = Joint.Clone(),
            };
            cloned.SetSWComponents(this);
            foreach (Link child in Children)
            {
                Link clonedChild = child.Clone();
                clonedChild.Parent = this;
                cloned.Children.Add(clonedChild);
            }
            return cloned;
        }

        // Idempotent migration that callers invoke after unpacking a Link tree
        // (e.g. after resolving persistent IDs to live components). Folds the
        // legacy single-list PIDs into the VisualGroups / CollisionGroups lists
        // and null-guards the runtime collections.
        public void MigrateLegacyComponents()
        {
            if (Sites == null)
            {
                Sites = new List<SiteSpec>();
            }
            if (InertialComponents == null)
            {
                InertialComponents = new List<Component2>();
            }
            if (InertialComponentNames == null)
            {
                InertialComponentNames = new List<string>();
            }
            if (InertialComponentPaths == null)
            {
                InertialComponentPaths = new List<string>();
            }
            if (UnresolvedInertialRefs == null)
            {
                UnresolvedInertialRefs = new List<ComponentRef>();
            }

            if (VisualGroups == null)
            {
                VisualGroups = new List<MeshGroup>();
            }
            if (VisualGroups.Count == 0
                && SWComponentPIDs != null
                && SWComponentPIDs.Count > 0)
            {
                VisualGroups.Add(new MeshGroup(MeshGroup.DefaultVisualName())
                {
                    ComponentPIDs = new List<byte[]>(SWComponentPIDs),
                    Components = new List<Component2>(),
                });
            }

            if (CollisionGroups == null)
            {
                CollisionGroups = new List<MeshGroup>();
            }
            if (CollisionGroups.Count == 0
                && CollisionComponentPIDs != null
                && CollisionComponentPIDs.Count > 0)
            {
                CollisionGroups.Add(new MeshGroup(MeshGroup.DefaultCollisionName())
                {
                    ComponentPIDs = new List<byte[]>(CollisionComponentPIDs),
                    Components = new List<Component2>(),
                });
            }
        }

        // Guarantees a non-null VisualGroups list (but does not inject a default
        // group).
        private void EnsureVisualGroupsInitialized()
        {
            if (VisualGroups == null)
            {
                VisualGroups = new List<MeshGroup>();
            }
        }

        public void SetSWComponents(Link externalLink)
        {
            VisualGroups = CloneGroups(externalLink.VisualGroups);
            CollisionGroups = CloneGroups(externalLink.CollisionGroups);

            InertialComponents = (externalLink.InertialComponents != null) ?
                new List<Component2>(externalLink.InertialComponents) :
                new List<Component2>();

            SWComponentPIDs = (externalLink.SWComponentPIDs != null) ?
                new List<byte[]>(externalLink.SWComponentPIDs) :
                new List<byte[]>();

            CollisionComponentPIDs = (externalLink.CollisionComponentPIDs != null) ?
                new List<byte[]>(externalLink.CollisionComponentPIDs) :
                new List<byte[]>();

            InertialComponentPIDs = (externalLink.InertialComponentPIDs != null) ?
                new List<byte[]>(externalLink.InertialComponentPIDs) :
                new List<byte[]>();

            InertialComponentNames = (externalLink.InertialComponentNames != null) ?
                new List<string>(externalLink.InertialComponentNames) :
                new List<string>();

            InertialComponentPaths = (externalLink.InertialComponentPaths != null) ?
                new List<string>(externalLink.InertialComponentPaths) :
                new List<string>();

            UnresolvedInertialRefs = new List<ComponentRef>();
            if (externalLink.UnresolvedInertialRefs != null)
            {
                foreach (ComponentRef r in externalLink.UnresolvedInertialRefs)
                {
                    UnresolvedInertialRefs.Add((r != null) ? r.Clone() : null);
                }
            }

            Sites = new List<SiteSpec>();
            if (externalLink.Sites != null)
            {
                foreach (SiteSpec s in externalLink.Sites)
                {
                    Sites.Add(s.Clone());
                }
            }

            InertialSource = externalLink.InertialSource;
            SWMainComponent = externalLink.SWMainComponent;
            SWMainComponentPID = externalLink.SWMainComponentPID;

            isFixedFrame = externalLink.isFixedFrame;
            CollisionUsesVisual = externalLink.CollisionUsesVisual;
            WorldAttachment = externalLink.WorldAttachment;
        }

        private static List<MeshGroup> CloneGroups(List<MeshGroup> source)
        {
            List<MeshGroup> result = new List<MeshGroup>();
            if (source == null)
            {
                return result;
            }
            foreach (MeshGroup g in source)
            {
                result.Add(g.Clone());
            }
            return result;
        }

        public string[] GetJointNames(bool includeFixed)
        {
            List<string> names = new List<string>();

            if (Joint != null && (includeFixed || Joint.Type != "fixed"))
            {
                names.Add(Joint.Name);
            }
            foreach (Link child in Children)
            {
                names.AddRange(child.GetJointNames(includeFixed));
            }

            return names.ToArray();
        }

        // Returns the components used to drive the inertial computation, based
        // on InertialSource. Falls back to VisualComponents (with
        // isFallback=true) when the selected set is empty.
        public List<Component2> GetInertialComponents(out bool isFallback)
        {
            isFallback = false;
            switch (InertialSource)
            {
                case InertialSource.Collision:
                    List<Component2> collision = CollisionComponents;
                    if (collision != null && collision.Count > 0)
                    {
                        return collision;
                    }
                    isFallback = true;
                    return VisualComponents;

                case InertialSource.Custom:
                    if (InertialComponents != null && InertialComponents.Count > 0)
                    {
                        return InertialComponents;
                    }
                    isFallback = true;
                    return VisualComponents;

                case InertialSource.Visual:
                default:
                    return VisualComponents;
            }
        }
    }
}
