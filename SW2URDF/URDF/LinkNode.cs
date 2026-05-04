using log4net;
using SW2URDF.Utilities;
using System;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SW2URDF.URDF
{
    //A LinkNode is derived from a TreeView TreeNode. I've added many new fields to it so
    // that information can be passed around from the TreeView itself.
    //
    // [Serializable] mirrors TreeNode (which is itself [Serializable] and implements
    // ISerializable); without it the analyzer flags CA2237. We do not actually
    // round-trip LinkNode through BinaryFormatter — node persistence happens via the
    // DataContract Link path in ConfigurationSerialization — but the attribute is
    // required for any subclass of an ISerializable type.
    [Serializable]
    public class LinkNode : TreeNode
    {
        private static readonly ILog logger = Logger.GetLogger();

        public Link Link
        { get; set; }

        public bool IsBaseNode
        { get; set; }

        public bool IsIncomplete
        { get; set; }

        public bool NeedsSaving
        { get; set; }

        public string WhyIncomplete
        { get; set; }

        public LinkNode()
        {
            Link = new Link();
        }

        // Required by CA2229 because the parent TreeNode implements
        // ISerializable. We do not actually round-trip a LinkNode through
        // BinaryFormatter (the configuration persistence path goes through
        // DataContract on the embedded Link), but the serialization
        // constructor must exist so the base type can deserialize itself
        // when somebody attempts it (e.g. a clipboard or remoting scenario).
        // The Link payload deliberately starts blank — callers that need it
        // populated should rebuild via UpdateLinkTree / Clone after
        // deserialization.
        protected LinkNode(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
            Link = new Link();
        }

        public LinkNode(Link link)
        {
            logger.Info("Building node " + link.Name);

            IsBaseNode = link.Parent == null;
            IsIncomplete = true;
            Link = link;

            Name = Link.Name;
            Text = Link.Name;

            foreach (Link child in link.Children)
            {
                Nodes.Add(new LinkNode(child));
            }
        }

        public Link UpdateLinkTree(Link parent)
        {
            Link.Children.Clear();
            Link.Parent = parent;
            foreach (LinkNode child in Nodes)
            {
                Link.Children.Add(child.UpdateLinkTree(Link));
            }
            return Link;
        }

        public override object Clone()
        {
            LinkNode cloned = (LinkNode)base.Clone();
            cloned.Link = Link.Clone();
            return cloned;
        }

        public Link RebuildLink()
        {
            Link.Children.Clear();
            foreach (LinkNode child in Nodes)
            {
                Link.Children.Add(child.RebuildLink());
            }
            return Link;
        }
    }
}