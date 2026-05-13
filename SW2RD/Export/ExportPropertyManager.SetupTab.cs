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
    // PMPage UI builder for the always-visible header above the tab strip
    // and the per-export Setup tab.
    //
    // The header (Preview/Export button, validation-status label, link tree,
    // and child-count spinner) is added directly to PMPage so it stays
    // visible regardless of which tab is active. The Setup tab itself only
    // hosts per-export choices (output/mesh format, export-meshes toggle).
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        // Builds the always-visible controls that get attached directly to
        // PMPage (i.e. live OUTSIDE the per-tab area). Called BEFORE
        // PMPage.AddTab(...) but SolidWorks renders these BELOW the tab
        // strip in practice (the tab strip is the page's primary
        // navigation; PMPage-level controls flow underneath the
        // currently-active tab's content). We embrace that placement and
        // order the controls to read as an intentional "footer":
        //
        //   Active link tree label
        //   <tree view>
        //   Children of selected link label + spinner
        //   --- separator label ---
        //   Status: Ready (validation panel)
        //   [Export] button
        //
        // This way the user's eye flows naturally from "what link am I
        // editing" -> "grow the tree" -> "validation state" -> "ship it".
        private void BuildPmPageHeader()
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            // "Active link tree" header above the tree so the user can
            // tell what the WindowFromHandle control beneath it is for.
            // The label is also a visual breakpoint between the per-tab
            // content above and the always-visible navigation below.
            PMPage.AddControl2(LabelActiveLinkTreeID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Active link tree", (short)alignment, options,
                "Click a link to edit it. Right-click for add / remove. Drag to reparent.");

            // Link tree host control. The actual TreeView (event handlers,
            // root node, focus) is wired up at the end of
            // SetupPropertyManagerPage so the first TreeAfterSelect ->
            // FillPropertyManager call sees fully-constructed PM controls.
            PMTree = PMPage.AddControl2(dotNetTree,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,
                "Link Tree", 0, options, "");
            PMTree.Height = 163;

            // Child-count spinner sits next to the tree so adding children
            // is part of building the tree, not a per-link side trip on
            // the Link/Joint tab.
            PMPage.AddControl2(LabelChildCountID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Children of selected link", (short)alignment, options,
                "How many child links the selected link should have. Increase to grow the tree.");

            PMNumberBoxChildCount = PMPage.AddControl2(
                NumBoxChildCountID,
                (short)swPropertyManagerPageControlType_e.swControlType_Numberbox,
                "", (short)alignment, options,
                "Enter the number of child links and they will be automatically added");
            PMNumberBoxChildCount.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessInteger, 0, int.MaxValue, true, 1, 1, 1);
            PMNumberBoxChildCount.Value = 0;

            // Validation / export status panel. Updated in-place by
            // ExportButtonPress while the page is still open; surfaces
            // missing required fields, name conflicts, etc. so the user
            // sees the same message in both the dialog and the panel.
            PMLabelValidationStatus = (PropertyManagerPageLabel)PMPage.AddControl2(
                ValidationStatusLabelID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Status: Ready",
                (short)alignment, options,
                "Validation and export status. Missing required fields are reported here.");

            // Export button. Validates the configured tree before
            // closing the page; prints diagnostics into PMLabelValidationStatus
            // if any pre-close check fails. Sits at the very bottom of
            // the page so it lines up with the green-check / red-X
            // convention SolidWorks users expect from PMPages.
            PMButtonExport = (PropertyManagerPageButton)PMPage.AddControl2(ButtonExportID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Export", 0, options,
                "Validate and export the configured robot description");
        }

        // Builds the per-export choices on the Setup tab: output format,
        // mesh format, and the "export meshes" toggle. Tree, button, and
        // status panel live in BuildPmPageHeader. The four legacy
        // "Compute X" checkboxes were removed when AssemblyExportForm went
        // away because the PMPage offers no UI to perform the manual
        // overrides those toggles were guarding.
        private void BuildSetupTab()
        {
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            PMSetupTab.AddControl2(LabelOutputFormatID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Output Format", (short)alignment, options,
                "Description format to write on export");

            PMComboBoxOutputFormat = (PropertyManagerPageCombobox)PMSetupTab.AddControl2(
                OutputFormatComboID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)alignment, options,
                "URDF: ROS-style package. MJCF: MuJoCo XML model.");
            PMComboBoxOutputFormat.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxOutputFormat.AddItems(new string[] { "URDF", "MJCF" });
            PMComboBoxOutputFormat.CurrentSelection =
                (short)ExportPreferences.ClampOutputFormat(ExportPreferences.GetLastOutputFormat());

            PMSetupTab.AddControl2(LabelMeshFormatID,
                (short)swPropertyManagerPageControlType_e.swControlType_Label,
                "Mesh Format", (short)alignment, options,
                "Mesh file type to emit alongside the description");

            PMComboBoxMeshFormat = (PropertyManagerPageCombobox)PMSetupTab.AddControl2(
                MeshFormatComboID,
                (short)swPropertyManagerPageControlType_e.swControlType_Combobox,
                "", (short)alignment, options,
                "STL: monochrome, MuJoCo / Gazebo friendly. 3DXML: preserves color.");
            PMComboBoxMeshFormat.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxMeshFormat.AddItems(new string[] { "STL", "3DXML" });
            PMComboBoxMeshFormat.CurrentSelection =
                (short)ExportPreferences.ClampMeshFormat(ExportPreferences.GetLastMeshFormat());

            PMCheckExportMeshes = (PropertyManagerPageCheckbox)PMSetupTab.AddControl2(
                ExportMeshesCheckID,
                (short)swPropertyManagerPageControlType_e.swControlType_Checkbox,
                "Export Meshes", (short)alignment, options,
                "Regenerate mesh files alongside the description. Uncheck to " +
                "rewrite only the description XML using existing meshes.");
            PMCheckExportMeshes.Checked = ExportPreferences.GetLastExportMeshes();
        }
    }
}
