using SolidWorks.Interop.sldworks;
using SW2URDF.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using System.Xml;

namespace SW2URDF.URDF
{
    //The link class, it contains many other elements not found in the URDF.
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Link : URDFElement//, ISerializable
    {
        [DataMember]
        public Link Parent;

        [DataMember]
        public List<Link> Children;

        [DataMember]
        private readonly URDFAttribute NameAttribute;

        public string Name
        {
            get => (string)NameAttribute.Value;
            set => NameAttribute.Value = value;
        }

        [DataMember]
        public Inertial Inertial;

        // Visual is a "template" element that holds the link's shared <visual>
        // origin / material; the per-group mesh filenames are written by walking
        // VisualGroups in WriteURDF. For backward compatibility with old saved
        // configs that contained a single <visual>, this single Visual element
        // is still serialized.
        [DataMember]
        public Visual Visual;

        // Collision is a "template" element analogous to Visual. The per-group
        // collision mesh filenames live in CollisionGroups.
        [DataMember]
        public Collision Collision;

        [DataMember]
        public Joint Joint;

        [DataMember]
        public bool STLQualityFine;

        [DataMember]
        public bool isIncomplete;

        [DataMember]
        public bool isFixedFrame;

        public Component2 SWMainComponent;

        // VisualGroups carries one entry per <visual> mesh emitted for this link.
        // It is the source of truth at runtime; the legacy SWComponents/
        // VisualComponents accessors flatten across all groups.
        [DataMember(IsRequired = false)]
        public List<MeshGroup> VisualGroups;

        // CollisionGroups carries one entry per <collision> mesh emitted for
        // this link. Empty means "no <collision> elements" (URDF consumers will
        // fall back to visual; MJCF emits none unless ExportFiles populates it).
        [DataMember(IsRequired = false)]
        public List<MeshGroup> CollisionGroups;

        // When true, the CollisionGroups list is ignored at export time and the
        // VisualGroups are reused as collision meshes. Lets users avoid
        // re-picking the same components for both visual and collision when the
        // two sets coincide. The CollisionGroups data is still serialized so
        // unchecking the toggle restores any previously-defined collision
        // groups.
        [DataMember(IsRequired = false)]
        public bool CollisionUsesVisual;

        // InertialComponents are used only when InertialSource == Custom. May be empty
        // even when Custom is selected, in which case the visual components are used
        // as a fallback (with a warning logged).
        public List<Component2> InertialComponents;

        // Legacy DataMembers preserved so old configs (pre-multi-group) keep
        // deserializing without data loss. After Link.OnDeserialized runs, the
        // values get migrated into VisualGroups / CollisionGroups (when those
        // are not already populated) and the rest of the pipeline reads from
        // the groups.
        [DataMember]
        public List<byte[]> SWComponentPIDs;

        [DataMember(IsRequired = false)]
        public List<byte[]> CollisionComponentPIDs;

        [DataMember(IsRequired = false)]
        public List<byte[]> InertialComponentPIDs;

        [DataMember]
        public byte[] SWMainComponentPID;

        // Drives which set of components ComputeInertialProperties consumes.
        // Default Visual matches the legacy URDF behavior.
        [DataMember(IsRequired = false)]
        public InertialSource InertialSource;

        // Sites attached to this link/body for MJCF export. Ignored by the URDF writer.
        [DataMember(IsRequired = false)]
        public List<SiteSpec> Sites;

        // How a top-level body (immediate child of the world) attaches to
        // the world frame. Welded -> no joint emitted; Free -> MJCF
        // <freejoint/> on the body (URDF cannot express this and the writer
        // warns + drops it). Ignored on nested links, which describe their
        // attachment via Joint.Type. IsRequired=false so legacy configs
        // (pre-WorldNode) deserialize cleanly with the default Welded.
        [DataMember(IsRequired = false)]
        public WorldAttachmentModel WorldAttachment;

        // VisualComponents is a flattened view across all visual groups. Setter
        // collapses to a single default group containing the supplied list.
        // Existing call sites use this as "the bag of visual components" and
        // don't need to be aware of the multi-group split.
        //
        // CA2227 is suppressed because the writable setter has explicit
        // replace-contents semantics; it is not a "settable backing
        // collection" but a flattened-view assignment that buckets the
        // supplied items into VisualGroups[0].
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
                    VisualGroups.Add(new MeshGroup(MeshGroup.DefaultVisualName(Name))
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
        // Setter mirrors VisualComponents: collapse to a single group.
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
                    CollisionGroups.Add(new MeshGroup(MeshGroup.DefaultCollisionName(Name))
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

        // Backward-compatible alias for VisualComponents. Older code paths still refer
        // to "SWComponents" as the single bag of bodies; expose them as the visual set.
        [SuppressMessage("Usage", "CA2227:Collection properties should be read-only",
            Justification = "Legacy alias for VisualComponents; same flattened-view replace-contents semantics.")]
        public List<Component2> SWComponents
        {
            get => VisualComponents;
            set => VisualComponents = value;
        }

        public Link() : base("link", true)
        {
            Parent = null;
            Children = new List<Link>();
            VisualGroups = new List<MeshGroup>();
            CollisionGroups = new List<MeshGroup>();
            InertialComponents = new List<Component2>();
            SWComponentPIDs = new List<byte[]>();
            CollisionComponentPIDs = new List<byte[]>();
            InertialComponentPIDs = new List<byte[]>();
            Sites = new List<SiteSpec>();
            InertialSource = InertialSource.Visual;
            NameAttribute = new URDFAttribute("name", true, "");

            Inertial = new Inertial();
            Visual = new Visual();
            Collision = new Collision();
            Joint = new Joint();

            isFixedFrame = false;

            Attributes.Add(NameAttribute);
            ChildElements.Add(Inertial);
            ChildElements.Add(Visual);
            ChildElements.Add(Collision);
            ChildElements.Add(Joint);
        }

        public Link Clone()
        {
            Link cloned = new Link();
            cloned.SetElement(this);
            foreach (Link child in Children)
            {
                Link clonedChild = child.Clone();
                clonedChild.Parent = this;
                cloned.Children.Add(clonedChild);
            }
            return cloned;
        }

        public Link(Link parent) : base("link", true)
        {
            Parent = parent;
            Children = new List<Link>();
            VisualGroups = new List<MeshGroup>();
            CollisionGroups = new List<MeshGroup>();
            InertialComponents = new List<Component2>();
            SWComponentPIDs = new List<byte[]>();
            CollisionComponentPIDs = new List<byte[]>();
            InertialComponentPIDs = new List<byte[]>();
            Sites = new List<SiteSpec>();
            InertialSource = InertialSource.Visual;
            NameAttribute = new URDFAttribute("name", true, "");

            Inertial = new Inertial();
            Visual = new Visual();
            Collision = new Collision();
            Joint = new Joint();

            isFixedFrame = false;

            Attributes.Add(NameAttribute);
            ChildElements.Add(Inertial);
            ChildElements.Add(Visual);
            ChildElements.Add(Collision);
            ChildElements.Add(Joint);
        }

        // Migration step: populate VisualGroups / CollisionGroups from the legacy
        // single-list PIDs when an older config (which knew nothing about groups)
        // is read in. Idempotent: once VisualGroups is non-empty the legacy
        // fields are ignored.
        [OnDeserialized]
        private void OnDeserialized(StreamingContext _)
        {
            MigrateLegacyComponents();
        }

        // Idempotent migration that callers can also invoke manually after
        // unpacking into a Link tree (e.g. legacy XML deserialization that
        // bypasses the DataContract serializer).
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

            if (VisualGroups == null)
            {
                VisualGroups = new List<MeshGroup>();
            }
            if (VisualGroups.Count == 0
                && SWComponentPIDs != null
                && SWComponentPIDs.Count > 0)
            {
                VisualGroups.Add(new MeshGroup(MeshGroup.DefaultVisualName(Name))
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
                CollisionGroups.Add(new MeshGroup(MeshGroup.DefaultCollisionName(Name))
                {
                    ComponentPIDs = new List<byte[]>(CollisionComponentPIDs),
                    Components = new List<Component2>(),
                });
            }
        }

        // EnsureVisualGroupsInitialized guarantees a non-null VisualGroups list
        // (but does not inject a default group). Used by the VisualComponents
        // setter so we don't NPE when the field hasn't been initialized yet
        // (e.g. on a fresh Link constructed via DataContract deserialization
        // before the property setter runs).
        private void EnsureVisualGroupsInitialized()
        {
            if (VisualGroups == null)
            {
                VisualGroups = new List<MeshGroup>();
            }
        }

        public override void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("link");
            NameAttribute.WriteURDF(writer);

            if (Inertial != null)
            {
                Inertial.WriteURDF(writer);
            }

            // Emit one <visual> element per visual group, using the link's
            // single Visual as the shared origin/material template. We swap in
            // each group's mesh filename for the duration of the write and
            // restore it afterwards so the in-memory model is unchanged.
            string savedVisualMesh = Visual.Geometry.Mesh.Filename;
            try
            {
                if (VisualGroups != null && VisualGroups.Count > 0)
                {
                    foreach (MeshGroup group in VisualGroups)
                    {
                        Visual.Geometry.Mesh.Filename = group.MeshFilename ?? savedVisualMesh;
                        if (Visual.ElementContainsData())
                        {
                            Visual.WriteURDF(writer);
                        }
                    }
                }
                else if (Visual != null && Visual.ElementContainsData())
                {
                    // No groups configured but the template carries a filename
                    // (e.g. legacy single-mesh path that hasn't been migrated).
                    Visual.WriteURDF(writer);
                }
            }
            finally
            {
                Visual.Geometry.Mesh.Filename = savedVisualMesh;
            }

            // Same template trick for collision: swap in per-group mesh filenames
            // and restore.
            string savedCollisionMesh = Collision.Geometry.Mesh.Filename;
            try
            {
                if (CollisionGroups != null && CollisionGroups.Count > 0)
                {
                    foreach (MeshGroup group in CollisionGroups)
                    {
                        Collision.Geometry.Mesh.Filename = group.MeshFilename ?? savedCollisionMesh;
                        if (Collision.ElementContainsData())
                        {
                            Collision.WriteURDF(writer);
                        }
                    }
                }
                else if (Collision != null && Collision.ElementContainsData())
                {
                    Collision.WriteURDF(writer);
                }
            }
            finally
            {
                Collision.Geometry.Mesh.Filename = savedCollisionMesh;
            }

            writer.WriteEndElement();
            if (Joint.ElementContainsData())
            {
                Joint.WriteURDF(writer);
            }

            foreach (Link child in Children)
            {
                child.WriteURDF(writer);
            }
        }

        public override void SetElement(URDFElement externalElement)
        {
            base.SetElement(externalElement);
            SetSWComponents((Link)externalElement);
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

        public override bool AreRequiredFieldsSatisfied()
        {
            if (!base.AreRequiredFieldsSatisfied())
            {
                return false;
            }

            foreach (Link child in Children)
            {
                if (!child.AreRequiredFieldsSatisfied())
                {
                    return false;
                }
            }

            return true;
        }

        // Returns the components used to drive the inertial computation, based on
        // InertialSource. Falls back to VisualComponents (with isFallback=true) when
        // the selected set is empty.
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
