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
using System.Diagnostics;
using System.Windows.Forms;

namespace SW2RD.Export
{
    // PropertyManagerPage2Handler9 callback implementations. Split out of
    // ExportPropertyManager.cs as part of the Phase 1 partial-class refactor;
    // no behavior changes. Keeps the handler methods (which SolidWorks
    // dispatches via COM) away from the per-section editor logic.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        #region Implemented Property Manager Page Handler Methods

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            //Turns the selection box blue so that selected components are added to the PMPage
            // selection box. The visual SelectionBox only exists in Configure
            // mode; in Export mode there are no component pickers, so guard
            // against the null.
            PMSelectionVisual?.SetSelectionFocus();
        }

        // Button-press dispatcher: maps the COM-supplied control ID to the
        // appropriate per-section handler. The IPropertyManagerPage2Handler9
        // entry point below wraps this in try/catch so a per-button bug
        // doesn't tear down the whole PM session.
        private void OnButtonPress(int Id)
        {
            switch (Id)
            {
                case ButtonExportID:
                    ExportButtonPress();
                    break;

                case SitesAddButtonID:
                    AddSiteFromForm();
                    break;

                case SitesRemoveButtonID:
                    RemoveSelectedSiteFromForm();
                    break;

                case VisualGroupsAddButtonID:
                    AddVisualGroupFromForm();
                    break;

                case VisualGroupsRemoveButtonID:
                    RemoveSelectedVisualGroupFromForm();
                    break;

                case CollisionGroupsAddButtonID:
                    AddCollisionGroupFromForm();
                    break;

                case CollisionGroupsRemoveButtonID:
                    RemoveSelectedCollisionGroupFromForm();
                    break;

                case BitmapAxisFlipID:
                    ToggleAxisFlip();
                    break;

                default:
                    break;
            }
        }

        // Called when a PropertyManagerPageButton is pressed. In our case, that's only the
        // export button for now
        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            try
            {
                OnButtonPress(Id);
            }
            catch (Exception e)
            {
                logger.Error("Exception caught handling button press " + Id, e);
                MessageBox.Show("There was a problem with the configuration property manager: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            // Marked selections owned by the SelectionBoxes are released by
            // SolidWorks before OnClose runs, so SaveActiveNode must not try
            // to refresh the active link's component lists from the SelectionMgr
            // (the read would return 0 items and clobber data the user committed
            // via OnSelectionboxListChanged). The pageIsClosing guard makes the
            // SelectionMgr-derived commits no-op for the duration of this call.
            pageIsClosing = true;
            try
            {
                // Only the Configure PMP edits and persists the kinematic
                // tree. In Export mode the tree was loaded read-only from the
                // saved attribute; there are no config-editing controls to
                // commit and re-writing the attribute would be a no-op at best
                // (and SaveActiveNode touches Configure-only controls that are
                // null here). The Export PMP closes via PMPage.Close(true)
                // from ExportButtonPress after the export has already run, so
                // OnClose here just needs to clear the axis overlay below.
                if (mode == ExportPmMode.Configure)
                {
                    if (Reason ==
                        (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Cancel)
                    {
                        logger.Info("Configuration canceled");
                        SaveActiveNode();
                    }
                    else if (Reason ==
                        (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
                    {
                        logger.Info("Configuration saved");
                        SaveActiveNode();
                        SaveConfigTree(ActiveSWModel, (LinkNode)Tree.Nodes[0], false);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception caught on close ", e);
                MessageBox.Show("There was a problem closing the property manager: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
            finally
            {
                pageIsClosing = false;
                // Clear any axis overlay arrow we drew via IBody2.Display3.
                // Transient bodies are session-scoped (not saved with the
                // document) but they remain visible in the viewport until
                // explicitly hidden, so we must drop our refs here.
                try
                {
                    Exporter.ClearAxisOverlay();
                }
                catch (Exception ex)
                {
                    logger.Warn("Failed to clear axis overlay on PM close: " + ex.Message);
                }
                // NOTE: we deliberately do NOT call Dispose() here. The Tree
                // TreeView's child nodes (the LinkNode hierarchy) get detached
                // by ExportButtonPress (Tree.Nodes.Remove(BaseNode)) and then
                // walked by Exporter.CreateRobotFromTreeView and FinishExport;
                // disposing Tree mid-flow would invalidate those TreeNodes
                // (they would still hold a stale TreeView reference and any
                // subsequent TreeNodeCollection.Add would throw "Cannot add or
                // insert ... in more than one place"). Dispose() is available
                // for callers that want to release the .NET Forms resources
                // once the export workflow has fully detached BaseNode from
                // Tree.
            }
        }

        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id)
        {
        }

        bool IPropertyManagerPage2Handler9.OnHelp()
        {
            return true;
        }

        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id)
        {
            if (Wparam == (int)Keys.Enter)
            {
                return true;
            }
            return false;
        }

        void IPropertyManagerPage2Handler9.OnLostFocus(int Id)
        {
            Debug.Print("Control box " + Id + " has lost focus");
        }

        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value)
        {
            if (Id == NumBoxChildCountID)
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                CreateNewNodes(node);
            }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id)
        {
            Debug.Print("The focus has moved to selection box " + Id);
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            // Move focus to next selection box if right-mouse button pressed
            PMPage.SetCursor((int)swPropertyManagerPageCursors_e.swPropertyManagerPageCursors_Advance);

            // The Visual / Collision SelectionBoxes mirror the active group's
            // components; when the user adds or removes a pick we must commit
            // back to the group and rebuild the listbox row text so the
            // "(N comp.)" count stays in sync without requiring a re-click.
            // The suppress flag short-circuits programmatic populates done by
            // LoadActive*GroupIntoSelectionBox / FillPropertyManager.
            if (suppressGroupListboxRefresh)
            {
                return;
            }

            // Skip when the page is in the middle of closing. SolidWorks
            // releases marked selections at PMPage teardown, which can
            // re-enter this handler with Count=0; the destructive Clear+
            // refill in CommitActive*GroupSelection would wipe the last-
            // edited link's groups in that case.
            if (pageIsClosing)
            {
                return;
            }

            LinkNode active = (Tree != null) ? (LinkNode)Tree.SelectedNode : null;
            if (active == null)
            {
                return;
            }

            if (Id == SelectionVisualID)
            {
                CommitActiveVisualGroupSelection(active);
                RefreshVisualGroupsListbox(active);
            }
            else if (Id == SelectionCollisionID)
            {
                CommitActiveCollisionGroupSelection(active);
                RefreshCollisionGroupsListbox(active);
            }
            else if (Id == SelectionInertialID)
            {
                // Mirror the visual / collision pattern: commit on every pick
                // so InertialComponents stays current without depending on the
                // SelectionMgr being live during OnClose. SaveActiveNode skips
                // its inertial refresh when pageIsClosing is true, so this
                // incremental commit is the authoritative path for the
                // green-check-without-navigating case.
                //
                // InertialSource gate (read from the combobox, not the
                // data model - see SaveActiveNode for the same reasoning):
                // when the user has source = Visual or Collision, the
                // inertial mark holds the visual / collision union for
                // highlight purposes ONLY. The SelectionBox is disabled
                // in those states (SetInertialEditorEnabled), but
                // synthetic events from programmatic Select4 / DeselectAll
                // calls and SW PMP teardown still reach this handler;
                // committing those picks back into InertialComponents
                // would silently corrupt the user's saved Custom picks
                // every time we re-rehydrate the inertial mark with a
                // non-Custom resolved set.
                short choice = (PMComboBoxInertialSource != null)
                    ? PMComboBoxInertialSource.CurrentSelection
                    : (short)0;
                if (choice != 2) // 2 = Custom
                {
                    return;
                }

                if (active.Link.InertialComponents == null)
                {
                    active.Link.InertialComponents = new List<SolidWorks.Interop.sldworks.Component2>();
                }

                // Same SelectionMgr-leak / teardown defense as
                // CommitActiveVisualGroupSelection: read the picks into a
                // local list and gate on its count, NOT on
                // GetSelectedObjectCount2 (which would include non-Component2
                // leaks like a RefAxis surfaced by ExportHelper.GetRefAxis).
                List<SolidWorks.Interop.sldworks.Component2> picked =
                    new List<SolidWorks.Interop.sldworks.Component2>();
                CommonSwOperations.GetSelectedComponents(
                    ActiveSWModel, picked, PMSelectionInertial.Mark);
                if (picked.Count == 0 && active.Link.InertialComponents.Count > 0)
                {
                    return;
                }

                active.Link.InertialComponents.Clear();
                active.Link.InertialComponents.AddRange(picked);
            }
            else if (Id == SelectionGlobalCoordsysID)
            {
                // Global origin coord-sys lives on the WorldNode at the
                // root of the tree. The picked feature name writes to
                // the WorldNode's GlobalOriginCoordinateSystemName which
                // (for backwards-compat with the existing STL anchor /
                // LocalizeJoint reads) is stored on the WorldNode's
                // sentinel Link.Joint.CoordinateSystemName. An empty
                // mark means the user cleared the box (or SW released
                // marks during a tab switch); we keep the existing
                // value to avoid wiping it on programmatic teardown.
                string picked = ReadMarkedFeatureName(GlobalCoordSysSelectionMark);
                if (!string.IsNullOrEmpty(picked) && active is WorldNode worldActive)
                {
                    worldActive.GlobalOriginCoordinateSystemName = picked;
                }
            }
            else if (Id == SelectionJointCoordsysID)
            {
                // The "Link coordinate system" picker is enabled for both
                // top-level bodies (where it is the world->body offset
                // coord-sys) and nested links (where it is the joint
                // origin). It is disabled on the WorldNode itself, so
                // the gate is `active is not WorldNode` rather than the
                // pre-refactor `!active.IsBaseNode`.
                string picked = ReadMarkedFeatureName(JointCoordSysSelectionMark);
                logger.Info("OnSelectionboxListChanged(JointCoordsysID, Count=" + Count +
                            ") picked='" + picked + "' isWorld=" + (active is WorldNode));
                if (!string.IsNullOrEmpty(picked) && !(active is WorldNode))
                {
                    active.Link.Joint.CoordinateSystemName = picked;
                    // RefreshAxisDirectionPreview is only meaningful for
                    // nested links (where there's an actual joint axis
                    // to preview). For top-level bodies the axis
                    // SelectionBox is disabled and Link.Joint.AxisName is
                    // empty, so the preview helper short-circuits to a
                    // no-op overlay clear. Defer either way - the
                    // EstimateAxis -> ClearSelection2(true) re-entrancy
                    // hazard is identical to OnAxisOverlayDirectionFlipped.
                    DeferRefreshAxisPreview();
                }
            }
            else if (Id == SelectionJointAxisID)
            {
                string picked = ReadMarkedFeatureName(JointAxisSelectionMark);
                logger.Info("OnSelectionboxListChanged(JointAxisID, Count=" + Count +
                            ") picked='" + picked + "' isBase=" + active.IsBaseNode);
                if (!string.IsNullOrEmpty(picked) && !active.IsBaseNode && !active.IsTopLevelBody)
                {
                    active.Link.Joint.AxisName = picked;
                    // An explicit pick means the user no longer wants
                    // automatic derivation. Clear the toggle and update
                    // the checkbox to match.
                    active.Link.Joint.AutoDeriveAxis = false;
                    if (PMCheckAutoDeriveAxis != null)
                    {
                        PMCheckAutoDeriveAxis.Checked = false;
                    }
                    DeferRefreshAxisPreview();
                }
            }
            else if (Id == SelectionSiteCoordSysID)
            {
                // Sites are edited live: the active listbox row owns the
                // SelectionBox below it, so every accepted coord-sys pick
                // writes directly to that SiteSpec. Empty marks can be
                // synthetic clears from tab rehydration / teardown, so only
                // non-empty picks update the model.
                CommitActiveSiteCoordSysSelection(active);
            }
        }

        bool IPropertyManagerPage2Handler9.OnSubmitSelection(
            int Id, object Selection, int SelType, ref string ItemText)
        {
            // SelectionBox-only feature picking: the per-mark commit
            // happens in OnSelectionboxListChanged for coord-sys / axis
            // boxes and in CommitActive*GroupSelection for component
            // boxes. OnSubmitSelection just gates whether SolidWorks
            // accepts the pick into the SelectionBox at all (return true =
            // accept).
            return true;
        }

        // Returns the persisted name of the single object held by the
        // SelectionBox with the given mark, or empty string if none.
        // SelectionBoxes are configured SingleEntityOnly = true so we
        // only read the first entry. Defensive against null
        // SelectionManager / non-Feature objects so a bad mark doesn't
        // throw out of the SW dispatch.
        //
        // For a feature that lives inside a top-level component (e.g. a
        // coordinate system or reference axis created in a part and used
        // in the assembly), Feature.Name returns only the LOCAL feature
        // name ("Coordinate System1"), which is not resolvable at the
        // assembly level. The rest of the pipeline encodes sub-component
        // references as "<FeatureName> <Component2.Name2>" (see
        // ResolveFeatureReference / GetComponentRefGeoNames /
        // FindRefGeoNames). We mirror that encoding here by appending the
        // owning component's Name2 in angle brackets, so the stored name
        // round-trips through export resolution AND rehydrates back into
        // the SelectionBox. Top-level (assembly-scope) features have no
        // owning component and keep their bare name.
        private string ReadMarkedFeatureName(int mark)
        {
            try
            {
                SelectionMgr selMgr = ActiveSWModel?.SelectionManager;
                if (selMgr == null)
                {
                    return string.Empty;
                }
                int count = selMgr.GetSelectedObjectCount2(mark);
                if (count <= 0)
                {
                    return string.Empty;
                }
                object obj = selMgr.GetSelectedObject6(1, mark);
                Feature feature = obj as Feature;
                if (feature == null)
                {
                    return string.Empty;
                }

                // GetSelectedObjectsComponent4 returns the component the
                // selected entity belongs to, or null for a feature that
                // lives directly in the active (assembly) document.
                Component2 owningComponent = null;
                try
                {
                    owningComponent = selMgr.GetSelectedObjectsComponent4(1, mark) as Component2;
                }
                catch (Exception componentEx)
                {
                    logger.Warn("ReadMarkedFeatureName(mark=" + mark +
                        ") could not resolve owning component: " + componentEx.Message);
                }

                string componentName = owningComponent?.Name2;
                if (!string.IsNullOrEmpty(componentName))
                {
                    return feature.Name + " <" + componentName + ">";
                }
                return feature.Name;
            }
            catch (Exception ex)
            {
                logger.Warn("ReadMarkedFeatureName(mark=" + mark + ") failed: " + ex.Message);
                return string.Empty;
            }
        }

        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            if (suppressSiteEditorEvents && Id == SitesNameTextBoxID)
            {
                return;
            }
            if (Id == TextBoxLinkNameID)
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                node.Text = PMTextBoxLinkName.Text;
                node.Name = PMTextBoxLinkName.Text;
            }
            else if (Id == TextBoxJointNameID)
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                if (node != null && !node.IsBaseNode && node.Link?.Joint != null)
                {
                    node.Link.Joint.Name = PMTextBoxJointName.Text ?? "";
                }
            }
            else if (Id == SitesNameTextBoxID)
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                SaveActiveSiteFields(node);
                RefreshSitesListbox(node);
            }
            else if (Id == VisualGroupsNameTextBoxID)
            {
                // Live-rename the active visual group. Skip the synthetic
                // event fired when SyncVisualGroupNameTextbox programmatically
                // loads the selected group's name (otherwise we'd just write
                // the loaded name straight back).
                if (suppressGroupNameTextboxEvents)
                {
                    return;
                }
                LinkNode node = (LinkNode)Tree.SelectedNode;
                if (node != null && node.Link.VisualGroups != null &&
                    activeVisualGroupIndex >= 0 &&
                    activeVisualGroupIndex < node.Link.VisualGroups.Count)
                {
                    node.Link.VisualGroups[activeVisualGroupIndex].Name =
                        PMTextBoxVisualGroupName.Text ?? "";
                    RefreshVisualGroupsListbox(node);
                }
            }
            else if (Id == CollisionGroupsNameTextBoxID)
            {
                // See VisualGroupsNameTextBoxID: live-rename the active
                // collision group, ignoring programmatic loads.
                if (suppressGroupNameTextboxEvents)
                {
                    return;
                }
                LinkNode node = (LinkNode)Tree.SelectedNode;
                if (node != null && node.Link.CollisionGroups != null &&
                    activeCollisionGroupIndex >= 0 &&
                    activeCollisionGroupIndex < node.Link.CollisionGroups.Count)
                {
                    node.Link.CollisionGroups[activeCollisionGroupIndex].Name =
                        PMTextBoxCollisionGroupName.Text ?? "";
                    RefreshCollisionGroupsListbox(node);
                }
            }
        }

        int IPropertyManagerPage2Handler9.OnWindowFromHandleControlCreated(int Id, bool Status)
        {
            return 0;
        }

        #endregion Implemented Property Manager Page Handler Methods

        #region Not implemented handler methods

        // These methods are still active. The exceptions that are thrown only cause the debugger
        // to pause. Comment out the exception if you choose not to implement it, but it gets
        // regularly called anyway
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked)
        {
            if (Id == FastMeshExportCheckID)
            {
                // Mesh quality only matters for the tessellation (fast) STL
                // path, so the dropdown is enabled only when fast export is
                // checked AND the format is STL. Re-evaluate on every toggle.
                UpdateMeshQualityEnabled();
                return;
            }

            if (Id == CheckCollisionUsesVisualID)
            {
                SetCollisionEditorEnabled(!Checked);

                // Persist the toggle on the active node so a later save round-
                // trip captures it. SaveActiveNode is also called when the
                // user navigates away, but flipping this flag immediately keeps
                // the data model in sync with the UI for any code path that
                // peeks at node.Link.CollisionUsesVisual before the next save.
                LinkNode active = (LinkNode)Tree?.SelectedNode;
                if (active != null)
                {
                    active.Link.CollisionUsesVisual = Checked;

                    // Re-rehydrate the collision mark so the viewer
                    // highlight follows the toggle: checked = show
                    // the visual-component union (what will be exported
                    // as collision); unchecked = show the active
                    // collision group's components. The rehydrate is a
                    // no-op when the user is not currently on the
                    // Collision section - their highlight on whatever
                    // section they are looking at must stay scoped to
                    // THAT section's marks (see
                    // RehydrateMarksForActiveSection).
                    if (currentActiveSectionId == CollisionGroupID)
                    {
                        try
                        {
                            RehydrateMarksForActiveSection(active, CollisionGroupID);
                        }
                        catch (Exception ex)
                        {
                            logger.Warn(
                                "OnCheckboxCheck(CollisionUsesVisual) rehydrate failed: " + ex.Message);
                        }
                    }
                }
                return;
            }

            if (Id == CheckAutoDeriveAxisID)
            {
                LinkNode active = (LinkNode)Tree?.SelectedNode;
                if (active != null && !active.IsBaseNode && active.Link?.Joint != null)
                {
                    active.Link.Joint.AutoDeriveAxis = Checked;
                    if (Checked)
                    {
                        // Auto-derive ON: drop any picked axis name so
                        // the export-time path falls back to
                        // EstimateGlobalJointFromComponents. The axis
                        // SelectionBox is also cleared so the UI matches
                        // the data model.
                        active.Link.Joint.AxisName = "";
                        if (PMSelectionJointAxis != null)
                        {
                            try
                            {
                                bool prior = suppressGroupListboxRefresh;
                                suppressGroupListboxRefresh = true;
                                try
                                {
                                    // Scope the clear to this mark only -
                                    // ClearSelection2(true) here would
                                    // wipe every sibling SelectionBox in
                                    // the same PMP.
                                    CommonSwOperations.DeselectAllAtMark(
                                        ActiveSWModel, PMSelectionJointAxis.Mark);
                                }
                                finally
                                {
                                    suppressGroupListboxRefresh = prior;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.Warn("Clearing axis SelectionBox on auto-derive toggle failed: " + ex.Message);
                            }
                        }
                    }
                    SetAxisPickerEnabled(!Checked);
                    // OnCheckboxCheck is dispatched from inside SW's
                    // PMP event pump alongside OnSelectionboxListChanged;
                    // defer the preview redraw for the same reason.
                    DeferRefreshAxisPreview();
                }
                return;
            }

            if (Id == CheckAutoComputeLimitsID)
            {
                LinkNode active = (LinkNode)Tree?.SelectedNode;
                bool isNestedLink = active != null
                    && ResolveNodeRole(active) == NodeRole.NestedLink;
                if (isNestedLink && active.Link?.Joint != null)
                {
                    active.Link.Joint.AutoComputeLimits = Checked;
                }
                SetAutoComputeLimitEditorEnabled(isNestedLink && !Checked);
                return;
            }

            logger.Info("OnCheckboxCheck called for Id=" + Id + ". No special handler registered.");
        }

        // Toggle the inertial SelectionBox enable in lockstep with the
        // InertialSource dropdown. Source = Custom means the
        // SelectionBox is the editor for Link.InertialComponents and
        // user picks commit normally. Source = Visual / Collision means
        // the SelectionBox is a READ-ONLY display of the resolved set
        // (the same set that drives mass / inertia per
        // Link.GetInertialComponents); disabling it prevents user
        // picks from committing visual / collision components into
        // InertialComponents (which would silently corrupt the
        // user's saved Custom picks). The commit gates in
        // SaveActiveNode and OnSelectionboxListChanged enforce the
        // same rule on the write path; SetInertialEditorEnabled is
        // the UX surface for it.
        private void SetInertialEditorEnabled(InertialSource source)
        {
            IPropertyManagerPageControl pageControl =
                PMSelectionInertial as IPropertyManagerPageControl;
            if (pageControl == null)
            {
                return;
            }
            pageControl.Enabled = (source == InertialSource.Custom);
        }

        // Toggle the joint-axis picker controls (SelectionBox + reverse-
        // direction button) in lockstep with PMCheckAutoDeriveAxis. Auto-
        // derive ON disables them; auto-derive OFF re-enables. Visibility
        // is unchanged - the controls remain on screen so the user can
        // see the relationship between the toggle and the picker.
        private void SetAxisPickerEnabled(bool enabled)
        {
            object[] controls = new object[]
            {
                PMSelectionJointAxis,
                PMBitmapAxisFlip,
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

        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text)
        {
            logger.Info("OnComboboxEditChanged called. This method no longer throws an Exception." +
                " It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            if (Id == OutputFormatComboID)
            {
                // Rotation format and angle units are MJCF-only; grey them out
                // for URDF so it's clear the options don't apply. Item order
                // matches the AddItems call in BuildSetupTab: 0 = URDF, 1 = MJCF.
                SetRotationFormatEnabled(Item == 1);
                SetAngleUnitEnabled(Item == 1);
                return;
            }

            if (Id == MeshFormatComboID)
            {
                // "Fast mesh export" (per-part tessellation) only produces STL.
                // 3DXML always uses the legacy whole-assembly path, so grey out
                // the checkbox for 3DXML - that leaves exactly three valid
                // combinations (STL+fast, STL+legacy, 3DXML+legacy) and no two
                // paths to the same result. Item order matches the AddItems call
                // in BuildSetupTab: 0 = STL, 1 = 3DXML.
                SetFastMeshExportEnabled(Item == 0);
                // Quality depends on BOTH format (STL) and the fast-export
                // checkbox, so re-evaluate it whenever the format changes too.
                UpdateMeshQualityEnabled();
                return;
            }

            if (Id == ComboInertialSourceID)
            {
                // Map the dropdown index to the InertialSource enum
                // (must stay in sync with the AddItems call in
                // BuildInertialTab and the persistence map in
                // SaveActiveNode).
                InertialSource newSource = (Item == 1) ? InertialSource.Collision
                    : (Item == 2) ? InertialSource.Custom
                    : InertialSource.Visual;

                LinkNode active = (LinkNode)Tree?.SelectedNode;
                if (active != null && active.Link != null)
                {
                    active.Link.InertialSource = newSource;
                }

                // SelectionBox is editable only when source = Custom;
                // Visual / Collision turn it into a read-only display
                // of the resolved set. Toggle the enable state in
                // lockstep with the source so the user sees the right
                // affordance immediately.
                SetInertialEditorEnabled(newSource);

                // Refresh the highlight so the viewer shows the set
                // that will actually drive mass / inertia. No-op when
                // the user isn't currently on the Inertial section.
                if (active != null && currentActiveSectionId == InertialGroupID)
                {
                    try
                    {
                        RehydrateMarksForActiveSection(active, InertialGroupID);
                    }
                    catch (Exception ex)
                    {
                        logger.Warn(
                            "OnComboboxSelectionChanged(InertialSource) rehydrate failed: " + ex.Message);
                    }
                }
                return;
            }

            if (Id == ComboBoxWorldAttachmentID)
            {
                // World attachment combobox order MUST match
                // WorldAttachmentModel (Welded = 0, Free = 1) - see the
                // AddItems call in BuildWorldAttachmentControls.
                LinkNode active = (LinkNode)Tree?.SelectedNode;
                if (active != null && active.IsTopLevelBody && active.Link != null)
                {
                    active.Link.WorldAttachment =
                        (Item == 1)
                            ? SW2RD.Core.WorldAttachmentModel.Free
                            : SW2RD.Core.WorldAttachmentModel.Welded;
                }
                return;
            }

            // PMComboBoxJointType is the only other combobox on the
            // page; changes there don't affect the axis overlay so
            // they're a no-op (kept for future expansion - e.g.
            // surfacing a warning when "fixed" is picked but an
            // axis is selected).
        }

        void IPropertyManagerPage2Handler9.OnGroupCheck(int Id, bool Checked)
        {
            logger.Info("OnGroupCheck called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        // Page-1 accordion + active-section driver. The five kinematic-
        // config groups behave as an accordion: expanding one collapses
        // the others, and the freshly-expanded group becomes the active
        // section that drives the SOLIDWORKS viewer highlight (and the
        // joint-axis arrow overlay for the Link/Joint section). Collapsing
        // a group is left alone - the highlight stays on the last expanded
        // section until the user expands a different one.
        void IPropertyManagerPage2Handler9.OnGroupExpand(int Id, bool Expanded)
        {
            // Re-entrancy guard: ApplyAccordionInitialState /
            // UpdateWizardVisibility and the sibling-collapse loop below
            // all write Expanded programmatically, which re-fires this
            // callback.
            if (suppressGroupExpandAccordion)
            {
                return;
            }

            // The accordion (and its viewer-highlight side effects) only
            // applies on the configure page. Ignore any stray expand event
            // that arrives while the export page is showing.
            if (currentWizardPage != 1)
            {
                return;
            }

            // Only the page-1 kinematic-config groups participate. The
            // tree and export groups are not accordion members.
            if (Id != LinkJointGroupID && Id != VisualGroupID && Id != CollisionGroupID
                && Id != InertialGroupID && Id != SitesGroupID)
            {
                return;
            }

            // Collapsing a section doesn't change the active section - the
            // user just folded it away. Leave the highlight as-is.
            if (!Expanded)
            {
                return;
            }

            // Accordion: collapse every sibling so only the just-expanded
            // group stays open.
            suppressGroupExpandAccordion = true;
            try
            {
                if (Id != LinkJointGroupID && PMLinkJointGroup != null) PMLinkJointGroup.Expanded = false;
                if (Id != VisualGroupID && PMVisualGroup != null) PMVisualGroup.Expanded = false;
                if (Id != CollisionGroupID && PMCollisionGroup != null) PMCollisionGroup.Expanded = false;
                if (Id != InertialGroupID && PMInertialGroup != null) PMInertialGroup.Expanded = false;
                if (Id != SitesGroupID && PMSitesGroup != null) PMSitesGroup.Expanded = false;
            }
            catch (Exception ex)
            {
                logger.Warn("OnGroupExpand accordion collapse failed: " + ex.Message);
            }
            finally
            {
                suppressGroupExpandAccordion = false;
            }

            // The freshly-expanded group is now the active section: drive
            // the viewer highlight to match.
            currentActiveSectionId = Id;
            LinkNode node = (LinkNode)Tree?.SelectedNode;
            if (node == null)
            {
                return;
            }

            try
            {
                RehydrateMarksForActiveSection(node, Id);
            }
            catch (Exception ex)
            {
                logger.Warn("OnGroupExpand(" + Id + ") rehydrate failed: " + ex.Message);
            }

            // Joint-axis arrow overlay follows the Link/Joint section. Draw
            // it (deferred, to avoid the SelectionMgr re-entrancy hazard
            // documented on DeferRefreshAxisPreview) when entering the
            // Link/Joint section on a nested link; clear it otherwise so it
            // doesn't linger in the viewport while another section is active.
            try
            {
                if (Id == LinkJointGroupID && ResolveNodeRole(node) == NodeRole.NestedLink)
                {
                    DeferRefreshAxisPreview();
                }
                else
                {
                    Exporter.ClearAxisOverlay();
                }
            }
            catch (Exception ex)
            {
                logger.Warn("OnGroupExpand(" + Id + ") axis overlay update failed: " + ex.Message);
            }
        }

        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            try
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                if (node == null)
                {
                    return;
                }
                if (Id == VisualGroupsListBoxID)
                {
                    // Save the previous group's selection before switching.
                    CommitActiveVisualGroupSelection(node);
                    if (Item >= 0 && Item < (node.Link.VisualGroups != null ? node.Link.VisualGroups.Count : 0))
                    {
                        activeVisualGroupIndex = Item;
                        LoadActiveVisualGroupIntoSelectionBox(node);
                        RefreshVisualGroupsListbox(node);
                        SyncVisualGroupNameTextbox(node);
                    }
                }
                else if (Id == CollisionGroupsListBoxID)
                {
                    CommitActiveCollisionGroupSelection(node);
                    if (Item >= 0 && Item < (node.Link.CollisionGroups != null ? node.Link.CollisionGroups.Count : 0))
                    {
                        activeCollisionGroupIndex = Item;
                        LoadActiveCollisionGroupIntoSelectionBox(node);
                        RefreshCollisionGroupsListbox(node);
                        SyncCollisionGroupNameTextbox(node);
                    }
                }
                else if (Id == SitesListBoxID)
                {
                    if (suppressSiteListboxSelectionChanged)
                    {
                        return;
                    }
                    SaveActiveSiteFields(node);
                    if (Item >= 0 && Item < (node.Link.Sites != null ? node.Link.Sites.Count : 0))
                    {
                        activeSiteIndex = Item;
                        LoadActiveSiteIntoForm(node);
                        RefreshSitesListbox(node);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception caught handling listbox selection change " + Id, e);
            }
        }

        // Native Next-arrow handler: advances the wizard from page 1
        // (Configure) to page 2 (Export). Returning true lets SW treat the
        // page change as accepted; the actual content swap is done by
        // UpdateWizardVisibility. Commit the active node first so the
        // export step sees the freshly-edited link state. While on the
        // export page the page-1 SelectionBox marks + axis overlay are
        // irrelevant, so we drain them to keep the viewport clean.
        bool IPropertyManagerPage2Handler9.OnNextPage()
        {
            try
            {
                if (currentWizardPage >= TotalWizardPages)
                {
                    return false;
                }

                SaveActiveNode();

                currentWizardPage++;
                UpdateWizardVisibility();

                // Leaving the configure page: drop every page-1 highlight
                // and the joint-axis arrow so the viewport isn't cluttered
                // while the user sets export options.
                try
                {
                    ClearAllSelectionMarks();
                    Exporter.ClearAxisOverlay();
                }
                catch (Exception ex)
                {
                    logger.Warn("OnNextPage clearing page-1 highlights failed: " + ex.Message);
                }

                return true;
            }
            catch (Exception e)
            {
                logger.Error("Exception caught advancing to the export page", e);
                return false;
            }
        }

        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id)
        {
            logger.Info("OnOptionCheck called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItem(int Id)
        {
            logger.Info("OnPopupMenuItem called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItemUpdate(int Id, ref int retval)
        {
            logger.Info("OnPopupMenuItemUpdate called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        bool IPropertyManagerPage2Handler9.OnPreview()
        {
            logger.Info("OnPreview called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
            return true;
        }

        // Native Previous-arrow handler: returns the wizard from page 2
        // (Export) back to page 1 (Configure). Re-hydrates the active
        // section's SelectionBox marks + joint-axis arrow so the viewer
        // highlight the user had on the configure page is restored.
        bool IPropertyManagerPage2Handler9.OnPreviousPage()
        {
            try
            {
                if (currentWizardPage <= 1)
                {
                    return false;
                }

                currentWizardPage--;
                UpdateWizardVisibility();

                // Returning to the configure page: restore the highlight
                // for whatever section was active when we left.
                LinkNode node = (LinkNode)Tree?.SelectedNode;
                if (node != null)
                {
                    try
                    {
                        RehydrateMarksForActiveSection(node, currentActiveSectionId);
                        if (currentActiveSectionId == LinkJointGroupID
                            && ResolveNodeRole(node) == NodeRole.NestedLink)
                        {
                            DeferRefreshAxisPreview();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Warn("OnPreviousPage restoring page-1 highlights failed: " + ex.Message);
                    }
                }

                return true;
            }
            catch (Exception e)
            {
                logger.Error("Exception caught returning to the configure page", e);
                return false;
            }
        }

        void IPropertyManagerPage2Handler9.OnRedo()
        {
            logger.Info("OnRedo called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id)
        {
            logger.Info("OnSelectionboxCalloutCreated called. This method no longer throws " +
                " an Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id)
        {
            logger.Info("OnSelectionboxCalloutDestroyed called. This method no longer throws " +
                "an Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSliderPositionChanged(int Id, double Value)
        {
            logger.Info("OnSliderPositionChanged called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSliderTrackingCompleted(int Id, double Value)
        {
            logger.Info("OnSliderTrackingCompleted called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        // The PMP no longer uses tabs - page-1 navigation is the accordion
        // of PropertyManagerPageGroups (see OnGroupExpand) and page-to-page
        // navigation is the native Next / Previous arrows (OnNextPage /
        // OnPreviousPage). The active-section viewer-highlight machinery
        // that used to live here now lives in OnGroupExpand /
        // RehydrateMarksForActiveSection. Kept as a no-op because the
        // IPropertyManagerPage2Handler9 interface requires it.
        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id)
        {
            return true;
        }

        void IPropertyManagerPage2Handler9.OnUndo()
        {
            logger.Info("OnUndo called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnWhatsNew()
        {
            logger.Info("OnWhatsNew called. This method no longer throws an Exception. It just " +
                " silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnListboxRMBUp(int Id, int PosX, int PosY)
        {
            logger.Info("OnListboxRMBUp called. This method no longer throws an Exception. It " +
                " just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value)
        {
            logger.Info("OnNumberBoxTrackingCompleted called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.AfterClose()
        {
            logger.Info("AfterClose called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        int IPropertyManagerPage2Handler9.OnActiveXControlCreated(int Id, bool Status)
        {
            logger.Info("OnActiveXControlCreated called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
            return 0;
        }

        #endregion Not implemented handler methods
    }
}
