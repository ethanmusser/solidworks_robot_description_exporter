/*
Copyright (c) 2015 Stephen Brawner

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
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2RD.Input;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SW2RD.Export
{
    public partial class ExportPropertyManager : PropertyManagerPage2Handler9
    {
        public void SaveConfigTree(ModelDoc2 model, LinkNode BaseNode, bool warnUser)
        {
            CommonSwOperations.RetrieveSWComponentPIDs(model, BaseNode);
            ConfigurationSerialization.SaveConfigTreeXML(swApp, model, BaseNode, warnUser);
        }

        //As nodes are created and destroyed, this menu gets called a lot. It basically just
        // adds the context menu (right-click menu) to the node
        public void AddDocMenu(LinkNode node)
        {
            node.ContextMenuStrip = docMenu;
            foreach (LinkNode child in node.Nodes)
            {
                AddDocMenu(child);
            }
        }

        // Finds the specified item in a combobox and sets the box to it. I'm not sure why I
        // couldn't do this with a foreach loop or even a for loop, but there is no way to get
        // the current number of items in the menu
        private void SelectComboBox(PropertyManagerPageCombobox box, string item)
        {
            short i = 0;
            string itemtext = "nothing";
            box.CurrentSelection = 0;

            // Cycles through the menu items until it finds what its looking for, it finds
            // blank strings, or itemtext is null
            while (!string.IsNullOrWhiteSpace(itemtext) && itemtext != item)
            {
                // Gets the item text at index in a pull-down menu. No way to now how many
                // items are in the combobox
                itemtext = box.get_ItemText(i);
                if (itemtext == item)
                {
                    box.CurrentSelection = i;
                }
                i++;
            }
        }

        // Adds an asterix to the node text if it is incomplete (not currently used)
        private void UpdateNodeNames(LinkNode node)
        {
            if (node.IsIncomplete)
            {
                node.Text = node.Link.Name + "*";
            }
            foreach (LinkNode child in node.Nodes)
            {
                UpdateNodeNames(child);
            }
        }

        // Determines how many nodes need to be built, and they are added to the current node
        private void CreateNewNodes(LinkNode CurrentlySelectedNode)
        {
            int nodesToBuild = (int)PMNumberBoxChildCount.Value - CurrentlySelectedNode.Nodes.Count;
            CreateNewNodes(CurrentlySelectedNode, nodesToBuild);
        }

        // Adds the number of empty nodes to the currently active node
        private void CreateNewNodes(LinkNode currentNode, int number)
        {
            for (int i = 0; i < number; i++)
            {
                LinkNode node = CreateEmptyNode(currentNode);
                currentNode.Nodes.Add(node);
            }
            for (int i = 0; i < -number; i++)
            {
                currentNode.Nodes.RemoveAt(currentNode.Nodes.Count - 1);
            }
            // The tree box is a fixed height (LinkTreeBoxHeight); do NOT grow it
            // with the node count here. SW PMPage does not reflow sibling
            // controls when a hosted WindowFromHandle's height changes at
            // runtime, so growing it overlapped the controls below the tree.
            // The WinForms TreeView's native vertical scrollbar handles overflow.
            currentNode.ExpandAll();
        }

        // When a new node is selected or another node is found that needs to be visited, this
        // method saves the previously active node and fills in the property mananger with the new one
        public void SwitchActiveNodes(LinkNode node)
        {
            // Switching links re-hydrates every SelectionBox (component Select4
            // loops, coord-sys / axis SelectByID2) and, on the Link/Joint
            // section, synchronously resolves the joint axis preview - which can
            // be slow on large or flexible-subassembly assemblies. Show a busy
            // indicator so the click does not look like a freeze. Inner scopes
            // (e.g. RefreshAxisDirectionPreview) nest under this one and only
            // swap the title.
            string linkLabel = node?.Link?.Name;
            if (string.IsNullOrEmpty(linkLabel))
            {
                linkLabel = node?.Text;
            }
            using (SwProgress.Busy(swApp, "Loading link: " + (linkLabel ?? "")))
            {
                SaveActiveNode();

                Font fontRegular = new Font(Tree.Font, FontStyle.Regular);
                Font fontBold = new Font(Tree.Font, FontStyle.Bold);
                if (previouslySelectedNode != null)
                {
                    previouslySelectedNode.NodeFont = fontRegular;
                }
                FillPropertyManager(node);

                //If this flag is set to true, it prevents this method from getting called again when
                // changing the selected node
                automaticallySwitched = true;

                //Change the selected node to the argument node. This highlights the newly activated node
                Tree.SelectedNode = node;

                node.NodeFont = fontBold;
                node.Text = node.Text;
                previouslySelectedNode = node;
                CheckNodeComplete(node);
            }
        }

        // This method runs through first the child nodes of the selected node to see if there are
        // more to visit then it runs through the nodes top to bottom to find the next to visit.
        // Returns the node if one is found otherwise it returns null.
        public LinkNode FindNextLinkToVisit(TreeView tree)
        {
            // First check if SelectedNode has any nodes to visit
            if (tree.SelectedNode != null)
            {
                LinkNode nodeToReturn = FindNextLinkToVisit((LinkNode)tree.SelectedNode);
                if (nodeToReturn != null)
                {
                    return nodeToReturn;
                }
            }

            // Now run through tree to see if any other nodes need to be visited
            return FindNextLinkToVisit((LinkNode)tree.Nodes[0]);
        }

        // Finds the next incomplete node and returns that
        public LinkNode FindNextLinkToVisit(LinkNode nodeToCheck)
        {
            if (nodeToCheck.Link.isIncomplete)
            {
                return nodeToCheck;
            }
            foreach (LinkNode node in nodeToCheck.Nodes)
            {
                return FindNextLinkToVisit(node);
            }
            return null;
        }

        // When the selected node is changed, the previously active node needs to be saved.
        // CoordinateSystemName / AxisName / AutoDeriveAxis are committed
        // incrementally by OnSelectionboxListChanged when the user picks
        // in the SelectionBoxes; this node-switch save handles the
        // textbox / combobox / checkbox state that does NOT have a
        // per-event commit hook.
        public void SaveActiveNode()
        {
            if (previouslySelectedNode != null)
            {
                previouslySelectedNode.Link.Name = PMTextBoxLinkName.Text;

                // Joint state save - only for nested links. Top-level bodies
                // have a "Joint" object that is repurposed to carry their
                // world->body offset coord-sys (committed inline via
                // OnSelectionboxListChanged), and the WorldNode has no joint
                // at all. For both non-nested cases we leave Link.Joint
                // unchanged here - the joint name/type/axis textboxes were
                // disabled when the user was on the node, so reading them
                // would commit stale values from a previously-edited nested
                // link.
                NodeRole previousRole = ResolveNodeRole(previouslySelectedNode);
                if (previousRole == NodeRole.NestedLink)
                {
                    previouslySelectedNode.Link.Joint.Name = PMTextBoxJointName.Text;
                    previouslySelectedNode.Link.Joint.Type = PMComboBoxJointType.get_ItemText(-1);
                    // currentAxisFlipped is also written through immediately on
                    // every bitmap-button click in OnButtonPress so the overlay
                    // arrow and persisted state stay in lockstep; this
                    // node-switch save is the safety net for any other path.
                    previouslySelectedNode.Link.Joint.AxisFlipped = currentAxisFlipped;

                    // PMComboBoxAxisSource is also written through on every
                    // dropdown change via OnComboboxSelectionChanged; this
                    // node-switch save mirrors AxisFlipped above as the safety
                    // net. Only commit it when the axis row is relevant
                    // (nested non-fixed joint) so the dropdown's default
                    // selection on irrelevant nodes never clobbers a saved
                    // source.
                    if (PMComboBoxAxisSource != null &&
                        ResolveNodeRole(previouslySelectedNode) == NodeRole.NestedLink &&
                        previouslySelectedNode.Link.Joint.Type != "fixed")
                    {
                        previouslySelectedNode.Link.Joint.AxisSource =
                            ClampAxisSource(PMComboBoxAxisSource.CurrentSelection);
                    }

                    // Joint Properties section. Each *OrClear setter
                    // either parses a populated textbox or clears the
                    // underlying URDFAttribute so the writer omits the
                    // attribute entirely. Reference / Armature / the
                    // auto-compute toggle are plain Joint fields and use
                    // local helpers.
                    SaveJointPropertiesToLink(previouslySelectedNode.Link.Joint);
                }
                else if (previousRole == NodeRole.TopLevelBody)
                {
                    // The unified joint-type dropdown carries the world
                    // attachment for a top-level body ("fixed" = 0 -> Welded,
                    // "free" = 1 -> Free). Also written through on every
                    // dropdown change via OnComboboxSelectionChanged; this
                    // node-switch save is the safety net.
                    if (PMComboBoxJointType != null)
                    {
                        short worldAttachmentChoice = PMComboBoxJointType.CurrentSelection;
                        previouslySelectedNode.Link.WorldAttachment =
                            (worldAttachmentChoice == 1)
                                ? SW2RD.Core.WorldAttachmentModel.Free
                                : SW2RD.Core.WorldAttachmentModel.Welded;
                    }
                }

                EnsureGroupsInitialized(previouslySelectedNode);
                EnsureSitesInitialized(previouslySelectedNode);
                SaveActiveSiteFields(previouslySelectedNode);

                // Persist the "Use visual groups as collision" toggle before
                // committing the per-group selections so that downstream code
                // can rely on it being current.
                previouslySelectedNode.Link.CollisionUsesVisual =
                    PMCheckCollisionUsesVisual.Checked;

                // Commit the SelectionBox contents back into the
                // previously-active visual / collision group on the link
                // we're leaving.
                CommitActiveVisualGroupSelection(previouslySelectedNode);
                CommitActiveCollisionGroupSelection(previouslySelectedNode);

                if (previouslySelectedNode.Link.InertialComponents == null)
                {
                    previouslySelectedNode.Link.InertialComponents = new List<Component2>();
                }

                // Read the InertialSource dropdown FIRST so the inertial
                // commit gate below decides based on the user's CURRENT
                // choice. The data model may still hold the previous
                // source value until this SaveActiveNode pass persists
                // it below; OnComboboxSelectionChanged DOES update the
                // data model on every user pick, but reading the
                // combobox directly here is the safe authoritative read
                // regardless of whether SW fired the combo change event.
                short choice = PMComboBoxInertialSource.CurrentSelection;
                InertialSource currentSource = (choice == 1) ? InertialSource.Collision
                    : (choice == 2) ? InertialSource.Custom
                    : InertialSource.Visual;

                // Same SelectionMgr-during-OnClose hazard as
                // CommitActiveVisualGroupSelection: the inertial SelectionBox
                // is backed by a marked selection that SolidWorks has already
                // released by the time OnClose runs. The OnSelectionboxListChanged
                // handler keeps InertialComponents up to date for every user
                // pick, so skipping the close-time refresh preserves the
                // committed data instead of clobbering it from an empty mark.
                //
                // Plus the SelectionMgr-leak defense: read into a local
                // list (filtered to Component2 by GetSelectedComponents) and
                // only replace the saved state if the user actually picked
                // something. The leak source (ExportHelper.GetRefAxis's
                // assembly-level Append=false SelectByID2 on the joint
                // axis feature) means the inertial mark can carry a stale
                // RefAxis under positive-mark queries on some SW versions;
                // without this defense the mid-PM-session navigation
                // would silently wipe the user's saved inertial picks.
                //
                // InertialSource gate: when source != Custom, the
                // inertial mark holds the visual / collision union for
                // highlight purposes only (see
                // LoadActiveInertialIntoSelectionBox / ResolveInertialHighlightSet).
                // Committing those into InertialComponents would
                // silently corrupt the user's saved Custom picks every
                // time they navigate away from a Visual-or-Collision-
                // sourced link. The SelectionBox is disabled in this
                // state (SetInertialEditorEnabled), but the
                // green-check + OnSelectionboxListChanged paths can
                // still fire while the mark is populated; the gate
                // here is the canonical write-side enforcement.
                if (!pageIsClosing && currentSource == InertialSource.Custom)
                {
                    List<Component2> pickedInertial = new List<Component2>();
                    CommonSwOperations.GetSelectedComponents(
                        ActiveSWModel, pickedInertial, PMSelectionInertial.Mark);
                    if (pickedInertial.Count > 0 ||
                        previouslySelectedNode.Link.InertialComponents.Count == 0)
                    {
                        previouslySelectedNode.Link.InertialComponents.Clear();
                        previouslySelectedNode.Link.InertialComponents.AddRange(pickedInertial);
                    }
                }

                previouslySelectedNode.Link.InertialSource = currentSource;
            }
        }

        //Creates an Empty node when children are added to a link
        public LinkNode CreateEmptyNode(LinkNode Parent)
        {
            // Tree root: synthesize a WorldNode container with one default
            // Welded top-level body underneath (named "base_link" to match
            // the fresh-tree convention pre-refactor). The WorldNode itself
            // owns the global-origin coord-sys + worldbody-direct geometry
            // slots; the inner "base_link" is the first body the user
            // configures.
            if (Parent == null)
            {
                WorldNode worldNode = new WorldNode();
                worldNode.IsIncomplete = true;
                worldNode.ContextMenuStrip = docMenu;

                LinkNode baseBody = CreateEmptyTopLevelBody();
                worldNode.Link.Children.Add(baseBody.Link);
                baseBody.Link.Parent = worldNode.Link;
                worldNode.Nodes.Add(baseBody);
                return worldNode;
            }

            LinkNode node = new LinkNode();
            if (Parent is WorldNode)
            {
                // First-level child: top-level body. Welded by default.
                node = CreateEmptyTopLevelBody();
            }
            else
            {
                node.IsBaseNode = false;
                node.Link.Name = "empty_link";
                // SelectionBox-only UI: empty AxisName with the default
                // ReferenceAxis source means the user still needs to pick a
                // reference axis. Coordinate-system axes / auto-derive are
                // opt-in via the "Joint axis source" dropdown.
                node.Link.Joint.AxisName = "";
                node.Link.Joint.CoordinateSystemName = "";
                node.Link.Joint.AxisSource = JointAxisSource.ReferenceAxis;
                node.Link.Joint.Type = "";
                node.Link.InertialComponents = new List<Component2>();
                node.Link.Sites = new List<SiteSpec>();
                node.Link.InertialSource = InertialSource.Visual;
                node.IsBaseNode = false;
                node.IsIncomplete = true;
                node.Link.VisualGroups = new List<MeshGroup>();
                node.Link.CollisionGroups = new List<MeshGroup>();
                node.Link.CollisionUsesVisual = Link.DefaultCollisionUsesVisual;
                node.Name = node.Link.Name;
                node.Text = node.Link.Name;
            }
            node.ContextMenuStrip = docMenu;
            return node;
        }

        // Builds a default top-level body LinkNode (immediate child of a
        // WorldNode). WorldAttachment defaults to Welded. The body's
        // Joint.CoordinateSystemName is the world->body offset, NOT an
        // incoming kinematic joint; joint type / axis / properties are
        // disabled in the PM for top-level bodies.
        private LinkNode CreateEmptyTopLevelBody()
        {
            LinkNode node = new LinkNode();
            node.Link.Name = "base_link";
            node.Link.Joint.Name = "";
            node.Link.Joint.AxisName = "";
            node.Link.Joint.CoordinateSystemName = "";
            node.Link.Joint.AxisSource = JointAxisSource.ReferenceAxis;
            node.Link.Joint.Type = "";
            node.Link.WorldAttachment = SW2RD.Core.WorldAttachmentModel.Welded;
            node.Link.InertialComponents = new List<Component2>();
            node.Link.Sites = new List<SiteSpec>();
            node.Link.InertialSource = InertialSource.Visual;
            node.IsBaseNode = false;
            node.IsIncomplete = true;
            node.Link.VisualGroups = new List<MeshGroup>();
            node.Link.CollisionGroups = new List<MeshGroup>();
            node.Link.CollisionUsesVisual = Link.DefaultCollisionUsesVisual;
            node.Name = node.Link.Name;
            node.Text = node.Link.Name;
            return node;
        }

        //Sets all the controls in the Property Manager from the Selected Node
        public void FillPropertyManager(LinkNode node)
        {
            PMTextBoxLinkName.Text = node.Link.Name;
            PMNumberBoxChildCount.Value = node.Nodes.Count;

            // Migrate any legacy single-list config into the new groups model
            // and ensure the group lists are non-null. New links may have
            // zero visual groups until the user adds one.
            EnsureGroupsInitialized(node);
            if (node.Link.InertialComponents == null)
            {
                node.Link.InertialComponents = new List<Component2>();
            }
            if (node.Link.Sites == null)
            {
                node.Link.Sites = new List<SiteSpec>();
            }

            // Reset which group is being edited; default to the first group on
            // each link switch when one exists.
            activeVisualGroupIndex = (node.Link.VisualGroups.Count > 0) ? 0 : -1;
            activeCollisionGroupIndex = (node.Link.CollisionGroups.Count > 0) ? 0 : -1;
            activeSiteIndex = (node.Link.Sites.Count > 0) ? 0 : -1;

            // Refresh the listboxes BEFORE re-populating selection boxes; the
            // selection box population calls ClearSelection2.
            RefreshVisualGroupsListbox(node);
            RefreshCollisionGroupsListbox(node);
            SyncVisualGroupNameTextbox(node);
            SyncCollisionGroupNameTextbox(node);
            RefreshSitesListbox(node);
            LoadActiveSiteIntoForm(node);

            // Toggle Enabled state on the Link/Joint controls BEFORE
            // populating the SelectionBoxes. SW occasionally drops
            // SelectionBox display contents when a control's Enabled
            // state flips after the box has been populated; doing the
            // Enable pass first means every load runs against settled
            // controls. The role-based EnableControls overload
            // distinguishes World / TopLevelBody / NestedLink so the
            // coord-sys picker + joint-type dropdown get the right enabled
            // state.
            NodeRole nodeRole = ResolveNodeRole(node);
            EnableControls(nodeRole);

            // Repopulate the single role-aware joint-type dropdown and
            // restore its selection from the active node (world attachment
            // for a top-level body, joint type for a nested link, empty for
            // the World root).
            PopulateJointTypeComboForRole(node, nodeRole);

            // Repopulate only the SelectionBox marks owned by the active
            // page-1 section so the SOLIDWORKS viewer highlights the entities
            // the user is editing. RehydrateMarksForActiveSection drains every
            // other mark and owns the per-section mark mapping.
            RehydrateMarksForActiveSection(node, currentActiveSectionId);

            // Inertial source combo.
            switch (node.Link.InertialSource)
            {
                case InertialSource.Collision:
                    PMComboBoxInertialSource.CurrentSelection = 1;
                    break;
                case InertialSource.Custom:
                    PMComboBoxInertialSource.CurrentSelection = 2;
                    break;
                case InertialSource.Visual:
                default:
                    PMComboBoxInertialSource.CurrentSelection = 0;
                    break;
            }
            // Mirror the source on the SelectionBox enable so the user
            // immediately sees "read-only display" affordance when
            // source != Custom. See SetInertialEditorEnabled for the
            // full rationale.
            SetInertialEditorEnabled(node.Link.InertialSource);

            // "Use visual groups as collision" toggle. Re-sync the editor
            // visibility on every node switch so the UI matches the data
            // model.
            PMCheckCollisionUsesVisual.Checked = node.Link.CollisionUsesVisual;
            SetCollisionEditorEnabled(!node.Link.CollisionUsesVisual);

            //Setting joint properties (controls already Enable-toggled above
            //before the SelectionBox loads). Joint name / axis / type /
            //properties round-trip only for nested links - the WorldNode
            //has no joint at all and a top-level body has no incoming
            //kinematic joint (its Link.Joint is repurposed to carry the
            //world->body offset coord-sys). For both non-nested cases we
            //clear the joint inputs so the disabled controls don't show
            //stale values from a previously-edited nested link.
            if (nodeRole == NodeRole.NestedLink && node.Parent != null)
            {
                PMTextBoxJointName.Text = node.Link.Joint.Name;
                PMLabelParentLink.Caption = node.Parent.Name;

                // Joint type selection is handled by
                // PopulateJointTypeComboForRole above (the single role-aware
                // dropdown); no separate select needed here.

                // Joint axis source dropdown: the reference-axis SelectionBox
                // is disabled (and remains empty) for any source other than
                // "Reference axis"; otherwise LoadActiveJointAxisIntoSelectionBox
                // above will have populated it.
                if (PMComboBoxAxisSource != null)
                {
                    PMComboBoxAxisSource.CurrentSelection = (short)(int)node.Link.Joint.AxisSource;
                }
                // Grey the whole axis row when this nested joint is "fixed"
                // (axis irrelevant); otherwise honor the axis-source choice.
                UpdateAxisRowEnabledState(node);

                // Restore the persisted "Reverse Direction" toggle for this
                // joint and (re)render the overlay arrow in the model view so
                // the user sees the saved direction as soon as they land on
                // the node. The bitmap button has no visual "pressed" state -
                // the overlay arrow IS the feedback. The arrow is gated to
                // the Link/Joint section so it doesn't linger in the viewport
                // while the user is editing geometry on another section; when
                // the Link/Joint section is not the active one we clear it and
                // let OnGroupExpand redraw it on return.
                currentAxisFlipped = node.Link.Joint.AxisFlipped;
                if (currentActiveSectionId == LinkJointGroupID)
                {
                    RefreshAxisDirectionPreview();
                }
                else
                {
                    Exporter.ClearAxisOverlay();
                }

                // Joint Properties section: limits, dynamics, MJCF-only
                // reference / armature, and the per-joint auto-compute
                // toggle. Empty textbox = attribute is unset on the data
                // model and the writer omits it. Damping / Friction live
                // on Joint.Dynamics, the rest live directly on Joint.
                FillJointPropertiesFromLink(node.Link.Joint);
            }
            else
            {
                //Labels and text box have be blanked before de-activating them
                PMTextBoxJointName.Text = "";
                PMLabelParentLink.Caption = (nodeRole == NodeRole.TopLevelBody && node.Parent != null)
                    ? node.Parent.Name
                    : " ";
                // Only clear the dropdown for the World root (its combo is
                // empty + disabled). A top-level body reuses this same combo as
                // its {fixed, free} world-attachment selector, and
                // PopulateJointTypeComboForRole above already set its selection
                // from Link.WorldAttachment. Clearing it here would force the
                // selection back to index 0 ("fixed") - SelectComboBox sets
                // CurrentSelection = 0 and the {fixed, free} list has no empty
                // item to match - which both mis-displays a "free" body and
                // lets the next SaveActiveNode read the stale index 0 and
                // overwrite WorldAttachment back to Welded.
                if (nodeRole == NodeRole.World)
                {
                    SelectComboBox(PMComboBoxJointType, "");
                }

                // No joint axis on this node: clear any previously-rendered
                // overlay so we don't leave a stale arrow in the viewport.
                currentAxisFlipped = false;
                Exporter.ClearAxisOverlay();

                // No joint properties on this node; clear the textboxes
                // so the next nested-link load starts from a clean slate.
                ClearJointPropertyTextboxes();
            }
        }

        // SelectionBox-only feature-picker rehydration helpers. Each
        // attaches the Feature.Name persisted on the active node to the
        // corresponding SelectionBox via SelectByID2 + Mark, so when
        // the user reopens the PMPage on a configured assembly the
        // boxes show what was previously picked. The suppress guard
        // prevents the resulting "Count went 0->1" OnSelectionboxListChanged
        // event from re-committing the same value (a silent no-op, but
        // documented for symmetry with the visual / collision loaders).
        //
        // Called from FillPropertyManager (per node switch) and from
        // OnTabClicked (when the user activates the Link/Joint tab).
        // Empty / sentinel names are no-ops; the SelectionBox stays
        // empty, which is the canonical UI state for "auto-generate".
        // Rehydrates the single coordinate-system picker for ANY node role.
        // The persisted name lives on Link.Joint.CoordinateSystemName for the
        // World (global origin), top-level bodies (world->body offset), and
        // nested links (joint origin) alike, so one loader serves every role.
        private void LoadActiveCoordsysIntoSelectionBox(LinkNode node)
        {
            if (node == null || PMSelectionJointCoordsys == null)
            {
                return;
            }
            // Always clear the mark first so the previous link's pick doesn't
            // bleed into a node whose coord-sys is unset.
            CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionJointCoordsys.Mark);
            string name = node.Link?.Joint?.CoordinateSystemName;
            if (!IsRealFeatureName(name))
            {
                return;
            }
            SelectFeatureIntoMark(name, "COORDSYS", PMSelectionJointCoordsys.Mark);
        }

        private void LoadActiveJointAxisIntoSelectionBox(LinkNode node)
        {
            if (node == null || PMSelectionJointAxis == null)
            {
                return;
            }
            CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionJointAxis.Mark);
            if (node.IsBaseNode)
            {
                return;
            }
            Joint joint = node.Link?.Joint;
            // The reference-axis SelectionBox is only populated in
            // "Reference axis" mode; coordinate-system basis axes and
            // auto-derive carry no reference-axis pick to rehydrate.
            if (joint == null || joint.AxisSource != JointAxisSource.ReferenceAxis)
            {
                return;
            }
            string name = joint.AxisName;
            if (!IsRealFeatureName(name))
            {
                return;
            }
            SelectFeatureIntoMark(name, "AXIS", PMSelectionJointAxis.Mark);
        }

        private static bool IsRealFeatureName(string name)
        {
            return !string.IsNullOrEmpty(name) && name != "Automatically Generate" && name != "None";
        }

        // Wraps the SelectByID2 -> mark call with the suppress guard +
        // a try/catch so a missing feature (deleted from the assembly
        // since the configuration was last saved) just leaves the
        // SelectionBox empty rather than throwing out of FillPropertyManager.
        //
        // The persisted name may be a bare assembly-level feature name
        // ("Coordinate System1") or a sub-component reference encoded as
        // "<FeatureName> <Component2.Name2>" (the convention produced by
        // ReadMarkedFeatureName and consumed by ResolveFeatureReference).
        // SelectByID2 does NOT understand the "<...>" display form, so we
        // translate it to SolidWorks' own component-qualified selection id
        // ("FeatureName@Component@...") before selecting.
        private void SelectFeatureIntoMark(string featureName, string typeName, int mark)
        {
            string selectionId = ToSelectByIdName(featureName);
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                ActiveSWModel.Extension.SelectByID2(
                    selectionId, typeName, 0, 0, 0, true, mark, null, 0);
            }
            catch (Exception ex)
            {
                logger.Warn("SelectByID2 for " + typeName + " '" + selectionId + "' failed: " + ex.Message);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
        }

        // Converts a persisted reference-geometry name into the selection
        // id SelectByID2 expects. A bare top-level name passes through
        // unchanged. A sub-component reference "<FeatureName> <Comp.Name2>"
        // becomes the SolidWorks component-qualified id
        // "<FeatureName>@<component selection path>".
        //
        // The component selection path is NOT a simple reverse-and-join of
        // Component2.Name2's "/"-separated instance chain. For a feature
        // nested more than one level deep (i.e. inside a SUB-ASSEMBLY)
        // SOLIDWORKS expects each level encoded as "<instance>@<parent-doc>"
        // and chained with "/", e.g. a feature on component "SubAsm-1/Part-1"
        // selects as "<FeatureName>@SubAsm-1@Assembly/Part-1@SubAsm" - the
        // exact form IComponent2.GetSelectByIDString() produces for the
        // owning component, with the feature name prefixed. We therefore ask
        // SolidWorks for the canonical component path rather than rebuilding
        // it ourselves; the old manual reverse-join only happened to be
        // correct for the single-level "Part-1" case
        // ("<FeatureName>@Part-1@Assembly") and silently produced an
        // unresolvable id for sub-assembly nesting, which is why a
        // sub-assembly coord system would vanish from the SelectionBox on
        // every rehydrate. The manual reconstruction is retained only as a
        // best-effort fallback when the owning component can no longer be
        // found (e.g. the feature was deleted from the assembly since the
        // configuration was saved).
        private string ToSelectByIdName(string persistedName)
        {
            if (string.IsNullOrEmpty(persistedName))
            {
                return persistedName;
            }
            int indexFirst = persistedName.IndexOf('<');
            int indexLast = (indexFirst < 0) ? -1 : persistedName.IndexOf('>', indexFirst);
            if (indexFirst < 0 || indexLast <= indexFirst)
            {
                return persistedName;
            }

            string featureName = persistedName.Substring(0, indexFirst).Trim();
            string componentName = persistedName
                .Substring(indexFirst + 1, indexLast - indexFirst - 1)
                .Trim();
            if (string.IsNullOrEmpty(componentName))
            {
                return featureName;
            }

            // Preferred path: let SOLIDWORKS produce the canonical component
            // selection string (correct at any sub-assembly nesting depth)
            // and prefix the feature name. Proven for the single-level case
            // ("Part-1@Assembly" -> "<FeatureName>@Part-1@Assembly") and the
            // only form that works for deeper sub-assembly nesting.
            Component2 owningComponent = FindComponentByName2(componentName);
            if (owningComponent != null)
            {
                try
                {
                    string componentSelection = owningComponent.GetSelectByIDString();
                    if (!string.IsNullOrEmpty(componentSelection))
                    {
                        return featureName + "@" + componentSelection;
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("GetSelectByIDString for component '" + componentName +
                        "' failed; falling back to manual reconstruction: " + ex.Message);
                }
            }

            // Fallback (single-level correct only): manual reverse-join.
            string[] pathSegments = componentName.Split('/');
            Array.Reverse(pathSegments);
            string qualified = featureName + "@" + string.Join("@", pathSegments);

            string assemblyName = GetActiveDocumentSelectionName();
            if (!string.IsNullOrEmpty(assemblyName))
            {
                qualified += "@" + assemblyName;
            }
            return qualified;
        }

        // Resolves a Component2 by its full "/"-separated Name2 path
        // (e.g. "SubAsm-1/Part-1"). Mirrors the component lookup in
        // ExportHelper.ResolveFeatureReference: GetComponents(false) returns
        // every component at every sub-assembly depth, so a deep Name2 match
        // is found directly. Returns null when the active document is not an
        // assembly or no component matches (deleted / renamed since save).
        private Component2 FindComponentByName2(string name2)
        {
            if (string.IsNullOrEmpty(name2))
            {
                return null;
            }
            try
            {
                AssemblyDoc assy = ActiveSWModel as AssemblyDoc;
                if (assy == null)
                {
                    return null;
                }
                object[] components = assy.GetComponents(false);
                if (components == null)
                {
                    return null;
                }
                foreach (object obj in components)
                {
                    if (obj is Component2 comp && comp.Name2 == name2)
                    {
                        return comp;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn("FindComponentByName2('" + name2 + "') failed: " + ex.Message);
            }
            return null;
        }

        // The top-level document name SOLIDWORKS uses as the trailing
        // segment of a component-qualified selection id: the file name
        // without extension. Falls back to the window title for an unsaved
        // document.
        private string GetActiveDocumentSelectionName()
        {
            try
            {
                string path = ActiveSWModel?.GetPathName();
                if (!string.IsNullOrEmpty(path))
                {
                    return System.IO.Path.GetFileNameWithoutExtension(path);
                }
                return ActiveSWModel?.GetTitle();
            }
            catch (Exception ex)
            {
                logger.Warn("GetActiveDocumentSelectionName failed: " + ex.Message);
                return null;
            }
        }

        // Joint Properties round-trip: pulls the URDFAttribute values
        // (or null) out of node.Link.Joint.Limit / Joint.Dynamics /
        // Joint.{Reference, Armature, AutoComputeLimits} and stamps them
        // onto the textboxes / checkbox built by
        // BuildJointPropertiesControls. Empty textbox represents an unset
        // attribute; the writer omits the attribute in that case.
        private void FillJointPropertiesFromLink(Joint joint)
        {
            if (joint == null)
            {
                ClearJointPropertyTextboxes();
                return;
            }
            PMCheckAutoComputeLimits.Checked = joint.AutoComputeLimits;
            PMTextBoxJointLower.Text = FormatJointDouble(joint.Limit?.LowerOrNull);
            PMTextBoxJointUpper.Text = FormatJointDouble(joint.Limit?.UpperOrNull);
            PMTextBoxJointEffort.Text = FormatJointDouble(joint.Limit?.EffortOrNull);
            PMTextBoxJointVelocity.Text = FormatJointDouble(joint.Limit?.VelocityOrNull);
            PMTextBoxJointDamping.Text = FormatJointDouble(joint.Dynamics?.DampingOrNull);
            PMTextBoxJointFriction.Text = FormatJointDouble(joint.Dynamics?.FrictionOrNull);
            PMTextBoxJointArmature.Text = FormatJointDouble(joint.Armature);
            PMTextBoxJointReference.Text = FormatJointDouble(joint.Reference);
            SetAutoComputeLimitEditorEnabled(!joint.AutoComputeLimits);
        }

        // Reads the eight Joint Properties textboxes back onto the data
        // model. Empty / whitespace text means the attribute should be
        // unset (writer omits it); a populated cell is parsed via the
        // *OrClear setters which delegate to URDFAttribute.SetDoubleValueFromString
        // for parse failures (the field is left untouched in that case).
        private void SaveJointPropertiesToLink(Joint joint)
        {
            if (joint == null)
            {
                return;
            }
            joint.AutoComputeLimits = PMCheckAutoComputeLimits.Checked;
            if (TryParseJointDouble(PMTextBoxJointLower.Text, "Lower", out double? lower))
            {
                joint.Limit?.SetLower(lower);
            }
            if (TryParseJointDouble(PMTextBoxJointUpper.Text, "Upper", out double? upper))
            {
                joint.Limit?.SetUpper(upper);
            }
            if (TryParseJointDouble(PMTextBoxJointEffort.Text, "Effort", out double? effort))
            {
                joint.Limit?.SetEffort(effort);
            }
            if (TryParseJointDouble(PMTextBoxJointVelocity.Text, "Velocity", out double? velocity))
            {
                joint.Limit?.SetVelocity(velocity);
            }
            if (TryParseJointDouble(PMTextBoxJointDamping.Text, "Damping", out double? damping))
            {
                joint.Dynamics?.SetDamping(damping);
            }
            if (TryParseJointDouble(PMTextBoxJointFriction.Text, "Friction", out double? friction))
            {
                joint.Dynamics?.SetFriction(friction);
            }
            if (TryParseJointDouble(PMTextBoxJointArmature.Text, "Armature", out double? armature))
            {
                joint.Armature = armature;
            }
            if (TryParseJointDouble(PMTextBoxJointReference.Text, "Reference", out double? reference))
            {
                joint.Reference = reference;
            }
        }

        private void ClearJointPropertyTextboxes()
        {
            if (PMCheckAutoComputeLimits != null)
            {
                PMCheckAutoComputeLimits.Checked = false;
            }
            if (PMTextBoxJointLower != null) PMTextBoxJointLower.Text = "";
            if (PMTextBoxJointUpper != null) PMTextBoxJointUpper.Text = "";
            if (PMTextBoxJointEffort != null) PMTextBoxJointEffort.Text = "";
            if (PMTextBoxJointVelocity != null) PMTextBoxJointVelocity.Text = "";
            if (PMTextBoxJointDamping != null) PMTextBoxJointDamping.Text = "";
            if (PMTextBoxJointFriction != null) PMTextBoxJointFriction.Text = "";
            if (PMTextBoxJointArmature != null) PMTextBoxJointArmature.Text = "";
            if (PMTextBoxJointReference != null) PMTextBoxJointReference.Text = "";
            SetAutoComputeLimitEditorEnabled(false);
        }

        private static string FormatJointDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                : "";
        }

        private bool TryParseJointDouble(string text, string fieldName, out double? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
            if (double.TryParse(text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double result))
            {
                value = result;
                return true;
            }
            logger.Warn("Ignoring invalid Joint Properties value for " + fieldName + ": '" + text + "'.");
            return false;
        }

        private void SetAutoComputeLimitEditorEnabled(bool enabled)
        {
            object[] controls = new object[]
            {
                PMLabelJointLower,
                PMTextBoxJointLower,
                PMLabelJointUpper,
                PMTextBoxJointUpper,
            };
            foreach (object ctl in controls)
            {
                IPropertyManagerPageControl pageControl = ctl as IPropertyManagerPageControl;
                if (pageControl != null)
                {
                    pageControl.Enabled = enabled;
                }
            }
        }

        // Three-way enable/disable layout driven by the active node's role
        // in the WorldNode-rooted tree:
        //
        //   World           : only the Global Origin picker is enabled.
        //                     Visual / Collision / Sites tabs ARE enabled
        //                     (worldbody-direct geometry, MJCF idiom);
        //                     Inertial tab is disabled (worldbody is massless).
        //                     Joint name / coord-sys / axis / type / properties
        //                     are all disabled.
        //   TopLevelBody    : Link coordinate system + World attachment combo
        //                     enabled; Global Origin disabled (the World
        //                     node owns it). Joint name / axis / type /
        //                     properties disabled (top-level bodies have
        //                     no incoming kinematic joint).
        //   NestedLink      : today's behavior - all joint controls enabled,
        //                     Global Origin disabled, World attachment
        //                     disabled.
        //
        // Visibility is FIXED at create-time on every Link/Joint control - we
        // never flip Visible cross-tab. Reason: on SW 2024 a control.Visible
        // flip applied while the user is on a different tab leaks the
        // control onto whichever tab is currently active (the leak appears
        // as joint-name / coord-sys / axis controls suddenly showing up
        // under the Setup or Inertial tab footer). The always-visible-
        // disabled layout matches SW's own coord-sys / mate creation PMs.
        private void EnableControls(bool enableJoints)
        {
            EnableControls(enableJoints
                ? NodeRole.NestedLink
                : NodeRole.WorldOrTopLevelLegacy);
        }

        // Per-link-role enable pass. Use the NodeRole-typed overload from
        // FillPropertyManager; the bool overload above exists only for
        // historical call-site compatibility (legacy "is base" boolean).
        private void EnableControls(NodeRole role)
        {
            bool enableJointInputs = role == NodeRole.NestedLink;
            // The single coordinate-system picker is enabled for every real
            // node: it is the global origin for the World, the world->body
            // offset for a top-level body, and the joint origin for a nested
            // link. All three persist to Link.Joint.CoordinateSystemName.
            bool enableCoordSys = true;
            // The unified joint-type dropdown is enabled for top-level bodies
            // (fixed / free world attachment) and nested links (fixed /
            // revolute / prismatic), and disabled for the World root.
            bool enableJointType = role == NodeRole.TopLevelBody || role == NodeRole.NestedLink;

            // Per-joint inputs (joint name, axis, joint properties). Greyed
            // out on the World node and on top-level bodies; visible at all
            // times so the layout doesn't reflow under the user. The joint
            // TYPE dropdown is NOT in this set - it is enabled for top-level
            // bodies too (fixed / free), so it has its own enable rule below.
            PropertyManagerPageControl[] pmJointControls =
                new PropertyManagerPageControl[] {
                    (PropertyManagerPageControl)PMTextBoxJointName,
                    (PropertyManagerPageControl)PMLabelJointName,
                    (PropertyManagerPageControl)PMLabelAxes,
                    (PropertyManagerPageControl)PMComboBoxAxisSource,
                    (PropertyManagerPageControl)PMSelectionJointAxis,
                    (PropertyManagerPageControl)PMBitmapAxisFlip,
                    (PropertyManagerPageControl)PMLabelJointProperties,
                    (PropertyManagerPageControl)PMCheckAutoComputeLimits,
                    (PropertyManagerPageControl)PMLabelJointLower,
                    (PropertyManagerPageControl)PMTextBoxJointLower,
                    (PropertyManagerPageControl)PMLabelJointUpper,
                    (PropertyManagerPageControl)PMTextBoxJointUpper,
                    (PropertyManagerPageControl)PMLabelJointEffort,
                    (PropertyManagerPageControl)PMTextBoxJointEffort,
                    (PropertyManagerPageControl)PMLabelJointVelocity,
                    (PropertyManagerPageControl)PMTextBoxJointVelocity,
                    (PropertyManagerPageControl)PMLabelJointDamping,
                    (PropertyManagerPageControl)PMTextBoxJointDamping,
                    (PropertyManagerPageControl)PMLabelJointFriction,
                    (PropertyManagerPageControl)PMTextBoxJointFriction,
                    (PropertyManagerPageControl)PMLabelJointArmature,
                    (PropertyManagerPageControl)PMTextBoxJointArmature,
                    (PropertyManagerPageControl)PMLabelJointReference,
                    (PropertyManagerPageControl)PMTextBoxJointReference };

            // The single coordinate-system picker. Enabled for every real
            // node (global origin / world->body offset / joint origin).
            PropertyManagerPageControl[] pmCoordSysControls = new PropertyManagerPageControl[] {
                (PropertyManagerPageControl)PMSelectionJointCoordsys,
                (PropertyManagerPageControl)PMLabelCoordSys};

            // The unified joint-type dropdown + its label. Enabled for
            // top-level bodies and nested links, disabled for the World root.
            PropertyManagerPageControl jointTypeCombo =
                PMComboBoxJointType as PropertyManagerPageControl;
            PropertyManagerPageControl jointTypeLabel =
                PMLabelJointType as PropertyManagerPageControl;

            foreach (PropertyManagerPageControl control in pmCoordSysControls)
            {
                control.Enabled = enableCoordSys;
            }
            foreach (PropertyManagerPageControl control in pmJointControls)
            {
                control.Enabled = enableJointInputs;
            }
            SetAutoComputeLimitEditorEnabled(enableJointInputs && !(PMCheckAutoComputeLimits?.Checked ?? false));
            if (jointTypeCombo != null)
            {
                jointTypeCombo.Enabled = enableJointType;
            }
            if (jointTypeLabel != null)
            {
                jointTypeLabel.Enabled = enableJointType;
            }
        }

        // Per-active-node role used by EnableControls. WorldOrTopLevelLegacy
        // captures the legacy bool=false behavior (today's "base link"
        // shape) that is still reachable from a few internal call sites.
        private enum NodeRole
        {
            World = 0,
            TopLevelBody = 1,
            NestedLink = 2,

            // Legacy compatibility: today's pre-refactor "base link" was
            // both the global-origin holder AND a body. The bool overload
            // of EnableControls maps `enableJoints=false` to this.
            WorldOrTopLevelLegacy = 3,
        }

        private static NodeRole ResolveNodeRole(LinkNode node)
        {
            if (node is WorldNode)
            {
                return NodeRole.World;
            }
            if (node != null && node.IsTopLevelBody)
            {
                return NodeRole.TopLevelBody;
            }
            return NodeRole.NestedLink;
        }

        // Item ordering for the nested-link joint-type dropdown. NOTE: no
        // leading empty item - SolidWorks comboboxes silently DROP empty
        // string entries from AddItems, so a leading "" would shift every
        // real item up by one slot in the live control versus this array.
        // Incompleteness (an unset joint) is tracked in the data model
        // (Joint.Type == "") and surfaced by validation, not by a blank combo
        // row. Used by PopulateJointTypeComboForRole.
        private static readonly string[] NestedJointTypeItems =
            new string[] { "fixed", "revolute", "prismatic" };

        // Item ordering for the top-level-body joint-type dropdown. Index
        // MUST line up with WorldAttachmentModel (Welded = 0 -> "fixed",
        // Free = 1 -> "free") so the combobox index casts straight to the
        // enum in OnComboboxSelectionChanged / SaveActiveNode.
        private static readonly string[] TopLevelJointTypeItems =
            new string[] { "fixed", "free" };

        // Repopulates the single role-aware joint-type dropdown and restores
        // its selection for the active node. World -> empty (disabled via
        // EnableControls); TopLevelBody -> {fixed, free} from WorldAttachment;
        // NestedLink -> {fixed, revolute, prismatic} from Joint.Type.
        private void PopulateJointTypeComboForRole(LinkNode node, NodeRole role)
        {
            if (PMComboBoxJointType == null)
            {
                return;
            }
            PMComboBoxJointType.Clear();
            if (role == NodeRole.TopLevelBody)
            {
                // {fixed, free} has no empty item, so index == enum value.
                PMComboBoxJointType.AddItems(TopLevelJointTypeItems);
                PMComboBoxJointType.CurrentSelection =
                    (node.Link.WorldAttachment == SW2RD.Core.WorldAttachmentModel.Free)
                        ? (short)1 : (short)0;
            }
            else if (role == NodeRole.NestedLink)
            {
                PMComboBoxJointType.AddItems(NestedJointTypeItems);
                // Select by matching the ACTUAL combobox item text rather than
                // by index into NestedJointTypeItems. SolidWorks may drop
                // empty entries (and the live order is the source of truth),
                // so resolving the index from get_ItemText is immune to any
                // add-time reshuffling. SaveActiveNode reads the selection
                // back via get_ItemText(-1), so a correct visual selection is
                // also a correct round-trip.
                PMComboBoxJointType.CurrentSelection =
                    ResolveComboIndexByText(PMComboBoxJointType, node.Link?.Joint?.Type);
            }
            // World role: leave the dropdown empty; EnableControls disables it.
        }

        // Finds the index of the item whose text equals `target` in a live
        // combobox by scanning get_ItemText. Returns 0 (the first item) when
        // `target` is null / empty / not found. Does NOT terminate on an empty
        // item text (unlike SelectComboBox) so a leading blank can't short-
        // circuit the scan; bounded so a missing target can't loop forever.
        private static short ResolveComboIndexByText(PropertyManagerPageCombobox box, string target)
        {
            if (box == null || string.IsNullOrEmpty(target))
            {
                return 0;
            }
            const short scanCap = 16;
            for (short k = 0; k < scanCap; k++)
            {
                string itemtext = box.get_ItemText(k);
                if (itemtext == null)
                {
                    break;
                }
                if (itemtext == target)
                {
                    return k;
                }
            }
            return 0;
        }

        //Populates the TreeView with the organized links from the robot
        public void FillTreeViewFromRobot(Robot robot)
        {
            Tree.Nodes.Clear();
            LinkNode baseNode = new LinkNode();
            Link baseLink = robot.BaseLink;
            baseNode.Name = baseLink.Name;
            baseNode.Text = baseLink.Name;
            baseNode.Link = baseLink;
            baseNode.ContextMenuStrip = docMenu;

            foreach (Link child in baseLink.Children)
            {
                baseNode.Nodes.Add(CreateLinkNodeFromLink(child));
            }
            Tree.Nodes.Add(baseNode);
            Tree.ExpandAll();
        }

        // Creates a LinkNode (the WinForms TreeView's per-row payload) from a
        // deserialized Link by recursively wrapping each child Link in a
        // LinkNode and clearing the embedded Link.Children list (the LinkNode
        // hierarchy is the source of truth for tree shape inside the PMPage).
        public LinkNode CreateLinkNodeFromLink(Link Link)
        {
            LinkNode node = new LinkNode();
            node.Name = Link.Name;
            node.Text = Link.Name;
            node.Link = Link;
            node.ContextMenuStrip = docMenu;

            foreach (Link child in Link.Children)
            {
                node.Nodes.Add(CreateLinkNodeFromLink(child));
            }

            // Need to erase the children from the embedded link because they may be rearranged later.
            node.Link.Children.Clear();
            return node;
        }

        /// <summary>
        /// Loads configuration tree into PM Page. If an error occurs, this will do nothing
        /// </summary>
        /// <returns>bool representing success of load. If false, PMPage should not open</returns>
        public bool LoadConfigTree()
        {
            LinkNode baseNode = ConfigurationSerialization.LoadBaseNodeFromModel(ActiveSWModel, out bool abortProcess);

            if (abortProcess)
            {
                MessageBox.Show("An error occured loading an existing configuration. Either resolve the issue" +
                    " or delete the configuration from the feature manager");
                return false;
            }

            SetConfigTree(baseNode);
            UpdateSetupConfigurationActions();

            return true;
        }

        private void ClearSavedConfigurationFromForm()
        {
            DialogResult answer = MessageBox.Show(
                "Clear the saved SW2RD export configuration from this model and start a fresh tree?\r\n\r\n" +
                "This cannot be undone.",
                "Clear Saved Export Configuration",
                MessageBoxButtons.YesNo);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            bool cleared;
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                ReplaceConfigTree(null);
                cleared = ConfigurationSerialization.ClearSavedConfiguration(ActiveSWModel);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
            UpdateSetupConfigurationActions();

            if (PMLabelValidationStatus != null)
            {
                PMLabelValidationStatus.Caption = cleared
                    ? "Status: Cleared saved configuration. Fresh tree started."
                    : "Status: Fresh tree started. No saved SW2RD configuration was found to delete.";
            }
        }

        private void ReplaceConfigTree(LinkNode baseNode)
        {
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                SaveActiveNode();
                previouslySelectedNode = null;
                rightClickedNode = null;
                activeVisualGroupIndex = -1;
                activeCollisionGroupIndex = -1;
                activeSiteIndex = -1;
                currentAxisFlipped = false;
                Exporter.ClearAxisOverlay();
                SetConfigTree(baseNode);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
        }

        private void SetConfigTree(LinkNode baseNode)
        {
            if (baseNode == null)
            {
                logger.Info("Starting new configuration");
                baseNode = CreateEmptyNode(null);
            }
            else
            {
                List<string> problemLinks = new List<string>();
                // Phase titles on the open-time busy indicator (no-op if no
                // progress bar is active). These two recursive walks are the bulk
                // of the open-time stall on large assemblies.
                SwProgress.SetTitle("Resolving link components...");
                CommonSwOperations.LoadSWComponents(ActiveSWModel, baseNode, problemLinks);
                SwProgress.SetTitle("Validating coordinate systems, axes, and sites...");
                ValidateFeatureReferences(baseNode, problemLinks);

                if (problemLinks.Count > 0)
                {
                    string msg = "Some saved references (components, coordinate systems, or axes) could " +
                        "not be found in this assembly and are shown as missing. They were preserved in " +
                        "the configuration so you can repair or remove them - re-pick a component or " +
                        "feature to repair the reference, or delete it. (A reference can go missing if " +
                        "its file / feature was renamed or deleted, or its reference went stale after a " +
                        "PDM pull.)\r\n\r\n" +
                        string.Join("\r\n", problemLinks);
                    MessageBox.Show(msg);
                }
            }

            NormalizeJointTypesForUi(baseNode);
            AddDocMenu(baseNode);

            Tree.Nodes.Clear();
            Tree.Nodes.Add(baseNode);
            Tree.ExpandAll();
            Tree.SelectedNode = Tree.Nodes[0];
        }

        // Walks the loaded link tree and flags any saved coordinate-system /
        // joint-axis / site coordinate-system whose feature no longer exists in
        // the assembly (renamed or deleted). Unlike components, these resolve by
        // feature NAME (no persistent ID), so "missing" simply means the name is
        // gone. Findings are appended to problemLinks and surfaced in the same
        // load-time warning as missing components. The check is read-only and
        // conservative: component-scoped names are skipped (see
        // ExportHelper.ReferenceFeatureExists) so it never raises a false alarm.
        private void ValidateFeatureReferences(LinkNode node, List<string> problemLinks)
        {
            if (node == null || node.Link == null || Exporter == null)
            {
                return;
            }

            Joint joint = node.Link.Joint;
            if (joint != null)
            {
                if (!string.IsNullOrEmpty(joint.CoordinateSystemName) &&
                    !Exporter.ReferenceFeatureExists("CoordSys", joint.CoordinateSystemName))
                {
                    problemLinks.Add(node.Name + " (coordinate system): '" + joint.CoordinateSystemName + "'");
                }

                // Axis only applies to nested links with an explicit (non
                // auto-derived) axis pick. The legacy "Automatically Generate"
                // sentinel is treated like auto-derive.
                bool axisApplies = !node.IsBaseNode && !node.IsTopLevelBody &&
                    !joint.AutoDeriveAxis &&
                    !string.IsNullOrEmpty(joint.AxisName) &&
                    joint.AxisName != "Automatically Generate";
                if (axisApplies && !Exporter.ReferenceFeatureExists("RefAxis", joint.AxisName))
                {
                    problemLinks.Add(node.Name + " (joint axis): '" + joint.AxisName + "'");
                }
            }

            if (node.Link.Sites != null)
            {
                foreach (SiteSpec site in node.Link.Sites)
                {
                    if (site == null)
                    {
                        continue;
                    }
                    if (site.Source == SiteSourceType.ReferencePoint)
                    {
                        if (!string.IsNullOrEmpty(site.ReferencePointName) &&
                            !Exporter.ReferenceFeatureExists("RefPoint", site.ReferencePointName))
                        {
                            problemLinks.Add(node.Name + " (site '" + (site.Name ?? "") +
                                "' reference point): '" + site.ReferencePointName + "'");
                        }
                    }
                    else if (!string.IsNullOrEmpty(site.CoordinateSystemName) &&
                        !Exporter.ReferenceFeatureExists("CoordSys", site.CoordinateSystemName))
                    {
                        problemLinks.Add(node.Name + " (site '" + (site.Name ?? "") +
                            "' coordinate system): '" + site.CoordinateSystemName + "'");
                    }
                }
            }

            foreach (System.Windows.Forms.TreeNode child in node.Nodes)
            {
                ValidateFeatureReferences(child as LinkNode, problemLinks);
            }
        }

        private static void NormalizeJointTypesForUi(LinkNode node)
        {
            if (node == null)
            {
                return;
            }

            if (!(node is WorldNode) && !node.IsTopLevelBody && node.Link?.Joint != null)
            {
                string jointType = node.Link.Joint.Type;
                if (jointType == "continuous")
                {
                    node.Link.Joint.Type = "revolute";
                }
                else if (jointType == "Automatically Detect" || jointType == "Automatically Generate")
                {
                    node.Link.Joint.Type = "";
                }
            }

            foreach (TreeNode child in node.Nodes)
            {
                NormalizeJointTypesForUi(child as LinkNode);
            }
        }

        public void MoveComponentsToFolder(LinkNode node)
        {
            bool needToCreateFolder = true;
            Object[] objects = ActiveSWModel.FeatureManager.GetFeatures(true);
            foreach (Object obj in objects)
            {
                Feature feat = (Feature)obj;
                if (feat.Name == "Robot Description Export Items")
                {
                    needToCreateFolder = false;
                }
            }
            ActiveSWModel.ClearSelection2(true);
            ActiveSWModel.Extension.SelectByID2(
                "Origin_global", "COORDSYS", 0, 0, 0, true, 0, null, 0);
            if (needToCreateFolder)
            {
                Feature folderFeature =
                    ActiveSWModel.FeatureManager.InsertFeatureTreeFolder2(
                        (int)swFeatureTreeFolderType_e.swFeatureTreeFolder_Containing);
                folderFeature.Name = "Robot Description Export Items";
            }
            ActiveSWModel.Extension.SelectByID2
                ("Robot Description Reference", "SKETCH", 0, 0, 0, true, 0, null, 0);
            ActiveSWModel.FeatureManager.MoveToFolder("Robot Description Export Items", "", false);
            ActiveSWModel.Extension.SelectByID2
                (ConfigurationSerialization.ConfigurationSwAttributeName, "ATTRIBUTE", 0, 0, 0, true, 0, null, 0);
            ActiveSWModel.FeatureManager.MoveToFolder("Robot Description Export Items", "", false);
            SelectFeatures(node);
            ActiveSWModel.FeatureManager.MoveToFolder("Robot Description Export Items", "", false);
        }

        public void SelectFeatures(LinkNode node)
        {
            ActiveSWModel.Extension.SelectByID2(
                node.Link.Joint.CoordinateSystemName, "COORDSYS", 0, 0, 0, true, -1, null, 0);
            if (node.Link.Joint.AxisName != "None")
            {
                ActiveSWModel.Extension.SelectByID2(
                    node.Link.Joint.AxisName, "AXIS", 0, 0, 0, true, -1, null, 0);
            }
            foreach (LinkNode child in node.Nodes)
            {
                SelectFeatures(child);
            }
        }

    }
}