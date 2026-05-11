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
using System.Diagnostics;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
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
            // selection box
            PMSelectionVisual.SetSelectionFocus();
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
                // Base-link global origin coord-sys. The picked feature
                // name is the source of truth; an empty mark means the
                // user cleared the box (or SW released marks during a
                // tab switch) and we keep the existing value to avoid
                // wiping it on programmatic teardown.
                string picked = ReadMarkedFeatureName(GlobalCoordSysSelectionMark);
                if (!string.IsNullOrEmpty(picked))
                {
                    active.Link.Joint.CoordinateSystemName = picked;
                }
            }
            else if (Id == SelectionJointCoordsysID)
            {
                string picked = ReadMarkedFeatureName(JointCoordSysSelectionMark);
                logger.Info("OnSelectionboxListChanged(JointCoordsysID, Count=" + Count +
                            ") picked='" + picked + "' isBase=" + active.IsBaseNode);
                if (!string.IsNullOrEmpty(picked) && !active.IsBaseNode)
                {
                    active.Link.Joint.CoordinateSystemName = picked;
                    // RefreshAxisDirectionPreview MUST be deferred -
                    // it calls EstimateAxis -> ClearSelection2(true)
                    // which would otherwise wipe the marked pick the
                    // user just made out of this very SelectionBox
                    // before SW gets a chance to render it. Same
                    // re-entrancy hazard as OnAxisOverlayDirectionFlipped.
                    DeferRefreshAxisPreview();
                }
            }
            else if (Id == SelectionJointAxisID)
            {
                string picked = ReadMarkedFeatureName(JointAxisSelectionMark);
                logger.Info("OnSelectionboxListChanged(JointAxisID, Count=" + Count +
                            ") picked='" + picked + "' isBase=" + active.IsBaseNode);
                if (!string.IsNullOrEmpty(picked) && !active.IsBaseNode)
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
            // SelectionSiteCoordSysID is intentionally not committed here.
            // The site coord-sys pick is "transient": it's consumed when
            // the user clicks Add Site (AddSiteFromForm reads the marked
            // SelectionMgr entry and clears the box). Letting an empty
            // mark wipe a partially-typed pending pick would be hostile
            // to the user; we just rely on AddSiteFromForm reading at
            // click time.
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

        // Returns the Feature.Name of the single object held by the
        // SelectionBox with the given mark, or empty string if none.
        // SelectionBoxes are configured SingleEntityOnly = true so we
        // only read the first entry. Defensive against null
        // SelectionManager / non-Feature objects so a bad mark doesn't
        // throw out of the SW dispatch.
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
                return feature?.Name ?? string.Empty;
            }
            catch (Exception ex)
            {
                logger.Warn("ReadMarkedFeatureName(mark=" + mark + ") failed: " + ex.Message);
                return string.Empty;
            }
        }

        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
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

            logger.Info("OnCheckboxCheck called for Id=" + Id + ". No special handler registered.");
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
            // PMComboBoxJointType is the only remaining read-only
            // combobox on the Link/Joint tab; coord-sys / axis
            // pickers are SelectionBox-only. Joint-type changes don't
            // affect the axis overlay so this handler is currently a
            // no-op, but kept for future expansion (e.g. surfacing a
            // warning when "fixed" is picked but an axis is selected).
        }

        void IPropertyManagerPage2Handler9.OnGroupCheck(int Id, bool Checked)
        {
            logger.Info("OnGroupCheck called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnGroupExpand(int Id, bool Expanded)
        {
            logger.Info("OnGroupExpand called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
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
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception caught handling listbox selection change " + Id, e);
            }
        }

        bool IPropertyManagerPage2Handler9.OnNextPage()
        {
            logger.Info("OnNextPage called. This method no longer throws an Exception. It just " + "" +
                "silently does nothing. Ok, except for this logging message");
            return true;
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

        bool IPropertyManagerPage2Handler9.OnPreviousPage()
        {
            logger.Info("OnPreviousPage called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
            return true;
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

        // SolidWorks does not paint marked SelectionBox contents into a
        // SelectionBox whose tab is not currently active, and switching
        // tabs after FillPropertyManager has populated the marks does NOT
        // retroactively rehydrate the box. Re-firing the per-group loader
        // when the user activates Visual / Collision / Inertial puts the
        // resolved Component2 list back into the SelectionBox so the user
        // sees what they configured. The suppressGroupListboxRefresh
        // guard around the loaders prevents re-entrant
        // OnSelectionboxListChanged events from double-committing the
        // selection back into the active group.
        //
        // This MUST run synchronously - SW activates the tab AFTER
        // OnTabClicked returns true, and a synchronous load populates
        // the underlying SelectionMgr marks BEFORE SW paints the now-
        // active tab, so the freshly-loaded contents render
        // immediately. A previous attempt to defer the load via
        // Tree.BeginInvoke removed this synchronous path and broke
        // rendering: SW painted the empty tab first, our deferred
        // load wrote to the marks afterwards, and SW never re-rendered
        // (the user saw "(N comp.)" in the listbox but an empty
        // SelectionBox below it on every tab switch). The marks
        // themselves are independent powers of 2 (see
        // *SelectionMark constants in ExportPropertyManager.cs and
        // the AGENTS.md "marks are bitmasks" note), so cross-mark
        // contamination is not a concern here.
        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id)
        {
            LinkNode node = (LinkNode)Tree?.SelectedNode;
            if (node == null)
            {
                return true;
            }

            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                switch (Id)
                {
                    case LinkJointTabID:
                        LoadActiveGlobalCoordsysIntoSelectionBox(node);
                        LoadActiveJointCoordsysIntoSelectionBox(node);
                        LoadActiveJointAxisIntoSelectionBox(node);
                        break;
                    case VisualTabID:
                        LoadActiveVisualGroupIntoSelectionBox(node);
                        break;
                    case CollisionTabID:
                        LoadActiveCollisionGroupIntoSelectionBox(node);
                        break;
                    case InertialTabID:
                        if (node.Link.InertialComponents == null)
                        {
                            node.Link.InertialComponents = new List<Component2>();
                        }
                        // Scope the clear to the inertial mark only so we
                        // don't disturb sibling SelectionBoxes (visual,
                        // collision, feature pickers).
                        CommonSwOperations.DeselectAllAtMark(
                            ActiveSWModel, PMSelectionInertial.Mark);
                        CommonSwOperations.SelectComponents(
                            ActiveSWModel,
                            node.Link.InertialComponents,
                            false,
                            PMSelectionInertial.Mark);
                        break;
                    case SitesTabID:
                        // Sites coord-sys SelectionBox is transient (it
                        // gets consumed on Add Site click). Clear any
                        // stale selection at the site mark so the user
                        // starts from a clean slate, but DO NOT touch
                        // sibling marks.
                        if (PMSelectionSiteCoordSys != null)
                        {
                            CommonSwOperations.DeselectAllAtMark(
                                ActiveSWModel, PMSelectionSiteCoordSys.Mark);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.Warn("OnTabClicked(" + Id + ") rehydrate failed: " + ex.Message);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
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
