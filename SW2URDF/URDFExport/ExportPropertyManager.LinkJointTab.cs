using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using System;

namespace SW2URDF.URDFExport
{
    // PMPage UI builder for the "Link/Joint" tab. Hosts the per-link
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
            PMLabelParentLink = (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(
                LabelLinkNameID, (short)controlType, "", (short)alignment, options, "");

            // Static "Link name" header above the link-name textbox so
            // a new user can tell what the textbox does. The legacy
            // layout relied on context from the WinForms popup that's
            // no longer here.
            int leftAlign = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            PMLinkJointTab.AddControl2(LabelLinkNameStaticID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Link name", (short)leftAlign, options,
                "Identifier exported as the URDF/MJCF link/body name.");

            // Link name textbox.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            PMTextBoxLinkName = (PropertyManagerPageTextbox)PMLinkJointTab.AddControl2(
                TextBoxLinkNameID, (short)controlType, "base_link", (short)alignment, options,
                "Enter the name of the link");

            // Joint Name label + textbox. Distinct ID per control so
            // SolidWorks doesn't leak the textbox onto a different tab
            // (see AGENTS.md "unique IDs per PMPage").
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelJointName = (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(
                LabelJointNameID, (short)controlType, "Joint name", (short)leftAlign, options,
                "Enter the name of the joint");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            PMTextBoxJointName = (PropertyManagerPageTextbox)PMLinkJointTab.AddControl2(
                TextBoxJointNameID, (short)controlType, "", (short)alignment, options,
                "Enter the name of the joint");

            // Build the global / joint coord-system selectors and the
            // joint-axis selector + reverse-direction button.
            object coordSysFilterObj = new swSelectType_e[] { swSelectType_e.swSelCOORDSYS };
            object axisFilterObj = new swSelectType_e[] { swSelectType_e.swSelDATUMAXES };

            BuildGlobalCoordsysControls(coordSysFilterObj);
            BuildJointCoordsysControls(coordSysFilterObj);
            BuildJointAxisControls(axisFilterObj);
            BuildJointTypeControls();
            BuildJointPropertiesControls();
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
            PMLabelGlobalCoordsys = (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(
                IDLabelGlobalCoordsys, (short)controlType,
                "Global origin coordinate system", (short)alignment, options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionGlobalCoordsys = (PropertyManagerPageSelectionbox)PMLinkJointTab.AddControl2(
                SelectionGlobalCoordsysID, (short)controlType,
                "Pick global origin coordinate system", (short)alignment, options, tip);
            // SingleEntityOnly = true matches SW's own coord-system /
            // mate creation PMP single-entity pickers: a new pick
            // OVERWRITES the prior contents in place. Height = 18 is
            // the SW-native single-row selectionbox height. The
            // earlier multi-entity (Height=30, SingleEntityOnly=false)
            // configuration was tried because rendering looked
            // unreliable, but that turned out to be symptomatic of the
            // EnableControls.Visible-toggle leak (fixed) and the
            // GetRefAxis SelectionMgr clobber (fixed) - not anything
            // intrinsic to SingleEntityOnly.
            //
            // AllowSelectInMultipleBoxes = FALSE: each feature picker
            // represents a SEMANTICALLY DISTINCT logical role - global
            // origin coord-sys, joint coord-sys, joint axis, site
            // coord-sys - and the same entity must never occupy two of
            // them at once. With it set to true, picking the same
            // coord-sys in box B (e.g. Joint) would DUPLICATE it into
            // both A (Global) and B; with it set to false, picking it
            // in B MOVES it out of A, so the user's last action wins
            // and the data model never has the same feature filling
            // two distinct roles. (Note: the previously-observed
            // cross-tab bleed where one pick rendered in every sibling
            // SelectionBox was NOT caused by this setting; it was a
            // mark bitmask collision and is fixed at the
            // *SelectionMark constants in ExportPropertyManager.cs.
            // See AGENTS.md for the marks-must-be-powers-of-two rule.)
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
            string tip = "Pick the reference coordinate system that defines the joint origin. Leave empty to auto-generate one from the parent/child kinematic chain.";
            PMLabelCoordSys = (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(
                LabelCoordSysID, (short)controlType,
                "Joint coordinate system", (short)alignment, options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionJointCoordsys = (PropertyManagerPageSelectionbox)PMLinkJointTab.AddControl2(
                SelectionJointCoordsysID, (short)controlType,
                "Pick joint coordinate system", (short)alignment, options, tip);
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
            PMLabelAxes = (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(
                LabelAxesID, (short)controlType, "Joint axis", (short)alignment, options, tip);

            // "Auto-derive axis from kinematic chain" toggle. When
            // checked, the SelectionBox is disabled and AxisName is
            // ignored at export time.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            PMCheckAutoDeriveAxis = (PropertyManagerPageCheckbox)PMLinkJointTab.AddControl2(
                CheckAutoDeriveAxisID, (short)controlType,
                "Auto-derive axis from kinematic chain", (short)alignment, options,
                "When checked, the joint axis is resolved from the SolidWorks mates at export time and the picker below is ignored.");
            PMCheckAutoDeriveAxis.Checked = true;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionJointAxis = (PropertyManagerPageSelectionbox)PMLinkJointTab.AddControl2(
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
            // uses on its own coord-system / extrude PMs. Stacked on the
            // row below the axis selectionbox; SW does not reliably honor
            // side-by-side layout hints for PM controls (see AGENTS.md
            // "PropertyManagerPage layout quirks"). The icon makes the
            // intent clear regardless of position.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_BitmapButton;
            PMBitmapAxisFlip = (PropertyManagerPageBitmapButton)PMLinkJointTab.AddControl2(
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
            PMLabelJointType = (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(
                LabelJointTypeID, (short)controlType, "Joint type", (short)alignment, options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMComboBoxJointType = (PropertyManagerPageCombobox)PMLinkJointTab.AddControl2(
                ComboBoxJointTypeID, (short)controlType, "Joint type",
                (short)alignment, options, tip);
            PMComboBoxJointType.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxJointType.AddItems(new string[] {
                "Automatically Detect", "continuous", "revolute", "prismatic", "fixed" });
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

            PMLabelJointProperties = (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(
                LabelJointPropertiesID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Joint Properties",
                (short)alignment, options,
                "Optional limits, dynamics, and MJCF-only reference / armature for this joint. Leave empty to omit.");

            PMCheckAutoComputeLimits = (PropertyManagerPageCheckbox)PMLinkJointTab.AddControl2(
                CheckAutoComputeLimitsID,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Auto-compute Lower/Upper from limit mate",
                (short)alignment, options,
                "When checked, Lower and Upper are derived from the SolidWorks limit mate on this joint at export time, overwriting any values typed below. Uncheck to use the values typed below verbatim.");
            PMCheckAutoComputeLimits.Checked = true;

            PMLabelJointLower = AddJointPropertyLabel(LabelJointLowerID,
                "Lower [rad or m]",
                "Lower limit of the joint range. Radians for revolute, meters for prismatic.");
            PMTextBoxJointLower = AddJointPropertyTextbox(TextBoxJointLowerID,
                "Lower limit (blank = none)");

            PMLabelJointUpper = AddJointPropertyLabel(LabelJointUpperID,
                "Upper [rad or m]",
                "Upper limit of the joint range.");
            PMTextBoxJointUpper = AddJointPropertyTextbox(TextBoxJointUpperID,
                "Upper limit (blank = none)");

            PMLabelJointEffort = AddJointPropertyLabel(LabelJointEffortID,
                "Effort [N or N*m]",
                "Magnitude of the maximum actuator effort. Maps to URDF <limit effort> and MJCF <joint actuatorfrcrange='-effort effort'>.");
            PMTextBoxJointEffort = AddJointPropertyTextbox(TextBoxJointEffortID,
                "Effort limit (blank = none)");

            PMLabelJointVelocity = AddJointPropertyLabel(LabelJointVelocityID,
                "Velocity [rad/s or m/s] (URDF)",
                "Maximum joint velocity. URDF <limit velocity>; ignored on MJCF export.");
            PMTextBoxJointVelocity = AddJointPropertyTextbox(TextBoxJointVelocityID,
                "Velocity limit (blank = none, URDF only)");

            PMLabelJointDamping = AddJointPropertyLabel(LabelJointDampingID,
                "Damping [N*m*s/rad or N*s/m]",
                "Viscous damping coefficient. Maps to URDF <dynamics damping> and MJCF <joint damping>.");
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
                "Reference [rad or m] (MJCF)",
                "Joint position assumed by the model when MuJoCo loads it. MJCF <joint ref>; ignored on URDF export.");
            PMTextBoxJointReference = AddJointPropertyTextbox(TextBoxJointReferenceID,
                "Reference position (blank = 0, MJCF only)");
        }

        private PropertyManagerPageLabel AddJointPropertyLabel(int id, string caption, string tip)
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            return (PropertyManagerPageLabel)PMLinkJointTab.AddControl2(id,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                caption, (short)alignment, options, tip);
        }

        private PropertyManagerPageTextbox AddJointPropertyTextbox(int id, string tip)
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            return (PropertyManagerPageTextbox)PMLinkJointTab.AddControl2(id,
                (short)swPropertyManagerPageControlType_e.swControlType_Textbox,
                "", (short)alignment, options, tip);
        }
    }
}
