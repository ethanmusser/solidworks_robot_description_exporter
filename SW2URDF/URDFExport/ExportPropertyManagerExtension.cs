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
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    public partial class ExportPropertyManager : PropertyManagerPage2Handler9
    {
        public static readonly double ConfigurationVersion = 1.3;
        public static readonly double SoapMinVersion = 1.3;

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
            int itemsCount = CommonSwOperations.GetCount(Tree.Nodes);
            int itemHeight = 1 + itemsCount * Tree.ItemHeight;
            int min = 163;
            int max = 600;

            int height = MathOps.Envelope(itemHeight, min, max);
            Tree.Height = height;
            PMTree.Height = height;
            currentNode.ExpandAll();
        }

        // When a new node is selected or another node is found that needs to be visited, this
        // method saves the previously active node and fills in the property mananger with the new one
        public void SwitchActiveNodes(LinkNode node)
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
                if (!previouslySelectedNode.IsBaseNode)
                {
                    previouslySelectedNode.Link.Joint.Name = PMTextBoxJointName.Text;
                    previouslySelectedNode.Link.Joint.Type = PMComboBoxJointType.get_ItemText(-1);
                    // currentAxisFlipped is also written through immediately on
                    // every bitmap-button click in OnButtonPress so the overlay
                    // arrow and persisted state stay in lockstep; this
                    // node-switch save is the safety net for any other path.
                    previouslySelectedNode.Link.Joint.AxisFlipped = currentAxisFlipped;

                    // PMCheckAutoDeriveAxis is also written through on
                    // every checkbox toggle via OnCheckboxCheck; this
                    // node-switch save mirrors AxisFlipped above as the
                    // safety net.
                    if (PMCheckAutoDeriveAxis != null)
                    {
                        previouslySelectedNode.Link.Joint.AutoDeriveAxis = PMCheckAutoDeriveAxis.Checked;
                    }

                    // Joint Properties section. Each *OrClear setter
                    // either parses a populated textbox or clears the
                    // underlying URDFAttribute so the writer omits the
                    // attribute entirely. Reference / Armature / the
                    // auto-compute toggle are plain Joint fields and use
                    // local helpers.
                    SaveJointPropertiesToLink(previouslySelectedNode.Link.Joint);
                }

                EnsureGroupsInitialized(previouslySelectedNode);

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
            LinkNode node = new LinkNode();

            if (Parent == null)             //For the base_link node
            {
                node.Link.Name = "base_link";
                node.Link.Joint.AxisName = "";
                node.Link.Joint.CoordinateSystemName = "";
                node.Link.InertialComponents = new List<Component2>();
                node.Link.Sites = new List<SiteSpec>();
                node.Link.InertialSource = InertialSource.Visual;
                node.IsBaseNode = true;
                node.IsIncomplete = true;
            }
            else
            {
                node.IsBaseNode = false;
                node.Link.Name = "Empty_Link";
                // SelectionBox-only UI: empty AxisName /
                // CoordinateSystemName + AutoDeriveAxis = true is the
                // new "let the exporter figure it out" state. Replaces
                // the legacy "Automatically Generate" sentinel that
                // older configs still write.
                node.Link.Joint.AxisName = "";
                node.Link.Joint.CoordinateSystemName = "";
                node.Link.Joint.AutoDeriveAxis = true;
                node.Link.Joint.Type = "Automatically Detect";
                node.Link.InertialComponents = new List<Component2>();
                node.Link.Sites = new List<SiteSpec>();
                node.Link.InertialSource = InertialSource.Visual;
                node.IsBaseNode = false;
                node.IsIncomplete = true;
            }
            // Seed a single empty visual group so the SelectionBox always has
            // a place to commit user picks into. Collision starts empty so the
            // URDF/MJCF fallback "reuse visual mesh" behavior kicks in until
            // the user explicitly creates a collision group.
            node.Link.VisualGroups = new List<MeshGroup>
            {
                new MeshGroup(MeshGroup.DefaultVisualName(node.Link.Name)),
            };
            node.Link.CollisionGroups = new List<MeshGroup>();
            node.Name = node.Link.Name;
            node.Text = node.Link.Name;
            node.ContextMenuStrip = docMenu;
            return node;
        }

        //Sets all the controls in the Property Manager from the Selected Node
        public void FillPropertyManager(LinkNode node)
        {
            PMTextBoxLinkName.Text = node.Link.Name;
            PMNumberBoxChildCount.Value = node.Nodes.Count;

            // Migrate any legacy single-list config into the new groups model
            // and ensure the lists are non-null. After this call, node.Link
            // always has at least one VisualGroup (so the SelectionBox has a
            // place to commit into).
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
            // each link switch.
            activeVisualGroupIndex = 0;
            activeCollisionGroupIndex = (node.Link.CollisionGroups.Count > 0) ? 0 : -1;

            // Refresh the listboxes BEFORE re-populating selection boxes; the
            // selection box population calls ClearSelection2.
            RefreshVisualGroupsListbox(node);
            RefreshCollisionGroupsListbox(node);

            // Toggle Enabled state on the Link/Joint controls BEFORE
            // populating the SelectionBoxes. SW occasionally drops
            // SelectionBox display contents when a control's Enabled
            // state flips after the box has been populated; doing the
            // Enable pass first means every load runs against settled
            // controls.
            EnableControls(!node.IsBaseNode);

            // Repopulate ONLY the SelectionBox marks owned by the
            // currently-active tab so the SOLIDWORKS viewer highlight
            // shows the entities for the tab the user is looking at
            // (rather than the union of every SelectionBox that ever
            // held a pick for this link). Every other mark is drained
            // first. See RehydrateMarksForActiveTab and AGENTS.md for
            // the per-tab mark mapping. OnTabClicked uses the same
            // helper, so a tab change after this point also keeps the
            // viewer in sync.
            RehydrateMarksForActiveTab(node, currentActiveTabId);

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

            // Sites tab: nothing to pre-populate for the SelectionBox
            // (the pick is consumed at Add Site click time, not
            // round-tripped). Just clear the name input and refresh the
            // listbox of already-saved sites.
            PMTextBoxSiteName.Text = "";
            RefreshSitesListbox(node);

            // "Use visual groups as collision" toggle. Re-sync the editor
            // visibility on every node switch so the UI matches the data
            // model.
            PMCheckCollisionUsesVisual.Checked = node.Link.CollisionUsesVisual;
            SetCollisionEditorEnabled(!node.Link.CollisionUsesVisual);

            //Setting joint properties (controls already Enable-toggled above
            //before the SelectionBox loads).
            if (!node.IsBaseNode && node.Parent != null)
            {
                PMTextBoxJointName.Text = node.Link.Joint.Name;
                PMLabelParentLink.Caption = node.Parent.Name;

                SelectComboBox(PMComboBoxJointType, node.Link.Joint.Type);

                // Auto-derive axis toggle: the SelectionBox is disabled
                // (and remains empty) when the toggle is on; otherwise
                // LoadActiveJointAxisIntoSelectionBox above will have
                // populated it.
                if (PMCheckAutoDeriveAxis != null)
                {
                    PMCheckAutoDeriveAxis.Checked = node.Link.Joint.AutoDeriveAxis;
                }
                SetAxisPickerEnabled(!node.Link.Joint.AutoDeriveAxis);

                // Restore the persisted "Reverse Direction" toggle for this
                // joint and (re)render the overlay arrow in the model view so
                // the user sees the saved direction as soon as they land on
                // the node. The bitmap button has no visual "pressed" state -
                // the overlay arrow IS the feedback.
                currentAxisFlipped = node.Link.Joint.AxisFlipped;
                RefreshAxisDirectionPreview();

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
                PMLabelParentLink.Caption = " ";
                SelectComboBox(PMComboBoxJointType, "");

                // Base node has no joint axis: clear any previously-rendered
                // overlay so we don't leave a stale arrow in the viewport.
                currentAxisFlipped = false;
                Exporter.ClearAxisOverlay();

                // Base node has no joint properties; clear the textboxes
                // so the next non-base node load starts from a clean
                // slate.
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
        private void LoadActiveGlobalCoordsysIntoSelectionBox(LinkNode node)
        {
            if (node == null || PMSelectionGlobalCoordsys == null)
            {
                return;
            }
            // Always clear the mark first so the previous link's pick
            // doesn't bleed into a non-base node where this loader
            // returns early. Without the clear, mark 21 would retain
            // stale content across base->non-base navigation.
            CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionGlobalCoordsys.Mark);
            // Only the base node's joint owns the global coord-sys.
            if (!node.IsBaseNode)
            {
                return;
            }
            string name = node.Link?.Joint?.CoordinateSystemName;
            if (!IsRealFeatureName(name))
            {
                return;
            }
            SelectFeatureIntoMark(name, "COORDSYS", PMSelectionGlobalCoordsys.Mark);
        }

        private void LoadActiveJointCoordsysIntoSelectionBox(LinkNode node)
        {
            if (node == null || PMSelectionJointCoordsys == null)
            {
                return;
            }
            CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionJointCoordsys.Mark);
            if (node.IsBaseNode)
            {
                return;
            }
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
            if (joint == null || joint.AutoDeriveAxis)
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
        private void SelectFeatureIntoMark(string featureName, string typeName, int mark)
        {
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                ActiveSWModel.Extension.SelectByID2(
                    featureName, typeName, 0, 0, 0, true, mark, null, 0);
            }
            catch (Exception ex)
            {
                logger.Warn("SelectByID2 for " + typeName + " '" + featureName + "' failed: " + ex.Message);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
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
            joint.Limit?.SetLowerOrClear(PMTextBoxJointLower.Text);
            joint.Limit?.SetUpperOrClear(PMTextBoxJointUpper.Text);
            joint.Limit?.SetEffortOrClear(PMTextBoxJointEffort.Text);
            joint.Limit?.SetVelocityOrClear(PMTextBoxJointVelocity.Text);
            joint.Dynamics?.SetDampingOrClear(PMTextBoxJointDamping.Text);
            joint.Dynamics?.SetFrictionOrClear(PMTextBoxJointFriction.Text);
            joint.Armature = ParseJointDouble(PMTextBoxJointArmature.Text);
            joint.Reference = ParseJointDouble(PMTextBoxJointReference.Text);
        }

        private void ClearJointPropertyTextboxes()
        {
            if (PMCheckAutoComputeLimits != null)
            {
                PMCheckAutoComputeLimits.Checked = true;
            }
            if (PMTextBoxJointLower != null) PMTextBoxJointLower.Text = "";
            if (PMTextBoxJointUpper != null) PMTextBoxJointUpper.Text = "";
            if (PMTextBoxJointEffort != null) PMTextBoxJointEffort.Text = "";
            if (PMTextBoxJointVelocity != null) PMTextBoxJointVelocity.Text = "";
            if (PMTextBoxJointDamping != null) PMTextBoxJointDamping.Text = "";
            if (PMTextBoxJointFriction != null) PMTextBoxJointFriction.Text = "";
            if (PMTextBoxJointArmature != null) PMTextBoxJointArmature.Text = "";
            if (PMTextBoxJointReference != null) PMTextBoxJointReference.Text = "";
        }

        private static string FormatJointDouble(double? value)
        {
            return value.HasValue
                ? value.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                : "";
        }

        private static double? ParseJointDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            if (double.TryParse(text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double result))
            {
                return result;
            }
            return null;
        }

        // Toggles Enabled state of joint / global-origin controls on the
        // Link/Joint tab as the active link changes. Visibility is FIXED
        // at create-time on every Link/Joint control - we never flip
        // Visible cross-tab. Reason: on SW 2024 a control.Visible flip
        // applied while the user is on a different tab leaks the
        // control onto whichever tab is currently active (the leak
        // appears as joint-name / coord-sys / axis controls suddenly
        // showing up under the Setup or Inertial tab footer). The
        // always-visible-disabled layout matches SW's own coord-sys /
        // mate creation PMs - the user sees the joint row at all
        // times; the base node greys out joint-only inputs and the
        // global-origin SelectionBox stays enabled, and conversely a
        // non-base node enables joint inputs and greys out the
        // global-origin box.
        private void EnableControls(bool enableJoints)
        {
            // Per-joint inputs (joint name, axis, type, joint properties).
            // Greyed out on the base node which owns no joint; visible at
            // all times so the layout doesn't reflow under the user.
            PropertyManagerPageControl[] pmJointControls =
                new PropertyManagerPageControl[] {
                    (PropertyManagerPageControl)PMTextBoxJointName,
                    (PropertyManagerPageControl)PMLabelJointName,
                    (PropertyManagerPageControl)PMLabelAxes,
                    (PropertyManagerPageControl)PMCheckAutoDeriveAxis,
                    (PropertyManagerPageControl)PMSelectionJointAxis,
                    (PropertyManagerPageControl)PMBitmapAxisFlip,
                    (PropertyManagerPageControl)PMComboBoxJointType,
                    (PropertyManagerPageControl)PMLabelJointType,
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

            // Base-node-only controls. The global origin SelectionBox +
            // its label are enabled when EnableControls is called with
            // enableJoints=false (the user is on the base node) and
            // greyed out otherwise.
            PropertyManagerPageControl[] pmGlobalOriginControls = new PropertyManagerPageControl[] {
                (PropertyManagerPageControl)PMSelectionGlobalCoordsys,
                (PropertyManagerPageControl)PMLabelGlobalCoordsys};

            // Per-joint reference-coord-system controls. Greyed out on the
            // base node, enabled on non-base nodes - inverse of the
            // global-origin pair above.
            PropertyManagerPageControl[] pmJointOriginControls = new PropertyManagerPageControl[] {
                (PropertyManagerPageControl)PMSelectionJointCoordsys,
                (PropertyManagerPageControl)PMLabelCoordSys};

            foreach (PropertyManagerPageControl control in pmGlobalOriginControls)
            {
                control.Enabled = !enableJoints;
            }
            foreach (PropertyManagerPageControl control in pmJointOriginControls)
            {
                control.Enabled = enableJoints;
            }
            foreach (PropertyManagerPageControl control in pmJointControls)
            {
                control.Enabled = enableJoints;
            }
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

            return true;
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
                CommonSwOperations.LoadSWComponents(ActiveSWModel, baseNode, problemLinks);

                if (problemLinks.Count > 0)
                {
                    string msg = "The following links had issues loading their associated SolidWorks components. " +
                        "Please inspect before exporting\r\n\r\n" +
                        string.Join(", ", problemLinks);
                    MessageBox.Show(msg);
                }
            }

            AddDocMenu(baseNode);

            Tree.Nodes.Clear();
            Tree.Nodes.Add(baseNode);
            Tree.ExpandAll();
            Tree.SelectedNode = Tree.Nodes[0];
        }

        public void MoveComponentsToFolder(LinkNode node)
        {
            bool needToCreateFolder = true;
            Object[] objects = ActiveSWModel.FeatureManager.GetFeatures(true);
            foreach (Object obj in objects)
            {
                Feature feat = (Feature)obj;
                if (feat.Name == "URDF Export Items")
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
                folderFeature.Name = "URDF Export Items";
            }
            ActiveSWModel.Extension.SelectByID2
                ("URDF Reference", "SKETCH", 0, 0, 0, true, 0, null, 0);
            ActiveSWModel.FeatureManager.MoveToFolder("URDF Export Items", "", false);
            ActiveSWModel.Extension.SelectByID2
                (ConfigurationSerialization.UrdfConfigurationSwAttributeName, "ATTRIBUTE", 0, 0, 0, true, 0, null, 0);
            ActiveSWModel.FeatureManager.MoveToFolder("URDF Export Items", "", false);
            SelectFeatures(node);
            ActiveSWModel.FeatureManager.MoveToFolder("URDF Export Items", "", false);
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