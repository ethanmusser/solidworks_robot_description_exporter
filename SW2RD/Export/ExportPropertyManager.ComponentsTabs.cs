using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using System;

namespace SW2RD.Export
{
    // PMPage UI builder for the Visual / Collision / Inertial component
    // tabs. Each tab is a flat list of controls (heading label, listbox /
    // selection / name / Add / Remove for groups; combobox + selection
    // for inertial). The per-tab single-group wrapper was retired - it
    // added a redundant collapsible header with no organizational value
    // now that the tab strip is the primary navigation.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        private void BuildComponentsTabs()
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            // Static heading on each tab so the visual hierarchy still
            // reads as "Visual" / "Collision" / "Inertial".
            PMVisualTab.AddControl2(LabelVisualHeaderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Visual", (short)alignment, options,
                "Components included in the visual mesh export for this link.");
            PMCollisionTab.AddControl2(LabelCollisionHeaderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Collision", (short)alignment, options,
                "Components included in the collision mesh export for this link.");
            PMInertialTab.AddControl2(LabelInertialHeaderID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Inertial", (short)alignment, options,
                "Components driving the link's mass and inertia computation.");

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
            PMLabelInertialSource = (PropertyManagerPageLabel)PMInertialTab.AddControl2(
                LabelInertialSourceID, (short)controlType, "Inertial Source",
                (short)alignment, options,
                "Choose which set of components drives the link's mass and inertia");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMComboBoxInertialSource = (PropertyManagerPageCombobox)PMInertialTab.AddControl2(
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
            PMLabelVisualComponents = (PropertyManagerPageLabel)PMVisualTab.AddControl2(
                LabelVisualID, (short)controlType, "Visual Groups", (short)alignment, options,
                "Define one or more named groups of components. Each group is exported as its own visual mesh.");

            PMVisualTab.AddControl2(
                VisualGroupsHelpLabelID, (short)controlType,
                "Click a row to load that group's components into the box below. To add a new group, type a name and click Add Group.",
                (short)alignment, options,
                "Components selected in the box below belong to the highlighted group.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            PMListBoxVisualGroups = (PropertyManagerPageListbox)PMVisualTab.AddControl2(
                VisualGroupsListBoxID, (short)controlType, "", (short)alignment, options,
                "Visual groups defined for this link. Click a row to edit it; click Remove Selected Group to delete it.");
            PMListBoxVisualGroups.Height = 50;

            PMVisualTab.AddControl2(LabelVisualComponentsHeaderID,
                (short)controlType, "Components in active visual group",
                (short)alignment, options,
                "Components belonging to the visual group highlighted above.");

            // SelectionBox sits directly under the listbox so the visual
            // flow is "pick a row -> edit its components below".
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionVisual = (PropertyManagerPageSelectionbox)PMVisualTab.AddControl2(
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
            PMVisualTab.AddControl2(
                VisualGroupsNameLabelID, (short)controlType, "Group name (for new group)",
                (short)alignment, options,
                "Used as the new group's display name and as the suffix on its mesh filename.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMTextBoxVisualGroupName = (PropertyManagerPageTextbox)PMVisualTab.AddControl2(
                VisualGroupsNameTextBoxID, (short)controlType, "", (short)alignment, options,
                "Group name for the next group to add.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            PMButtonVisualGroupAdd = (PropertyManagerPageButton)PMVisualTab.AddControl2(
                VisualGroupsAddButtonID, (short)controlType, "Add Visual Group", 0, options,
                "Save the current selection into the highlighted group, then create a new empty group.");

            PMButtonVisualGroupRemove = (PropertyManagerPageButton)PMVisualTab.AddControl2(
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
            PMCheckCollisionUsesVisual = PMCollisionTab.AddControl2(
                CheckCollisionUsesVisualID, (short)controlType,
                "Use visual groups as collision", (short)alignment, options,
                "When checked, the visual groups are reused as collision meshes; the collision editor below is hidden so you don't have to re-pick the same components.");
            PMCheckCollisionUsesVisual.Checked = false;
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
            PMLabelCollisionComponents = (PropertyManagerPageLabel)PMCollisionTab.AddControl2(
                LabelCollisionID, (short)controlType, "Collision Groups", (short)alignment, options,
                "Define one or more named groups of components. Each group is exported as its own collision mesh. Empty list reuses the visual meshes for collision.");

            PMLabelCollisionGroupsHelp = (PropertyManagerPageLabel)PMCollisionTab.AddControl2(
                CollisionGroupsHelpLabelID, (short)controlType,
                "Click a row to load that group's components into the box below. To add a new group, type a name and click Add Group.",
                (short)alignment, options,
                "Components selected in the box below belong to the highlighted group.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            PMListBoxCollisionGroups = (PropertyManagerPageListbox)PMCollisionTab.AddControl2(
                CollisionGroupsListBoxID, (short)controlType, "", (short)alignment, options,
                "Collision groups defined for this link.");
            PMListBoxCollisionGroups.Height = 50;

            PMCollisionTab.AddControl2(LabelCollisionComponentsHeaderID,
                (short)controlType, "Components in active collision group",
                (short)alignment, options,
                "Components belonging to the collision group highlighted above.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionCollision = (PropertyManagerPageSelectionbox)PMCollisionTab.AddControl2(
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
            PMLabelCollisionGroupsName = (PropertyManagerPageLabel)PMCollisionTab.AddControl2(
                CollisionGroupsNameLabelID, (short)controlType, "Group name (for new group)",
                (short)alignment, options,
                "Used as the new group's display name and as the suffix on its mesh filename.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMTextBoxCollisionGroupName = (PropertyManagerPageTextbox)PMCollisionTab.AddControl2(
                CollisionGroupsNameTextBoxID, (short)controlType, "", (short)alignment, options,
                "Group name for the next group to add.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            PMButtonCollisionGroupAdd = (PropertyManagerPageButton)PMCollisionTab.AddControl2(
                CollisionGroupsAddButtonID, (short)controlType, "Add Collision Group", 0, options,
                "Save the current selection into the highlighted group, then create a new empty group.");

            PMButtonCollisionGroupRemove = (PropertyManagerPageButton)PMCollisionTab.AddControl2(
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
            PMLabelInertialComponents = (PropertyManagerPageLabel)PMInertialTab.AddControl2(
                LabelInertialID, (short)controlType,
                "Inertial Components (used when source = Custom)",
                (short)alignment, options,
                "Optional. When Inertial Source is Custom, mass and inertia are computed from these components.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionInertial = (PropertyManagerPageSelectionbox)PMInertialTab.AddControl2(
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
