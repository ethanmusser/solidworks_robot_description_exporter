using SolidWorks.Interop.sldworks;
using System.Collections.Generic;
using System.Collections.Specialized;
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

        [DataMember]
        public Visual Visual;

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

        // VisualComponents are the SolidWorks components that contribute to the
        // <visual> mesh. Historically these were stored as SWComponents and were
        // also reused as the collision mesh; both interpretations are still
        // supported through the SWComponents alias property below.
        public List<Component2> VisualComponents;

        // CollisionComponents are the SolidWorks components that contribute to
        // the <collision> mesh. May be empty, in which case the visual mesh is
        // reused for collision (URDF backward-compatible behavior).
        public List<Component2> CollisionComponents;

        // InertialComponents are used only when InertialSource == Custom. May be empty
        // even when Custom is selected, in which case the visual components are used
        // as a fallback (with a warning logged).
        public List<Component2> InertialComponents;

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

        // Backward-compatible alias for VisualComponents. Older code paths still refer
        // to "SWComponents" as the single bag of bodies; expose them as the visual set.
        public List<Component2> SWComponents
        {
            get => VisualComponents;
            set => VisualComponents = value;
        }

        public Link() : base("link", true)
        {
            Parent = null;
            Children = new List<Link>();
            VisualComponents = new List<Component2>();
            CollisionComponents = new List<Component2>();
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
            VisualComponents = new List<Component2>();
            CollisionComponents = new List<Component2>();
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

        public override void WriteURDF(XmlWriter writer)
        {
            writer.WriteStartElement("link");
            NameAttribute.WriteURDF(writer);

            if (Inertial != null)
            {
                Inertial.WriteURDF(writer);
            }
            if (Visual != null)
            {
                Visual.WriteURDF(writer);
            }
            if (Collision != null)
            {
                Collision.WriteURDF(writer);
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

        public override void SetElementFromData(List<string> context, StringDictionary dictionary)
        {
            base.SetElementFromData(context, dictionary);

            if (dictionary.ContainsKey("Link.InertialSource"))
            {
                string raw = dictionary["Link.InertialSource"];
                if (System.Enum.TryParse(raw, true, out InertialSource parsed))
                {
                    InertialSource = parsed;
                }
            }

            if (dictionary.ContainsKey("Link.Sites"))
            {
                Sites = ParseSites(dictionary["Link.Sites"]);
            }
        }

        private static List<SiteSpec> ParseSites(string raw)
        {
            List<SiteSpec> sites = new List<SiteSpec>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return sites;
            }
            foreach (string entry in raw.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }
                int sep = entry.IndexOf('|');
                if (sep < 0)
                {
                    sites.Add(new SiteSpec(entry.Trim(), ""));
                }
                else
                {
                    string name = entry.Substring(0, sep).Trim();
                    string coord = entry.Substring(sep + 1).Trim();
                    sites.Add(new SiteSpec(name, coord));
                }
            }
            return sites;
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            string visualComponentsContext = "Link.SWComponents";
            dictionary.Add(visualComponentsContext, ComponentNamesJoined(VisualComponents));

            string collisionComponentsContext = "Link.CollisionComponents";
            dictionary.Add(collisionComponentsContext, ComponentNamesJoined(CollisionComponents));

            string inertialComponentsContext = "Link.InertialComponents";
            dictionary.Add(inertialComponentsContext, ComponentNamesJoined(InertialComponents));

            string inertialSourceContext = "Link.InertialSource";
            dictionary.Add(inertialSourceContext, InertialSource.ToString());

            string sitesContext = "Link.Sites";
            dictionary.Add(sitesContext, SitesJoined(Sites));

            base.AppendToCSVDictionary(context, dictionary);
        }

        private static string ComponentNamesJoined(List<Component2> components)
        {
            if (components == null)
            {
                return string.Empty;
            }
            IEnumerable<string> names = components.Select(c => c.Name2);
            return string.Join(";", names);
        }

        private static string SitesJoined(List<SiteSpec> sites)
        {
            if (sites == null || sites.Count == 0)
            {
                return string.Empty;
            }
            // Each site is encoded as "name|coord_system" and sites are joined with ';'.
            return string.Join(";",
                sites.Select(s => (s.Name ?? "") + "|" + (s.CoordinateSystemName ?? "")));
        }

        public override void SetElement(URDFElement externalElement)
        {
            base.SetElement(externalElement);
            SetSWComponents((Link)externalElement);
        }

        public void SetSWComponents(Link externalLink)
        {
            VisualComponents = (externalLink.VisualComponents != null) ?
                new List<Component2>(externalLink.VisualComponents) :
                new List<Component2>();

            CollisionComponents = (externalLink.CollisionComponents != null) ?
                new List<Component2>(externalLink.CollisionComponents) :
                new List<Component2>();

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
                    if (CollisionComponents != null && CollisionComponents.Count > 0)
                    {
                        return CollisionComponents;
                    }
                    isFallback = true;
                    return VisualComponents ?? new List<Component2>();

                case InertialSource.Custom:
                    if (InertialComponents != null && InertialComponents.Count > 0)
                    {
                        return InertialComponents;
                    }
                    isFallback = true;
                    return VisualComponents ?? new List<Component2>();

                case InertialSource.Visual:
                default:
                    return VisualComponents ?? new List<Component2>();
            }
        }
    }
}
