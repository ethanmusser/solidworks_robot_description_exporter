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

using log4net;
using SW2RD.Utilities;
using System.Windows.Forms;

namespace SW2RD.Input
{
    // A LinkNode is derived from TreeView.TreeNode. We add fields here so the
    // TreeView surface can carry per-link state alongside the SOLIDWORKS-side
    // Link payload.
    //
    // LinkNode is not serialized directly; node persistence happens via
    // the DataContract Link path in
    // ConfigurationSerialization — and TreeNode.Clone() uses MemberwiseClone,
    // not BinaryFormatter, so dropping these annotations has no behavior
    // impact.
    //
    // The tree root is now an explicit <see cref="WorldNode"/> (a LinkNode
    // subclass) that owns the global frame and any worldbody-direct
    // visual / collision / site geometry. Immediate children of the
    // WorldNode are the "top-level bodies" - each is the root of an
    // independent kinematic tree. Deeper nodes are nested links with an
    // incoming kinematic joint described by <see cref="Link.Joint"/>.
    public class LinkNode : TreeNode
    {
        private static readonly ILog logger = Logger.GetLogger();

        public Link Link
        { get; set; }

        // True for the WorldNode at the root. Top-level bodies (immediate
        // children of the WorldNode) and nested links both have this set
        // to false. Use <see cref="IsTopLevelBody"/> to detect a top-level
        // body explicitly.
        public bool IsBaseNode
        { get; set; }

        public bool IsIncomplete
        { get; set; }

        public bool NeedsSaving
        { get; set; }

        public string WhyIncomplete
        { get; set; }

        /// <summary>
        /// True when this node is a top-level body (an immediate child of
        /// the WorldNode root). Top-level bodies have <see cref="IsBaseNode"/>
        /// false but their <see cref="Link.Joint"/> describes the body's
        /// reference frame (= world->body offset coord-sys) rather than an
        /// incoming kinematic joint.
        /// </summary>
        public bool IsTopLevelBody => Parent is WorldNode;

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

    /// <summary>
    /// The root of the LinkNode tree. Represents the explicit world / global
    /// frame in the PMPage, distinct from any link or body. Its underlying
    /// <see cref="LinkNode.Link"/> is repurposed as the world geometry container:
    /// <list type="bullet">
    ///   <item><description><c>Link.Joint.CoordinateSystemName</c> is the global
    ///     origin coord-sys (matching pre-refactor base-link semantics so STL /
    ///     LocalizeJoint anchoring stays unchanged).</description></item>
    ///   <item><description><c>Link.VisualGroups</c> / <c>Link.CollisionGroups</c>
    ///     / <c>Link.Sites</c> hold worldbody-direct geometry (MJCF emits these
    ///     as direct children of <c>&lt;worldbody&gt;</c>; URDF drops them with
    ///     a warning).</description></item>
    ///   <item><description><c>Link.Inertial</c> is unused — the MJCF worldbody
    ///     is massless.</description></item>
    /// </list>
    ///
    /// Immediate children of the WorldNode are top-level body LinkNodes. Each
    /// top-level body's <c>Link.WorldAttachment</c> selects between Welded
    /// (no joint) and Free (MJCF freejoint).
    /// </summary>
    public class WorldNode : LinkNode
    {
        public const string DefaultName = "world";

        public WorldNode() : base()
        {
            IsBaseNode = true;
            Link.Name = DefaultName;
            Name = DefaultName;
            Text = DefaultName;
        }

        public WorldNode(Link link) : base(link)
        {
            IsBaseNode = true;
            if (string.IsNullOrEmpty(Text))
            {
                Text = DefaultName;
                Name = DefaultName;
            }
        }

        /// <summary>
        /// Convenience accessor for the global-origin coord-sys. Stored on
        /// the underlying Link's Joint to keep the legacy STL / LocalizeJoint
        /// anchor reads working unchanged.
        /// </summary>
        public string GlobalOriginCoordinateSystemName
        {
            get => Link?.Joint?.CoordinateSystemName ?? "";
            set
            {
                if (Link != null && Link.Joint != null)
                {
                    Link.Joint.CoordinateSystemName = value ?? "";
                }
            }
        }
    }
}
