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

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2RD.Input;
using System;

namespace SW2RD.Export
{
    // PMPage UI builder for the Visual / Collision / Inertial component
    // tabs. Each tab is a flat list of controls because the tab strip is
    // already the primary navigation surface.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        private void BuildComponentsTabs()
        {
            // No static "Visual" / "Collision" / "Inertial" heading labels
            // here - the accordion group caption already names each section,
            // so an in-body label repeating it is redundant. The first
            // descriptive label in each editor below ("Visual Groups",
            // "Collision Groups", "Inertial Source") carries the usage hint.
            // LabelVisualHeaderID / LabelCollisionHeaderID /
            // LabelInertialHeaderID remain reserved (unused) in
            // ExportPropertyManager.cs.
            object filterObj = new swSelectType_e[] { swSelectType_e.swSelCOMPONENTS };

            BuildInertialSourceCombobox();
            BuildVisualGroupsEditor(filterObj);
            BuildCollisionUsesVisualToggle();
            BuildCollisionGroupsEditor(filterObj);
            BuildInertialComponentsSelector(filterObj);
        }

        private void BuildInertialSourceCombobox()
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelInertialSource = (PropertyManagerPageLabel)PMInertialGroup.AddControl2(
                LabelInertialSourceID, (short)controlType, "Inertial Source",
                (short)alignment, options,
                "Choose which set of components drives the link's mass and inertia");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMComboBoxInertialSource = (PropertyManagerPageCombobox)PMInertialGroup.AddControl2(
                ComboInertialSourceID, (short)controlType, "Inertial Source",
                (short)alignment, options,
                "Visual: use visual components. Collision: use collision components. Custom: use the inertial components box below.");
            PMComboBoxInertialSource.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxInertialSource.AddItems(new string[] {
                "Visual",
                "Collision",
                "Custom (Inertial Components)" });
            PMComboBoxInertialSource.CurrentSelection = 0;
        }

        // Visual Groups editor: header label, help label, listbox of
        // groups, the SelectionBox that edits the *active* group's
        // components, the new-group name field, and Add / Remove buttons.
        // Each visual group becomes one STL + one <visual> (URDF) /
        // <mesh>+<geom class="visual"> (MJCF) on export.
        private void BuildVisualGroupsEditor(object componentFilter)
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelVisualComponents = (PropertyManagerPageLabel)PMVisualGroup.AddControl2(
                LabelVisualID, (short)controlType, "Visual Groups", (short)alignment, options,
                "Define one or more named groups of components. Each group is exported as its own visual mesh.");

            PMVisualGroup.AddControl2(
                VisualGroupsHelpLabelID, (short)controlType,
                "Click a row to load that group's components into the box below and its name into the Group name box (edit it to rename in place). Click Add Visual Group to create a new group.",
                (short)alignment, options,
                "Components selected in the box below belong to the highlighted group.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            PMListBoxVisualGroups = (PropertyManagerPageListbox)PMVisualGroup.AddControl2(
                VisualGroupsListBoxID, (short)controlType, "", (short)alignment, options,
                "Visual groups defined for this link. Click a row to edit it; click Remove Selected Group to delete it.");
            PMListBoxVisualGroups.Height = 150;

            // BUGFIX: this header MUST be a Label. controlType is still
            // swControlType_Listbox here (reused from the listbox above),
            // so emit the label with an explicit Label type rather than
            // the stale controlType value - otherwise SW registers this as
            // a second listbox of the wrong type, which intermittently
            // renders as a missing label above the SelectionBox.
            PMVisualGroup.AddControl2(LabelVisualComponentsHeaderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Components in active visual group",
                (short)alignment, options,
                "Components belonging to the visual group highlighted above.");

            // SelectionBox sits directly under the listbox so the visual
            // flow is "pick a row -> edit its components below".
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionVisual = (PropertyManagerPageSelectionbox)PMVisualGroup.AddControl2(
                SelectionVisualID, (short)controlType,
                "Components for the highlighted visual group", (short)alignment, options,
                "Components belonging to the visual group selected above.");
            PMSelectionVisual.AllowSelectInMultipleBoxes = true;
            PMSelectionVisual.SingleEntityOnly = false;
            PMSelectionVisual.AllowMultipleSelectOfSameEntity = false;
            PMSelectionVisual.Height = 40;
            PMSelectionVisual.SetSelectionFilters(componentFilter);
            PMSelectionVisual.Mark = VisualSelectionMark;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            PMVisualGroup.AddControl2(
                VisualGroupsNameLabelID, (short)controlType, "Group name",
                (short)alignment, options,
                "Display name of the selected group and the suffix on its mesh filename.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMTextBoxVisualGroupName = (PropertyManagerPageTextbox)PMVisualGroup.AddControl2(
                VisualGroupsNameTextBoxID, (short)controlType, "", (short)alignment, options,
                "Edit to rename the selected visual group.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            PMButtonVisualGroupAdd = (PropertyManagerPageButton)PMVisualGroup.AddControl2(
                VisualGroupsAddButtonID, (short)controlType, "Add Visual Group", 0, options,
                "Save the current selection into the highlighted group, then create a new empty group.");

            PMButtonVisualGroupRemove = (PropertyManagerPageButton)PMVisualGroup.AddControl2(
                VisualGroupsRemoveButtonID, (short)controlType, "Remove Selected Visual Group", 0, options,
                "Delete the highlighted visual group from this link.");
        }

        // "Use visual groups as collision" toggle. When checked,
        // SetCollisionEditorEnabled(false) greys out the entire collision
        // editor below (Enabled=false but still rendered) and ExportHelper
        // reuses the visual meshes for collision via
        // Link.CollisionUsesVisual.
        private void BuildCollisionUsesVisualToggle()
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMCheckCollisionUsesVisual = PMCollisionGroup.AddControl2(
                CheckCollisionUsesVisualID, (short)controlType,
                "Use visual groups as collision", (short)alignment, options,
                "When checked, the visual groups are reused as collision meshes; the collision editor below is hidden so you don't have to re-pick the same components.");
            PMCheckCollisionUsesVisual.Checked = Link.DefaultCollisionUsesVisual;
        }

        // Collision Groups editor. Mirrors Visual Groups. An empty list
        // falls back to using the visual meshes for collision (URDF/MJCF
        // backward-compat behavior).
        private void BuildCollisionGroupsEditor(object componentFilter)
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelCollisionComponents = (PropertyManagerPageLabel)PMCollisionGroup.AddControl2(
                LabelCollisionID, (short)controlType, "Collision Groups", (short)alignment, options,
                "Define one or more named groups of components. Each group is exported as its own collision mesh. Empty list reuses the visual meshes for collision.");

            PMLabelCollisionGroupsHelp = (PropertyManagerPageLabel)PMCollisionGroup.AddControl2(
                CollisionGroupsHelpLabelID, (short)controlType,
                "Click a row to load that group's components into the box below and its name into the Group name box (edit it to rename in place). Click Add Collision Group to create a new group.",
                (short)alignment, options,
                "Components selected in the box below belong to the highlighted group.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            PMListBoxCollisionGroups = (PropertyManagerPageListbox)PMCollisionGroup.AddControl2(
                CollisionGroupsListBoxID, (short)controlType, "", (short)alignment, options,
                "Collision groups defined for this link.");
            PMListBoxCollisionGroups.Height = 150;

            // BUGFIX: explicit Label type - see the matching note on the
            // visual header above. controlType is still Listbox here.
            PMCollisionGroup.AddControl2(LabelCollisionComponentsHeaderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Components in active collision group",
                (short)alignment, options,
                "Components belonging to the collision group highlighted above.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionCollision = (PropertyManagerPageSelectionbox)PMCollisionGroup.AddControl2(
                SelectionCollisionID, (short)controlType,
                "Components for the highlighted collision group", (short)alignment, options,
                "Components belonging to the collision group selected above.");
            PMSelectionCollision.AllowSelectInMultipleBoxes = true;
            PMSelectionCollision.SingleEntityOnly = false;
            PMSelectionCollision.AllowMultipleSelectOfSameEntity = false;
            PMSelectionCollision.Height = 40;
            PMSelectionCollision.SetSelectionFilters(componentFilter);
            PMSelectionCollision.Mark = CollisionSelectionMark;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            PMLabelCollisionGroupsName = (PropertyManagerPageLabel)PMCollisionGroup.AddControl2(
                CollisionGroupsNameLabelID, (short)controlType, "Group name",
                (short)alignment, options,
                "Display name of the selected group and the suffix on its mesh filename.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMTextBoxCollisionGroupName = (PropertyManagerPageTextbox)PMCollisionGroup.AddControl2(
                CollisionGroupsNameTextBoxID, (short)controlType, "", (short)alignment, options,
                "Edit to rename the selected collision group.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            PMButtonCollisionGroupAdd = (PropertyManagerPageButton)PMCollisionGroup.AddControl2(
                CollisionGroupsAddButtonID, (short)controlType, "Add Collision Group", 0, options,
                "Save the current selection into the highlighted group, then create a new empty group.");

            PMButtonCollisionGroupRemove = (PropertyManagerPageButton)PMCollisionGroup.AddControl2(
                CollisionGroupsRemoveButtonID, (short)controlType,
                "Remove Selected Collision Group", 0, options,
                "Delete the highlighted collision group from this link.");
        }

        // Inertial Components selector (only consulted when Inertial
        // Source = Custom). Single SelectionBox; no group abstraction
        // because inertial doesn't emit per-group meshes.
        private void BuildInertialComponentsSelector(object componentFilter)
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelInertialComponents = (PropertyManagerPageLabel)PMInertialGroup.AddControl2(
                LabelInertialID, (short)controlType,
                "Inertial Components (used when source = Custom)",
                (short)alignment, options,
                "Optional. When Inertial Source is Custom, mass and inertia are computed from these components.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionInertial = (PropertyManagerPageSelectionbox)PMInertialGroup.AddControl2(
                SelectionInertialID, (short)controlType, "Inertial Components",
                (short)alignment, options, "");
            PMSelectionInertial.AllowSelectInMultipleBoxes = true;
            PMSelectionInertial.SingleEntityOnly = false;
            PMSelectionInertial.AllowMultipleSelectOfSameEntity = false;
            PMSelectionInertial.Height = 40;
            PMSelectionInertial.SetSelectionFilters(componentFilter);
            PMSelectionInertial.Mark = InertialSelectionMark;
        }
    }
}
