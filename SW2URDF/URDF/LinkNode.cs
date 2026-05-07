using log4net;
using SW2URDF.Utilities;
using System.Windows.Forms;

namespace SW2URDF.URDF
{
    // A LinkNode is derived from TreeView.TreeNode. We add fields here so the
    // TreeView surface can carry per-link state alongside the SOLIDWORKS-side
    // Link payload.
    //
    // Earlier revisions carried [Serializable] + an ISerializable constructor
    // purely to silence the now-retired FxCopAnalyzers CA2237 / CA2229
    // warnings about subclassing TreeNode (which is itself [Serializable]).
    // We never round-tripped LinkNode through BinaryFormatter — node
    // persistence happens via the DataContract Link path in
    // ConfigurationSerialization — and TreeNode.Clone() uses MemberwiseClone,
    // not BinaryFormatter, so dropping these annotations has no behavior
    // impact.
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
