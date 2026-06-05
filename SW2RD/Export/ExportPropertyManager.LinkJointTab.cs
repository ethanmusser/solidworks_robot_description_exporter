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
using System;

namespace SW2RD.Export
{
    // PMPage UI builder for the "Link / Joint" group. Hosts the per-link
    // text inputs (link name, joint name), the global / joint coordinate
    // system selectors, the joint axis selector with its reverse-direction
    // bitmap button, the joint type combobox, and the per-joint Joint
    // Properties section (limits / dynamics / reference / armature /
    // auto-compute toggle).
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        private void BuildLinkJointTab()
        {
            // "Parent link" dynamic label - the caption gets updated as the
            // user navigates the tree.
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelParentLink = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                LabelLinkNameID, (short)controlType, "", (short)alignment, options, "");

            // Static "Link name" header above the textbox so the
            // section remains self-describing.
            int leftAlign = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            PMLinkJointGroup.AddControl2(LabelLinkNameStaticID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Link name", (short)leftAlign, options,
                "Identifier exported as the URDF/MJCF link/body name.");

            // Link name textbox.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            PMTextBoxLinkName = (PropertyManagerPageTextbox)PMLinkJointGroup.AddControl2(
                TextBoxLinkNameID, (short)controlType, "base_link", (short)alignment, options,
                "Enter the name of the link");

            // Joint Name label + textbox. SolidWorks requires distinct
            // control IDs or controls can leak onto unrelated tabs.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelJointName = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                LabelJointNameID, (short)controlType, "Joint name", (short)leftAlign, options,
                "Enter the name of the joint");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            PMTextBoxJointName = (PropertyManagerPageTextbox)PMLinkJointGroup.AddControl2(
                TextBoxJointNameID, (short)controlType, "", (short)alignment, options,
                "Enter the name of the joint");

            // Build the global / joint coord-system selectors and the
            // joint-axis selector + reverse-direction button.
            object coordSysFilterObj = new swSelectType_e[] { swSelectType_e.swSelCOORDSYS };
            object axisFilterObj = new swSelectType_e[] { swSelectType_e.swSelDATUMAXES };

            BuildGlobalCoordsysControls(coordSysFilterObj);
            BuildWorldAttachmentControls();
            BuildJointCoordsysControls(coordSysFilterObj);
            BuildJointAxisControls(axisFilterObj);
            BuildJointTypeControls();
            BuildJointPropertiesControls();
        }

        // World attachment combobox (Welded / Free) for top-level bodies.
        // Only enabled when the active node is a top-level body (immediate
        // child of the WorldNode); disabled for the WorldNode itself and
        // for nested links. URDF export ignores this field; MJCF emits a
        // <freejoint/> on the body when set to Free.
        private void BuildWorldAttachmentControls()
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            string tip = "How a top-level body attaches to the world. Welded = body is rigidly fixed; Free = MJCF emits a <freejoint/> on the body so it floats with 6 DoF. URDF ignores this and always emits a fixed-base base_link.";
            PMLabelWorldAttachment = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                LabelWorldAttachmentID, (short)controlType,
                "World attachment", (short)alignment, options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMComboBoxWorldAttachment = (PropertyManagerPageCombobox)PMLinkJointGroup.AddControl2(
                ComboBoxWorldAttachmentID, (short)controlType,
                "World attachment", (short)alignment, options, tip);
            PMComboBoxWorldAttachment.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            // Order MUST match WorldAttachmentModel (Welded = 0, Free = 1)
            // so the combobox index can be cast directly to the enum.
            PMComboBoxWorldAttachment.AddItems(new string[] { "Welded", "Free" });
        }

        // Global Origin coord system label + single-entity SelectionBox.
        // Picking a coord system in the SW tree commits its name through
        // OnSelectionboxListChanged; FillPropertyManager / OnTabClicked
        // rehydrate the box via SelectByID2 + Mark when the user revisits
        // the tab. Empty box = "Automatically Generate" semantics at
        // export time.
        private void BuildGlobalCoordsysControls(object coordSysFilterObj)
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            string tip = "Pick the reference coordinate system that defines the global origin. Leave empty to auto-generate one at the assembly origin.";
            PMLabelGlobalCoordsys = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                IDLabelGlobalCoordsys, (short)controlType,
                "Global origin coordinate system", (short)alignment, options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionGlobalCoordsys = (PropertyManagerPageSelectionbox)PMLinkJointGroup.AddControl2(
                SelectionGlobalCoordsysID, (short)controlType,
                "Pick global origin coordinate system", (short)alignment, options, tip);
            // SingleEntityOnly = true matches SW's coord-system and mate
            // pickers: a new pick overwrites the prior contents in place.
            // Height = 18 is the SW-native single-row selectionbox height.
            //
            // AllowSelectInMultipleBoxes = false keeps each feature picker
            // bound to one semantic role. The *SelectionMark constants in
            // ExportPropertyManager.cs are unique bitmasks, so each picker
            // also receives only selections made for its own mark.
            PMSelectionGlobalCoordsys.AllowSelectInMultipleBoxes = false;
            PMSelectionGlobalCoordsys.SingleEntityOnly = true;
            PMSelectionGlobalCoordsys.AllowMultipleSelectOfSameEntity = false;
            PMSelectionGlobalCoordsys.Height = 18;
            PMSelectionGlobalCoordsys.SetSelectionFilters(coordSysFilterObj);
            PMSelectionGlobalCoordsys.Mark = GlobalCoordSysSelectionMark;
        }

        private void BuildJointCoordsysControls(object coordSysFilterObj)
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            // Visible | Enabled at create time; EnableControls flips both
            // off for the base node which has no joint to anchor.
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            // The "Link coordinate system" picker doubles as both the
            // joint-origin coord-sys (nested links) and the world->body
            // offset coord-sys (top-level bodies). The label is
            // generic/role-neutral on purpose so it reads correctly for
            // either case; the underlying field on the data model is the
            // same Link.Joint.CoordinateSystemName in both. The picker is
            // disabled on the WorldNode itself (the WorldNode owns the
            // Global Origin picker, not this one).
            string tip = "Pick the reference coordinate system that defines this body's frame. For a top-level body it is the world->body offset; for a nested link it is the joint origin. Leave empty to auto-generate one from the parent/child kinematic chain.";
            PMLabelCoordSys = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                LabelCoordSysID, (short)controlType,
                "Link coordinate system", (short)alignment, options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionJointCoordsys = (PropertyManagerPageSelectionbox)PMLinkJointGroup.AddControl2(
                SelectionJointCoordsysID, (short)controlType,
                "Pick link coordinate system", (short)alignment, options, tip);
            // SW-native single-entity overwrite UX - see
            // PMSelectionGlobalCoordsys above for the full rationale,
            // including why AllowSelectInMultipleBoxes is FALSE
            // (semantic exclusivity: the same coord-sys can never be
            // both the global origin and a joint origin in the data
            // model, so the picker must move the entity out of the
            // sibling box on a fresh pick).
            PMSelectionJointCoordsys.AllowSelectInMultipleBoxes = false;
            PMSelectionJointCoordsys.SingleEntityOnly = true;
            PMSelectionJointCoordsys.AllowMultipleSelectOfSameEntity = false;
            PMSelectionJointCoordsys.Height = 18;
            PMSelectionJointCoordsys.SetSelectionFilters(coordSysFilterObj);
            PMSelectionJointCoordsys.Mark = JointCoordSysSelectionMark;
        }

        private void BuildJointAxisControls(object axisFilterObj)
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            string tip = "Pick the SolidWorks reference axis that defines the joint motion direction. Toggle the auto-derive checkbox to let the exporter resolve the axis from the kinematic chain instead.";
            PMLabelAxes = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                LabelAxesID, (short)controlType, "Joint axis", (short)alignment, options, tip);

            // "Auto-derive axis from kinematic chain" toggle. Defaults
            // off so new joints require an explicit reference-axis pick
            // unless the user opts into inference from mates.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            PMCheckAutoDeriveAxis = (PropertyManagerPageCheckbox)PMLinkJointGroup.AddControl2(
                CheckAutoDeriveAxisID, (short)controlType,
                "Auto-derive axis from kinematic chain", (short)alignment, options,
                "When checked, the joint axis is resolved from the SolidWorks mates at export time and the picker below is ignored.");
            PMCheckAutoDeriveAxis.Checked = false;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionJointAxis = (PropertyManagerPageSelectionbox)PMLinkJointGroup.AddControl2(
                SelectionJointAxisID, (short)controlType, "Pick joint axis",
                (short)alignment, options, tip);
            // SW-native single-entity overwrite UX - see
            // PMSelectionGlobalCoordsys above for the full rationale.
            // AllowSelectInMultipleBoxes = FALSE keeps the joint axis
            // pick exclusive to this box (different selection filter
            // from the coord-sys pickers, but the semantic-exclusivity
            // intent is the same: one logical role, one home box).
            PMSelectionJointAxis.AllowSelectInMultipleBoxes = false;
            PMSelectionJointAxis.SingleEntityOnly = true;
            PMSelectionJointAxis.AllowMultipleSelectOfSameEntity = false;
            PMSelectionJointAxis.Height = 18;
            PMSelectionJointAxis.SetSelectionFilters(axisFilterObj);
            PMSelectionJointAxis.Mark = JointAxisSelectionMark;

            // "Reverse Direction" bitmap button - same standard icon SW
            // uses on its own coord-system / extrude PMs. SW renders text
            // buttons as full-width rows, so this control is stacked below
            // the axis selectionbox.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_BitmapButton;
            PMBitmapAxisFlip = (PropertyManagerPageBitmapButton)PMLinkJointGroup.AddControl2(
                BitmapAxisFlipID, (short)controlType, "Reverse Direction",
                (short)alignment, options,
                "Reverse the positive direction of the reference axis");
            PMBitmapAxisFlip.SetStandardBitmaps(
                (int)swPropertyManagerPageBitmapButtons_e.swBitmapButtonImage_reverse_direction);
        }

        private void BuildJointTypeControls()
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            string tip = "Select the joint type";
            PMLabelJointType = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                LabelJointTypeID, (short)controlType, "Joint type", (short)alignment, options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMComboBoxJointType = (PropertyManagerPageCombobox)PMLinkJointGroup.AddControl2(
                ComboBoxJointTypeID, (short)controlType, "Joint type",
                (short)alignment, options, tip);
            PMComboBoxJointType.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxJointType.AddItems(new string[] {
                "", "fixed", "revolute", "prismatic" });
        }

        // Joint Properties section: per-joint Limits / Dynamics / Reference
        // / Armature inputs plus the per-joint "auto-compute Lower/Upper
        // from a SolidWorks limit mate" toggle. All controls disable on
        // the base node along with the rest of the joint row via
        // EnableControls.
        //
        // Empty textbox -> the underlying URDFAttribute is cleared so the
        // writer omits the attribute entirely. URDF-only fields (Velocity)
        // are silently dropped on MJCF export; MJCF-only fields (Armature,
        // Reference) are silently dropped on URDF export.
        private void BuildJointPropertiesControls()
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            PMLabelJointProperties = (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(
                LabelJointPropertiesID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Joint Properties",
                (short)alignment, options,
                "Optional limits, dynamics, and MJCF-only reference / armature for this joint. Leave empty to omit.");

            PMCheckAutoComputeLimits = (PropertyManagerPageCheckbox)PMLinkJointGroup.AddControl2(
                CheckAutoComputeLimitsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Auto-compute Lower/Upper from limit mate",
                (short)alignment, options,
                "When checked, Lower and Upper are derived from the SolidWorks limit mate on this joint at export time, overwriting any values typed below. Uncheck to use the values typed below verbatim.");
            PMCheckAutoComputeLimits.Checked = true;

            PMLabelJointLower = AddJointPropertyLabel(LabelJointLowerID,
                "Lower [deg or m]",
                "Lower limit of the joint range. Degrees for revolute, meters for prismatic.");
            PMTextBoxJointLower = AddJointPropertyTextbox(TextBoxJointLowerID,
                "Lower limit (blank = none)");

            PMLabelJointUpper = AddJointPropertyLabel(LabelJointUpperID,
                "Upper [deg or m]",
                "Upper limit of the joint range. Degrees for revolute, meters for prismatic.");
            PMTextBoxJointUpper = AddJointPropertyTextbox(TextBoxJointUpperID,
                "Upper limit (blank = none)");

            PMLabelJointEffort = AddJointPropertyLabel(LabelJointEffortID,
                "Effort [N or N*m]",
                "Magnitude of the maximum actuator effort. Maps to URDF <limit effort> and MJCF <joint actuatorfrcrange='-effort effort'>.");
            PMTextBoxJointEffort = AddJointPropertyTextbox(TextBoxJointEffortID,
                "Effort limit (blank = none)");

            PMLabelJointVelocity = AddJointPropertyLabel(LabelJointVelocityID,
                "Velocity [deg/s or m/s] (URDF)",
                "Maximum joint velocity. Degrees/second for revolute, meters/second for prismatic. URDF <limit velocity>; ignored on MJCF export.");
            PMTextBoxJointVelocity = AddJointPropertyTextbox(TextBoxJointVelocityID,
                "Velocity limit (blank = none, URDF only)");

            PMLabelJointDamping = AddJointPropertyLabel(LabelJointDampingID,
                "Damping [N*m*s/deg or N*s/m]",
                "Viscous damping coefficient. Enter N*m*s/deg for revolute or N*s/m for prismatic. Export converts angular damping to the per-radian coefficient used by URDF/MJCF.");
            PMTextBoxJointDamping = AddJointPropertyTextbox(TextBoxJointDampingID,
                "Damping (blank = none)");

            PMLabelJointFriction = AddJointPropertyLabel(LabelJointFrictionID,
                "Friction [N*m or N]",
                "Static (Coulomb) friction. Maps to URDF <dynamics friction> and MJCF <joint frictionloss>.");
            PMTextBoxJointFriction = AddJointPropertyTextbox(TextBoxJointFrictionID,
                "Friction (blank = none)");

            PMLabelJointArmature = AddJointPropertyLabel(LabelJointArmatureID,
                "Armature [kg*m^2 or kg] (MJCF)",
                "Equivalent rotor inertia of the actuator. MJCF <joint armature>; ignored on URDF export.");
            PMTextBoxJointArmature = AddJointPropertyTextbox(TextBoxJointArmatureID,
                "Armature (blank = none, MJCF only)");

            PMLabelJointReference = AddJointPropertyLabel(LabelJointReferenceID,
                "Reference [deg or m] (MJCF)",
                "Joint position assumed by the model when MuJoCo loads it. Degrees for hinge joints, meters for slide joints. MJCF <joint ref>; ignored on URDF export.");
            PMTextBoxJointReference = AddJointPropertyTextbox(TextBoxJointReferenceID,
                "Reference position (blank = 0, MJCF only)");
        }

        private PropertyManagerPageLabel AddJointPropertyLabel(int id, string caption, string tip)
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            return (PropertyManagerPageLabel)PMLinkJointGroup.AddControl2(id,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                caption, (short)alignment, options, tip);
        }

        private PropertyManagerPageTextbox AddJointPropertyTextbox(int id, string tip)
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            return (PropertyManagerPageTextbox)PMLinkJointGroup.AddControl2(id,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)alignment, options, tip);
        }
    }
}
