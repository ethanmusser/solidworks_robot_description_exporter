using SW2RD.Core;
using SW2RD.URDF;
using SW2RD.Export;
using System;
using System.Reflection;

namespace SW2RD.Configuration
{
    /// <summary>
    /// LinkNode (PMPage) <-> Config (JSON-persisted) bridge. The bridge
    /// pivots through the legacy <see cref="Robot"/> + <see cref="Link"/>
    /// types so existing PMPage code, the export pipeline, and the SW
    /// component-resolution path
    /// (<see cref="CommonSwOperations.LoadSWComponents(SolidWorks.Interop.sldworks.ModelDoc2, LinkNode, System.Collections.Generic.List{string})"/>)
    /// keep operating on familiar types. Tree shape and component PIDs
    /// round-trip; transient export-time state (resolved Component2
    /// instances, <c>SWMainComponent</c>, computed origin / axis values)
    /// is intentionally NOT persisted - the load path repopulates it
    /// from SolidWorks.
    /// </summary>
    public static class ConfigBridge
    {
        /// <summary>
        /// Converts a configured LinkNode tree into a Config record ready
        /// for JSON serialization. The caller is responsible for refreshing
        /// the LinkNode -> Link name + child-tree linkage first
        /// (<see cref="ConfigurationSerialization"/> does this via
        /// <c>SavePropertiesLinkNodeToLink</c> + <c>UpdateLinkTree(null)</c>).
        /// </summary>
        public static Config CreateFromLinkNode(LinkNode baseNode, string robotName)
        {
            if (baseNode == null)
            {
                throw new ArgumentNullException(nameof(baseNode));
            }

            // Mirror SerializeToString's pre-write normalization: copy
            // LinkNode.Name onto Link.Name for every node and rebuild the
            // Link.Children tree from the LinkNode hierarchy. Without this
            // an in-place reshuffle in the PMPage (drag-drop, add/remove
            // child) would not round-trip because Link.Children is the
            // shape DataContract / Config walks.
            CopyLinkNodeNamesToLinks(baseNode);
            string treeName = robotName ?? (baseNode is WorldNode ? "" : baseNode.Link?.Name ?? "");

            // The new shape expects a WorldNode root; for compatibility the
            // adapter also accepts a plain LinkNode and synthesizes a
            // single-body World wrapper.
            KinematicTree tree = KinematicTreeAdapter.ToCore(baseNode, treeName);
            return Config.Create(tree, GetExporterVersion());
        }

        /// <summary>
        /// Reconstructs a LinkNode tree (rooted at a <see cref="WorldNode"/>)
        /// from a Config record. The caller must then run
        /// <see cref="CommonSwOperations.LoadSWComponents(SolidWorks.Interop.sldworks.ModelDoc2, LinkNode, System.Collections.Generic.List{string})"/>
        /// to resolve the persistent IDs back into live Component2 objects.
        /// </summary>
        public static LinkNode CreateLinkNode(Config config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (config.Tree == null)
            {
                throw new InvalidOperationException("Config has no kinematic tree.");
            }

            return KinematicTreeAdapter.ToWorldNode(config.Tree);
        }

        // Recursively pushes LinkNode.Name onto Link.Name. Mirrors the
        // private SavePropertiesLinkNodeToLink in ConfigurationSerialization
        // but lives here so the bridge is self-contained.
        private static void CopyLinkNodeNamesToLinks(LinkNode node)
        {
            if (node?.Link == null)
            {
                return;
            }
            node.Link.Name = node.Name;
            foreach (System.Windows.Forms.TreeNode childNode in node.Nodes)
            {
                CopyLinkNodeNamesToLinks(childNode as LinkNode);
            }
        }

        // Best-effort version string for breadcrumb purposes only - never
        // gates load. Pulls AssemblyInformationalVersion (set by the
        // VersionInfo build-time script) and falls back to AssemblyVersion
        // if the informational variant isn't populated.
        private static string GetExporterVersion()
        {
            try
            {
                Assembly asm = typeof(ConfigBridge).Assembly;
                AssemblyInformationalVersionAttribute info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                {
                    return info.InformationalVersion;
                }
                return asm.GetName().Version?.ToString() ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
