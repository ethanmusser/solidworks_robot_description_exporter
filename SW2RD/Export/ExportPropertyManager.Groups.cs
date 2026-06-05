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
using SolidWorks.Interop.swpublished;
using SW2RD.Input;
using System;
using System.Collections.Generic;

namespace SW2RD.Export
{
    // Visual / collision multi-group editor: add / remove groups, commit the
    // SelectionBox into the active group, load the active group back into the
    // SelectionBox when switching groups, refresh the listbox count badges.
    // Split out of ExportPropertyManager.cs as part of the Phase 1
    // partial-class refactor; no behavior changes.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        // Saves the components currently selected in the visual SelectionBox
        // into the active visual group of the active link, creates a new empty
        // group, and refreshes the listbox so the user can populate it.
        private void AddVisualGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);

            // Commit the user's current selection into the previously-active
            // group before we create a new one.
            CommitActiveVisualGroupSelection(node);

            // The group-name textbox now reflects / renames the active group
            // in place, so a new group always gets an auto default name; the
            // user renames it afterward via the textbox (which now shows this
            // default, selected and ready to edit).
            string newName = NextDefaultGroupName(
                node.Link.VisualGroups, MeshGroup.DefaultVisualName());
            node.Link.VisualGroups.Add(new MeshGroup(newName));

            activeVisualGroupIndex = node.Link.VisualGroups.Count - 1;
            RefreshVisualGroupsListbox(node);
            LoadActiveVisualGroupIntoSelectionBox(node);
            SyncVisualGroupNameTextbox(node);
        }

        private void RemoveSelectedVisualGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (node.Link.VisualGroups.Count == 0)
            {
                return;
            }
            short selected = PMListBoxVisualGroups.CurrentSelection;
            if (selected < 0 || selected >= node.Link.VisualGroups.Count)
            {
                return;
            }
            node.Link.VisualGroups.RemoveAt(selected);
            if (activeVisualGroupIndex >= node.Link.VisualGroups.Count)
            {
                activeVisualGroupIndex = node.Link.VisualGroups.Count - 1;
            }
            if (activeVisualGroupIndex < 0)
            {
                activeVisualGroupIndex = -1;
            }
            RefreshVisualGroupsListbox(node);
            LoadActiveVisualGroupIntoSelectionBox(node);
            SyncVisualGroupNameTextbox(node);
        }

        private void AddCollisionGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);

            CommitActiveCollisionGroupSelection(node);

            // See AddVisualGroupFromForm: the textbox renames the active group
            // in place, so new groups get an auto default name and the textbox
            // is populated with it for immediate rename.
            string newName = NextDefaultGroupName(
                node.Link.CollisionGroups, MeshGroup.DefaultCollisionName());
            node.Link.CollisionGroups.Add(new MeshGroup(newName));

            activeCollisionGroupIndex = node.Link.CollisionGroups.Count - 1;
            RefreshCollisionGroupsListbox(node);
            LoadActiveCollisionGroupIntoSelectionBox(node);
            SyncCollisionGroupNameTextbox(node);
        }

        private void RemoveSelectedCollisionGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (node.Link.CollisionGroups.Count == 0)
            {
                return;
            }
            short selected = PMListBoxCollisionGroups.CurrentSelection;
            if (selected < 0 || selected >= node.Link.CollisionGroups.Count)
            {
                return;
            }
            node.Link.CollisionGroups.RemoveAt(selected);
            if (activeCollisionGroupIndex >= node.Link.CollisionGroups.Count)
            {
                activeCollisionGroupIndex = node.Link.CollisionGroups.Count - 1;
            }
            if (activeCollisionGroupIndex < 0)
            {
                activeCollisionGroupIndex = 0;
            }
            RefreshCollisionGroupsListbox(node);
            LoadActiveCollisionGroupIntoSelectionBox(node);
            SyncCollisionGroupNameTextbox(node);
        }

        // Commits the visual SelectionBox's current contents into the active
        // visual group on the active link. Called whenever the user is about
        // to change the active group or active node.
        private void CommitActiveVisualGroupSelection(LinkNode node)
        {
            if (node == null)
            {
                return;
            }
            // The page is closing: SolidWorks has already released the marks
            // that back this SelectionBox, so reading them back would clear
            // the group with whatever stale state happens to be there.
            // OnSelectionboxListChanged has kept the group in sync on every
            // user pick, so the in-memory data is already authoritative.
            if (pageIsClosing)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (activeVisualGroupIndex < 0 || activeVisualGroupIndex >= node.Link.VisualGroups.Count)
            {
                return;
            }
            MeshGroup group = node.Link.VisualGroups[activeVisualGroupIndex];
            if (group.Components == null)
            {
                group.Components = new List<Component2>();
            }

            // Teardown defense: if SolidWorks has 0 REAL Component2 picks
            // at this mark but the active group already holds components,
            // this commit is almost certainly being driven by a programmatic
            // clear (PMPage tear-down on green-check, another loader's
            // ClearSelection2(true) cascade, or a SelectionMgr leak from
            // ExportHelper.GetRefAxis - see CommonSwOperations.GetSelectedComponents
            // for that scenario) rather than a deliberate user action. The
            // destructive Clear+Refill below would wipe a freshly-picked
            // component, so we bail out and let the existing in-memory
            // state stand. The OnSelectionboxListChanged handler kept
            // group.Components in sync for every user pick on the way in,
            // so we already have the authoritative list. Trade-off: the user
            // cannot clear the LAST component in a group through the
            // SelectionBox UI alone - they need to remove the group entirely
            // or pick a different component first. That UX cost is worth
            // avoiding silent data loss on the last-edited link.
            //
            // We read the picks into a local list and gate on its count
            // (instead of ActiveSWModel.SelectionManager.GetSelectedObjectCount2)
            // because GetSelectedObjectCount2 includes non-Component2 leaks
            // at the queried mark - that count > 0 with the legacy gate
            // would let the destructive path proceed and immediately wipe
            // the saved components when the SelectionBox-mark contained only
            // a leaked RefAxis with no real component picks.
            List<Component2> picked = new List<Component2>();
            CommonSwOperations.GetSelectedComponents(
                ActiveSWModel, picked, PMSelectionVisual.Mark);
            if (picked.Count == 0 && group.Components.Count > 0)
            {
                return;
            }

            group.Components.Clear();
            group.Components.AddRange(picked);
        }

        private void CommitActiveCollisionGroupSelection(LinkNode node)
        {
            if (node == null)
            {
                return;
            }
            // See CommitActiveVisualGroupSelection: skip during OnClose so we
            // don't clobber the active group from an empty SelectionMgr.
            if (pageIsClosing)
            {
                return;
            }
            // CollisionUsesVisual contract: the collision mark holds the
            // VISUAL component union for highlight purposes when the
            // toggle is checked (see LoadActiveCollisionGroupIntoSelectionBox).
            // Committing those back into the active collision group
            // would overwrite the user's saved collision picks with
            // visual components - a destructive corruption that the
            // user can't easily undo (and that they'd hit immediately
            // on next link switch). The editor is disabled in this
            // state, so the saved groups are already authoritative.
            if (node.Link.CollisionUsesVisual)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (activeCollisionGroupIndex < 0 || activeCollisionGroupIndex >= node.Link.CollisionGroups.Count)
            {
                return;
            }
            MeshGroup group = node.Link.CollisionGroups[activeCollisionGroupIndex];
            if (group.Components == null)
            {
                group.Components = new List<Component2>();
            }

            // See CommitActiveVisualGroupSelection: same teardown / cascade /
            // SelectionMgr-leak defense applied to the collision side.
            List<Component2> picked = new List<Component2>();
            CommonSwOperations.GetSelectedComponents(
                ActiveSWModel, picked, PMSelectionCollision.Mark);
            if (picked.Count == 0 && group.Components.Count > 0)
            {
                return;
            }

            group.Components.Clear();
            group.Components.AddRange(picked);
        }

        // Loads the active visual group's components into the visual
        // SelectionBox. Called after the active group changes.
        private void LoadActiveVisualGroupIntoSelectionBox(LinkNode node)
        {
            // The pre-populate clear below and the subsequent
            // SelectComponents call fire OnSelectionboxListChanged once per
            // affected item. Without the suppress guard around the WHOLE
            // body, the Count=0 event from the clear would re-enter
            // CommitActiveVisualGroupSelection and clobber group.Components
            // with an empty SelectionMgr read - that's the data-loss path
            // the user hit on the end-effector link.
            //
            // The clear is SCOPED to the visual mark only via
            // CommonSwOperations.DeselectAllAtMark. Using the global
            // ClearSelection2(true) here would wipe every sibling
            // SelectionBox (collision mark 12, inertial mark 13, the four
            // feature pickers at marks 21-24) and was the root cause of
            // the "Visual tab opens empty under (1 comp.)" symptom when
            // the collision loader ran after this one in
            // FillPropertyManager.
            //
            // SAVE/RESTORE the prior flag value (do NOT unconditionally
            // reset to false in the finally). FillPropertyManager wraps
            // its entire SelectionBox load block in an outer
            // suppressGroupListboxRefresh = true; an unconditional finally
            // = false here would clobber the outer guard for everything
            // that runs AFTER this loader (notably the inertial direct
            // SelectComponents call). When that runs with suppress = false,
            // Component2.Select4 fires OnSelectionboxListChanged ->
            // inertial branch -> active.Link.InertialComponents.Clear() +
            // AddRange(picked), which mutates the very list being foreach'd
            // -> InvalidOperationException("Collection was modified").
            // If selection loading re-enters commit logic, it can mutate the
            // component list while it is being enumerated and leave the active
            // tree node out of sync with the textboxes.
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                // Drop the visual mark only so we don't accumulate
                // components from the previously-active group.
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionVisual.Mark);
                if (node == null)
                {
                    return;
                }
                EnsureGroupsInitialized(node);
                if (activeVisualGroupIndex < 0 || activeVisualGroupIndex >= node.Link.VisualGroups.Count)
                {
                    return;
                }
                MeshGroup group = node.Link.VisualGroups[activeVisualGroupIndex];
                if (group.Components == null)
                {
                    return;
                }
                CommonSwOperations.SelectComponents(
                    ActiveSWModel, group.Components, false, PMSelectionVisual.Mark);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
        }

        private void LoadActiveCollisionGroupIntoSelectionBox(LinkNode node)
        {
            // Mirror of LoadActiveVisualGroupIntoSelectionBox - scope the
            // pre-populate clear to the collision mark only via
            // DeselectAllAtMark so we don't disturb siblings (visual,
            // inertial, feature pickers).
            //
            // SAVE/RESTORE the prior flag value - see the comment on
            // LoadActiveVisualGroupIntoSelectionBox for the full rationale.
            // Tldr: FillPropertyManager wraps the whole load block in
            // suppress = true; an unconditional finally = false here would
            // expose the inertial direct SelectComponents call to the
            // "Collection was modified" foreach exception.
            //
            // CollisionUsesVisual override: when the user has checked
            // "Use visual groups as collision", the collision editor is
            // greyed out (SetCollisionEditorEnabled(false)) and the
            // saved per-group collision picks are irrelevant at export
            // time - the visual groups feed the collision writer
            // directly. The viewer-highlight contract is "show what
            // will be exported as collision for the active link", so
            // we load the union of visual components into the collision
            // mark here. The collision SelectionBox is disabled in this
            // state so the user can't accidentally edit; the commit
            // gate in CommitActiveCollisionGroupSelection refuses to
            // write back when CollisionUsesVisual is true, preserving
            // the saved per-group collision picks for if the user
            // unchecks the toggle later.
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionCollision.Mark);
                if (node == null)
                {
                    return;
                }
                EnsureGroupsInitialized(node);

                if (node.Link.CollisionUsesVisual)
                {
                    List<Component2> visuals = node.Link.VisualComponents;
                    if (visuals != null && visuals.Count > 0)
                    {
                        CommonSwOperations.SelectComponents(
                            ActiveSWModel, visuals, false, PMSelectionCollision.Mark);
                    }
                    return;
                }

                if (activeCollisionGroupIndex < 0 || activeCollisionGroupIndex >= node.Link.CollisionGroups.Count)
                {
                    return;
                }
                MeshGroup group = node.Link.CollisionGroups[activeCollisionGroupIndex];
                if (group.Components == null)
                {
                    return;
                }
                CommonSwOperations.SelectComponents(
                    ActiveSWModel, group.Components, false, PMSelectionCollision.Mark);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
        }

        // Loads the inertial mark with the set of components that will
        // drive mass / inertia at export time, per the InertialSource
        // dropdown. Pulled out of FillPropertyManager so the
        // viewer-highlight-follows-active-section logic in RehydrateMarksForActiveSection
        // has a uniform "load the inertial mark" entry point.
        //
        // Source resolution rules (must mirror Link.GetInertialComponents
        // so the highlight shows EXACTLY what the export pipeline will
        // use - any divergence here is a user-confusing UX bug):
        //   Visual    -> union of all VisualGroups[].Components
        //   Collision -> union of all CollisionGroups[].Components,
        //                falling back to VisualGroups[] when empty
        //   Custom    -> Link.InertialComponents, falling back to
        //                VisualGroups[] when empty
        //
        // The user's Custom picks (Link.InertialComponents) are
        // preserved even when source != Custom: we just don't show
        // them. Switching back to Custom rehydrates from the same
        // list. The commit gates in SaveActiveNode and
        // OnSelectionboxListChanged keep this guarantee on the
        // write path by refusing to write into InertialComponents
        // unless source == Custom.
        private void LoadActiveInertialIntoSelectionBox(LinkNode node)
        {
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionInertial.Mark);
                if (node == null)
                {
                    return;
                }
                if (node.Link.InertialComponents == null)
                {
                    node.Link.InertialComponents = new List<Component2>();
                }
                List<Component2> resolved = ResolveInertialHighlightSet(node);
                if (resolved != null && resolved.Count > 0)
                {
                    CommonSwOperations.SelectComponents(
                        ActiveSWModel, resolved, false, PMSelectionInertial.Mark);
                }
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
        }

        // Mirrors Link.GetInertialComponents (without the isFallback
        // out-param) for the viewer-highlight + read-only SelectionBox
        // display path. Returns a fresh list each call - both
        // VisualComponents and CollisionComponents are union getters
        // that already allocate per-call - so callers may modify the
        // returned list freely.
        //
        // INVARIANT: any change to the source resolution rule MUST
        // land in Link.GetInertialComponents as well, or the
        // highlighted set will drift from what gets exported.
        private static List<Component2> ResolveInertialHighlightSet(LinkNode node)
        {
            if (node == null || node.Link == null)
            {
                return null;
            }
            switch (node.Link.InertialSource)
            {
                case InertialSource.Collision:
                    List<Component2> coll = node.Link.CollisionComponents;
                    if (coll != null && coll.Count > 0)
                    {
                        return coll;
                    }
                    return node.Link.VisualComponents;
                case InertialSource.Custom:
                    if (node.Link.InertialComponents != null && node.Link.InertialComponents.Count > 0)
                    {
                        return node.Link.InertialComponents;
                    }
                    return node.Link.VisualComponents;
                case InertialSource.Visual:
                default:
                    return node.Link.VisualComponents;
            }
        }

        // Clears every component / feature-picker mark owned by the PMP.
        // Used by RehydrateMarksForActiveSection to drain the marks for the
        // sections the user is leaving so the SOLIDWORKS viewer highlight no
        // longer shows them. Safe to call even before all SelectionBoxes
        // have been added (defensive null checks per box).
        private void ClearAllSelectionMarks()
        {
            if (ActiveSWModel == null)
            {
                return;
            }
            if (PMSelectionVisual != null)
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionVisual.Mark);
            }
            if (PMSelectionCollision != null)
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionCollision.Mark);
            }
            if (PMSelectionInertial != null)
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionInertial.Mark);
            }
            if (PMSelectionJointCoordsys != null)
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionJointCoordsys.Mark);
            }
            if (PMSelectionJointAxis != null)
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionJointAxis.Mark);
            }
            if (PMSelectionSiteCoordSys != null)
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, PMSelectionSiteCoordSys.Mark);
            }
        }

        // Drains every PMP-owned SelectionBox mark and repopulates only
        // the marks that belong to the currently-active page-1 section
        // (the expanded accordion group). The net effect is that the
        // SOLIDWORKS viewer highlight reflects ONLY the entities of the
        // active section (not the union of every SelectionBox that ever
        // held a pick for this link).
        //
        // Section (group) -> marks mapping:
        //   LinkJointGroupID:  Global / Joint coord-sys + Joint axis
        //   VisualGroupID:     active visual group
        //   CollisionGroupID:  active collision group
        //   InertialGroupID:   inertial components
        //   SitesGroupID:      active site's reference coord-sys
        //
        // The joint-axis ARROW overlay is NOT driven from here (it is a
        // viewport manipulator, not a SelectionBox mark); the overlay is
        // gated to the Link/Joint section by FillPropertyManager and
        // OnGroupExpand so it doesn't linger while another section is
        // active.
        //
        // The whole sequence runs under suppressGroupListboxRefresh = true
        // so the synthetic OnSelectionboxListChanged events fired by the
        // DeselectAllAtMark / Select4 dance below don't re-enter
        // CommitActive*GroupSelection. The loaders called below ALSO save
        // and restore the flag (see the rationale on each), so this
        // outer guard is the canonical "I'm doing programmatic mark
        // surgery, don't commit anything back" envelope.
        private void RehydrateMarksForActiveSection(LinkNode node, int sectionId)
        {
            if (node == null)
            {
                return;
            }
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                ClearAllSelectionMarks();
                switch (sectionId)
                {
                    case LinkJointGroupID:
                        LoadActiveCoordsysIntoSelectionBox(node);
                        LoadActiveJointAxisIntoSelectionBox(node);
                        break;
                    case VisualGroupID:
                        LoadActiveVisualGroupIntoSelectionBox(node);
                        break;
                    case CollisionGroupID:
                        LoadActiveCollisionGroupIntoSelectionBox(node);
                        break;
                    case InertialGroupID:
                        LoadActiveInertialIntoSelectionBox(node);
                        break;
                    case SitesGroupID:
                        LoadActiveSiteCoordSysIntoSelectionBox(node);
                        break;
                }
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
        }

        public void RefreshVisualGroupsListbox(LinkNode node)
        {
            PMListBoxVisualGroups.Clear();
            if (node == null || node.Link.VisualGroups == null)
            {
                return;
            }
            for (int i = 0; i < node.Link.VisualGroups.Count; i++)
            {
                MeshGroup g = node.Link.VisualGroups[i];
                int count = (g.Components != null) ? g.Components.Count : 0;
                string label = (string.IsNullOrEmpty(g.Name) ? "(unnamed)" : g.Name) +
                    " (" + count + " comp.)";
                PMListBoxVisualGroups.AddItems(label);
            }
            if (activeVisualGroupIndex >= 0 && activeVisualGroupIndex < node.Link.VisualGroups.Count)
            {
                PMListBoxVisualGroups.CurrentSelection = (short)activeVisualGroupIndex;
            }
        }

        public void RefreshCollisionGroupsListbox(LinkNode node)
        {
            PMListBoxCollisionGroups.Clear();
            if (node == null || node.Link.CollisionGroups == null)
            {
                return;
            }
            for (int i = 0; i < node.Link.CollisionGroups.Count; i++)
            {
                MeshGroup g = node.Link.CollisionGroups[i];
                int count = (g.Components != null) ? g.Components.Count : 0;
                string label = (string.IsNullOrEmpty(g.Name) ? "(unnamed)" : g.Name) +
                    " (" + count + " comp.)";
                PMListBoxCollisionGroups.AddItems(label);
            }
            if (activeCollisionGroupIndex >= 0 && activeCollisionGroupIndex < node.Link.CollisionGroups.Count)
            {
                PMListBoxCollisionGroups.CurrentSelection = (short)activeCollisionGroupIndex;
            }
        }

        // Loads the active visual group's name into the group-name textbox
        // so the user can rename it in place. Mirrors LoadActiveSiteIntoForm's
        // name fill. Wrapped in suppressGroupNameTextboxEvents so the
        // programmatic write doesn't re-enter the rename handler and bounce
        // the just-loaded name straight back into the group.
        private void SyncVisualGroupNameTextbox(LinkNode node)
        {
            if (PMTextBoxVisualGroupName == null)
            {
                return;
            }
            bool prior = suppressGroupNameTextboxEvents;
            suppressGroupNameTextboxEvents = true;
            try
            {
                string name = "";
                if (node != null && node.Link.VisualGroups != null &&
                    activeVisualGroupIndex >= 0 &&
                    activeVisualGroupIndex < node.Link.VisualGroups.Count)
                {
                    name = node.Link.VisualGroups[activeVisualGroupIndex].Name ?? "";
                }
                PMTextBoxVisualGroupName.Text = name;
            }
            finally
            {
                suppressGroupNameTextboxEvents = prior;
            }
        }

        private void SyncCollisionGroupNameTextbox(LinkNode node)
        {
            if (PMTextBoxCollisionGroupName == null)
            {
                return;
            }
            bool prior = suppressGroupNameTextboxEvents;
            suppressGroupNameTextboxEvents = true;
            try
            {
                string name = "";
                if (node != null && node.Link.CollisionGroups != null &&
                    activeCollisionGroupIndex >= 0 &&
                    activeCollisionGroupIndex < node.Link.CollisionGroups.Count)
                {
                    name = node.Link.CollisionGroups[activeCollisionGroupIndex].Name ?? "";
                }
                PMTextBoxCollisionGroupName.Text = name;
            }
            finally
            {
                suppressGroupNameTextboxEvents = prior;
            }
        }

        // Ensures the link has non-null VisualGroups / CollisionGroups lists.
        // Legacy flat component lists still migrate into a first visual group,
        // but newly-created links may intentionally have zero visual groups.
        private static void EnsureGroupsInitialized(LinkNode node)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            node.Link.MigrateLegacyComponents();
            if (node.Link.VisualGroups == null)
            {
                node.Link.VisualGroups = new List<MeshGroup>();
            }
            if (node.Link.CollisionGroups == null)
            {
                node.Link.CollisionGroups = new List<MeshGroup>();
            }
        }

        // Builds a default name for a brand-new group that doesn't collide
        // with the existing names on the link (e.g. "<link>_visual_2").
        private static string NextDefaultGroupName(List<MeshGroup> groups, string baseName)
        {
            HashSet<string> existing = new HashSet<string>();
            foreach (MeshGroup g in groups)
            {
                if (!string.IsNullOrEmpty(g.Name))
                {
                    existing.Add(g.Name);
                }
            }
            if (!existing.Contains(baseName))
            {
                return baseName;
            }
            int n = 2;
            while (existing.Contains(baseName + "_" + n))
            {
                n++;
            }
            return baseName + "_" + n;
        }
    }
}
