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
    // PMPage UI builder for the page-1 Tree group (link tree + child-count
    // spinner + saved-config label) and the page-2 Export group (output /
    // mesh format choices, validation status, Export button).
    //
    // The Tree group lives on page 1 of the wizard so the user always sees
    // the kinematic tree while configuring links; the Export group lives on
    // page 2 (the next-arrow step) and holds the per-export choices and the
    // Export button.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        // Builds the page-1 Tree group: the link tree, the child-count
        // spinner, and the saved-configuration status label. The actual
        // TreeView (event handlers, root node, focus) is wired up later in
        // SetupPropertyManagerPage / WireUpLinkTree so the first
        // TreeAfterSelect -> FillPropertyManager call sees fully-constructed
        // PM controls.
        //
        //   Active link tree label
        //   <tree view>
        //   Children of selected link label + spinner
        //   Saved configuration status label
        private void BuildTreeGroup()
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            // "Active link tree" header above the tree so the user can
            // tell what the WindowFromHandle control beneath it is for.
            PMTreeGroup.AddControl2(LabelActiveLinkTreeID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Active link tree", (short)alignment, options,
                "Click a link to edit it. Right-click for add / remove. Drag to reparent.");

            // Link tree host control. The actual TreeView (event handlers,
            // root node, focus) is wired up at the end of
            // SetupPropertyManagerPage so the first TreeAfterSelect ->
            // FillPropertyManager call sees fully-constructed PM controls.
            PMTree = PMTreeGroup.AddControl2(dotNetTree,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "Link Tree", 0, options, "");
            // Fixed height; the tree no longer grows with the node count at
            // runtime (SW does not reflow the controls below it). Larger trees
            // scroll via the TreeView's native vertical scrollbar.
            PMTree.Height = LinkTreeBoxHeight;

            // Child-count spinner sits next to the tree so adding children
            // is part of building the tree, not a per-link side trip on
            // the Link/Joint section.
            PMTreeGroup.AddControl2(LabelChildCountID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Children of selected link", (short)alignment, options,
                "How many child links the selected link should have. Increase to grow the tree.");

            PMNumberBoxChildCount = PMTreeGroup.AddControl2(
                NumBoxChildCountID,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "", (short)alignment, options,
                "Enter the number of child links and they will be automatically added");
            PMNumberBoxChildCount.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessInteger, 0, int.MaxValue, true, 1, 1, 1);
            PMNumberBoxChildCount.Value = 0;

            // Saved-configuration status label. Reflects whether this model
            // already carries a saved SW2RD config (the Clear action lives
            // on the ribbon now, not on the page).
            PMLabelConfigurationCache = (PropertyManagerPageLabel)PMTreeGroup.AddControl2(
                LabelConfigurationCacheID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "", (short)alignment, options,
                "Shows whether this model has saved SW2RD configuration.");

            UpdateSetupConfigurationActions();
        }

        // Builds the page-2 Export group: output format, mesh format, the
        // "export meshes" / "fast mesh export" toggles, mesh quality, MJCF
        // rotation / angle options, the validation-status label, and the
        // Export button. The four legacy "Compute X" checkboxes were removed
        // when AssemblyExportForm went away because the PMPage offers no UI
        // to perform the manual overrides those toggles were guarding.
        private void BuildExportGroup()
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            PMExportGroup.AddControl2(LabelOutputFormatID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Output Format", (short)alignment, options,
                "Description format to write on export");

            PMComboBoxOutputFormat = (PropertyManagerPageCombobox)PMExportGroup.AddControl2(
                OutputFormatComboID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)alignment, options,
                "URDF: ROS-style package. MJCF: MuJoCo XML model.");
            PMComboBoxOutputFormat.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxOutputFormat.AddItems(new string[] { "URDF", "MJCF" });
            PMComboBoxOutputFormat.CurrentSelection =
                (short)ExportPreferences.ClampOutputFormat(ExportPreferences.GetLastOutputFormat());

            PMExportGroup.AddControl2(LabelMeshFormatID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Mesh Format", (short)alignment, options,
                "Mesh file type to emit alongside the description");

            PMComboBoxMeshFormat = (PropertyManagerPageCombobox)PMExportGroup.AddControl2(
                MeshFormatComboID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)alignment, options,
                "STL: monochrome, MuJoCo / Gazebo friendly. 3DXML: preserves color.");
            PMComboBoxMeshFormat.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxMeshFormat.AddItems(new string[] { "STL", "3DXML" });
            PMComboBoxMeshFormat.CurrentSelection =
                (short)ExportPreferences.ClampMeshFormat(ExportPreferences.GetLastMeshFormat());

            PMCheckExportMeshes = (PropertyManagerPageCheckbox)PMExportGroup.AddControl2(
                ExportMeshesCheckID,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Export Meshes", (short)alignment, options,
                "Regenerate mesh files alongside the description. Uncheck to " +
                "rewrite only the description XML using existing meshes.");
            PMCheckExportMeshes.Checked = ExportPreferences.GetLastExportMeshes();

            PMCheckFastMeshExport = (PropertyManagerPageCheckbox)PMExportGroup.AddControl2(
                FastMeshExportCheckID,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Fast mesh export", (short)alignment, options,
                "Export meshes by tessellating each part directly at the chosen " +
                "mesh quality, skipping the slow hide/show of the whole assembly. " +
                "Much faster on large assemblies. Uncheck to use the legacy " +
                "whole-assembly STL export.");
            PMCheckFastMeshExport.Checked = ExportPreferences.GetFastMeshExport();

            PMComboBoxMeshQuality = (PropertyManagerPageCombobox)PMExportGroup.AddControl2(
                MeshQualityComboID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "Mesh quality", (short)alignment, options,
                "Quality of the fast (tessellation) mesh export. The chord tolerance " +
                "is set per part relative to that part's own size, so every part - " +
                "and every part inside a sub-assembly - gets uniform, display-" +
                "independent detail. Finer = smoother curves and larger files.");
            PMComboBoxMeshQuality.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            // Item order MUST match the MeshQualityLevel mapping in ExportHelper
            // and ExportPreferences (0 = Very coarse .. 4 = Very fine, 5 = Custom).
            PMComboBoxMeshQuality.AddItems(new string[]
            {
                "Very coarse", "Coarse", "Medium", "Fine", "Very fine", "Custom",
            });
            PMComboBoxMeshQuality.CurrentSelection =
                (short)ExportPreferences.ClampMeshQuality(ExportPreferences.GetMeshQuality());

            // Manual override fields for the "Custom" quality level. Shown always
            // but enabled only when Custom is selected (see UpdateMeshQualityEnabled).
            // Values are unitless so no document-unit conversion is involved:
            // chord as a percent of each part's bbox diagonal, angle in degrees,
            // max chord clamp in millimeters.
            PMNumberBoxCustomChordFraction = (PropertyManagerPageNumberbox)PMExportGroup.AddControl2(
                CustomChordFractionNumberID,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Custom chord (% of part)", (short)alignment, options,
                "Custom mesh quality only. Per-part chord tolerance as a percentage of " +
                "that part's bounding-box diagonal. Larger = coarser / fewer faces.");
            PMNumberBoxCustomChordFraction.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble, 0.01, 50.0, true, 0.1, 0.1, 0.01);
            PMNumberBoxCustomChordFraction.Value =
                ExportPreferences.GetCustomChordFraction() * 100.0;

            PMNumberBoxCustomAngle = (PropertyManagerPageNumberbox)PMExportGroup.AddControl2(
                CustomAngleNumberID,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Custom angle (deg)", (short)alignment, options,
                "Custom mesh quality only. Surface-plane angle tolerance in degrees. " +
                "Larger = coarser curves / fewer faces.");
            PMNumberBoxCustomAngle.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble, 1.0, 60.0, true, 1.0, 1.0, 0.5);
            PMNumberBoxCustomAngle.Value = ExportPreferences.GetCustomAngleDeg();

            PMNumberBoxCustomMaxChord = (PropertyManagerPageNumberbox)PMExportGroup.AddControl2(
                CustomMaxChordNumberID,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "Custom max chord (mm)", (short)alignment, options,
                "Custom mesh quality only. Upper clamp on the per-part chord tolerance " +
                "in millimeters, so very large parts can coarsen further. Raise to allow " +
                "coarser meshes on big bodies.");
            PMNumberBoxCustomMaxChord.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessDouble, 0.01, 1000.0, true, 1.0, 1.0, 0.1);
            PMNumberBoxCustomMaxChord.Value = ExportPreferences.GetCustomMaxChordMm();

            PMCheckKeepResolved = (PropertyManagerPageCheckbox)PMExportGroup.AddControl2(
                KeepResolvedCheckID,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Keep components resolved after export", (short)alignment, options,
                "Leave components that were resolved for this export resolved when it " +
                "finishes, instead of reverting them to lightweight. Speeds up repeated " +
                "exports in the same session (only the first pays the resolve cost) at the " +
                "cost of higher memory use. Uncheck to return the assembly to its prior " +
                "lightweight state after each export.");
            PMCheckKeepResolved.Checked = ExportPreferences.GetKeepResolvedAfterExport();

            // Fast mesh export only produces STL; grey it out unless STL is the
            // selected mesh format (CurrentSelection 0 = STL, 1 = 3DXML). Kept in
            // sync at runtime by OnComboboxSelectionChanged(MeshFormatComboID).
            SetFastMeshExportEnabled(PMComboBoxMeshFormat.CurrentSelection == 0);
            // Mesh quality only affects the fast STL path, so it starts greyed
            // unless STL + fast export are both active. Kept in sync at runtime
            // by the format combo and fast-export checkbox handlers.
            UpdateMeshQualityEnabled();

            PMExportGroup.AddControl2(LabelRotationFormatID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Rotation Format (MJCF)", (short)alignment, options,
                "How frame orientations are written in MJCF output. All three are " +
                "equivalent; pick the most readable for you. URDF ignores this.");

            PMComboBoxRotationFormat = (PropertyManagerPageCombobox)PMExportGroup.AddControl2(
                RotationFormatComboID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)alignment, options,
                "Axis-angle: rotation axis + angle (deg). Quaternion: w x y z. " +
                "Euler: roll-pitch-yaw (deg), same convention as URDF.");
            PMComboBoxRotationFormat.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            // Item order MUST match the MJCFRotationFormat enum / ExportPreferences
            // value (0 = Axis-angle, 1 = Quaternion, 2 = Euler).
            PMComboBoxRotationFormat.AddItems(new string[] { "Axis-angle", "Quaternion", "Euler" });
            PMComboBoxRotationFormat.CurrentSelection =
                (short)ExportPreferences.ClampRotationFormat(ExportPreferences.GetRotationFormat());

            // MJCF-only option; grey it out for URDF so the user sees clearly that
            // it does not apply. Output format CurrentSelection: 0 = URDF, 1 = MJCF.
            // Kept in sync at runtime by OnComboboxSelectionChanged(OutputFormatComboID).
            SetRotationFormatEnabled(PMComboBoxOutputFormat.CurrentSelection == 1);

            PMExportGroup.AddControl2(LabelAngleUnitID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Angle Units (MJCF)", (short)alignment, options,
                "Units for angular values (axis-angle / Euler angles and hinge joint " +
                "ranges) in MJCF output. URDF always uses radians and ignores this.");

            PMComboBoxAngleUnit = (PropertyManagerPageCombobox)PMExportGroup.AddControl2(
                AngleUnitComboID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)alignment, options,
                "Degrees: MuJoCo's default (no compiler angle attribute written). " +
                "Radians: writes <compiler angle=\"radian\"> and emits angles in radians.");
            PMComboBoxAngleUnit.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            // Item order MUST match the MJCFAngleUnit enum / ExportPreferences
            // value (0 = Degrees, 1 = Radians).
            PMComboBoxAngleUnit.AddItems(new string[] { "Degrees", "Radians" });
            PMComboBoxAngleUnit.CurrentSelection =
                (short)ExportPreferences.ClampAngleUnit(ExportPreferences.GetAngleUnit());

            // MJCF-only option, same gating as the rotation format dropdown.
            SetAngleUnitEnabled(PMComboBoxOutputFormat.CurrentSelection == 1);

            // Validation / export status panel. Updated in-place by
            // ExportButtonPress while the page is still open; surfaces
            // missing required fields, name conflicts, etc. so the user
            // sees the same message in both the dialog and the panel.
            PMLabelValidationStatus = (PropertyManagerPageLabel)PMExportGroup.AddControl2(
                ValidationStatusLabelID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Status: Ready",
                (short)alignment, options,
                "Validation and export status. Missing required fields are reported here.");

            // Export button. Validates the configured tree before closing
            // the page; prints diagnostics into PMLabelValidationStatus if
            // any pre-close check fails.
            PMButtonExport = (PropertyManagerPageButton)PMExportGroup.AddControl2(ButtonExportID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Export", 0, options,
                "Validate and export the configured robot description");
        }

        private void UpdateSetupConfigurationActions()
        {
            bool hasSaved = ConfigurationSerialization.HasSavedConfiguration(ActiveSWModel);

            if (PMLabelConfigurationCache != null)
            {
                string savedText = hasSaved ? "SW2RD saved config found" : "No SW2RD saved config";
                PMLabelConfigurationCache.Caption = "Saved Configuration: " + savedText + ".";
            }
        }

        private static void SetControlEnabled(object control, bool enabled)
        {
            IPropertyManagerPageControl pageControl = control as IPropertyManagerPageControl;
            if (pageControl != null)
            {
                pageControl.Enabled = enabled;
            }
        }

        // Greys out the "Fast mesh export" checkbox when the selected mesh format
        // can't use the tessellation path (only STL can). Called at build time
        // and from OnComboboxSelectionChanged when the format dropdown changes.
        private void SetFastMeshExportEnabled(bool enabled)
        {
            SetControlEnabled(PMCheckFastMeshExport, enabled);
        }

        // Greys out the "Mesh quality" dropdown unless it actually affects the
        // export: quality only applies to the tessellation (fast) STL path, so
        // it is enabled only when the format is STL AND fast export is checked.
        // Called at build time and whenever either of those two controls change.
        private void UpdateMeshQualityEnabled()
        {
            bool stl = PMComboBoxMeshFormat != null && PMComboBoxMeshFormat.CurrentSelection == 0;
            bool fast = PMCheckFastMeshExport != null && PMCheckFastMeshExport.Checked;
            bool qualityActive = stl && fast;
            SetControlEnabled(PMComboBoxMeshQuality, qualityActive);

            // The manual override fields apply only to the "Custom" level (index
            // 5), and only when the quality dropdown itself is active.
            bool custom = qualityActive && PMComboBoxMeshQuality != null &&
                PMComboBoxMeshQuality.CurrentSelection == 5;
            SetControlEnabled(PMNumberBoxCustomChordFraction, custom);
            SetControlEnabled(PMNumberBoxCustomAngle, custom);
            SetControlEnabled(PMNumberBoxCustomMaxChord, custom);
        }

        // Greys out the MJCF "Rotation Format" dropdown when the selected output
        // format is URDF (which has no equivalent option and ignores it). Called
        // at build time and from OnComboboxSelectionChanged when the output
        // format dropdown changes.
        private void SetRotationFormatEnabled(bool enabled)
        {
            SetControlEnabled(PMComboBoxRotationFormat, enabled);
        }

        // Greys out the MJCF "Angle Units" dropdown when the selected output
        // format is URDF (which always uses radians and ignores it). Called at
        // build time and from OnComboboxSelectionChanged when the output format
        // dropdown changes.
        private void SetAngleUnitEnabled(bool enabled)
        {
            SetControlEnabled(PMComboBoxAngleUnit, enabled);
        }
    }
}
