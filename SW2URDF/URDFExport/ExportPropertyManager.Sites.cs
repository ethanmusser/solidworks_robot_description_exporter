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
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    // Per-link MJCF <site> editor: PMPage UI builder for the Sites tab,
    // plus the runtime add / remove / refresh operations. Sites are
    // MJCF-only (no URDF analog).
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        // Builds every control on the Sites tab. Layout mirrors the
        // Visual / Collision groups editor:
        //   header -> help -> sites listbox + label -> name input ->
        //   coord-system selectionbox + read-only echo combobox ->
        //   Add / Remove buttons.
        private void BuildSitesTab()
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            // Static heading on the tab so the visual hierarchy still
            // reads "Sites (MJCF)" after the per-tab single-group wrapper
            // was retired.
            PMSitesTab.AddControl2(LabelSitesHeaderID,
                (short)controlType, "Sites (MJCF)", (short)alignment, options,
                "MJCF-only frames attached to a body. Ignored when exporting URDF.");

            PMSitesTab.AddControl2(
                SitesHelpLabelID, (short)controlType,
                "Type a site name, pick a reference coord. system, then click Add Site.",
                (short)alignment, options,
                "Sites are MJCF-only frames attached to a body. They are ignored when exporting URDF.");

            PMSitesTab.AddControl2(
                SitesListLabelID, (short)controlType, "Sites defined for this link",
                (short)alignment, options,
                "Read-only summary. Use Remove Selected Site to delete one.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            PMListBoxSites = (PropertyManagerPageListbox)PMSitesTab.AddControl2(
                SitesListBoxID, (short)controlType, "", (short)alignment, options,
                "Sites already added to this link. Select one and click Remove Selected Site to delete it.");
            PMListBoxSites.Height = 50;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            PMSitesTab.AddControl2(
                SitesNameLabelID, (short)controlType, "Site name", (short)alignment, options,
                "Identifier that will appear as <site name=...> in the MJCF file.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMTextBoxSiteName = (PropertyManagerPageTextbox)PMSitesTab.AddControl2(
                SitesNameTextBoxID, (short)controlType, "", (short)alignment, options,
                "Site name (will appear as <site name=...>)");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            PMSitesTab.AddControl2(LabelSiteCoordSysHeaderID,
                (short)controlType, "Site coordinate system", (short)alignment, options,
                "Reference coordinate system that defines the site's pose relative to the parent body. Picked from the SW tree.");

            object coordSysFilterObj = new swSelectType_e[] { swSelectType_e.swSelCOORDSYS };
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionSiteCoordSys = (PropertyManagerPageSelectionbox)PMSitesTab.AddControl2(
                SelectionSiteCoordSysID, (short)controlType, "Pick site coord. system",
                (short)alignment, options,
                "Pick the reference coordinate system that defines the site's pose. The pick is consumed when you click Add Site.");
            // SingleEntityOnly = true matches SW's own coord-system /
            // mate creation single-entity pickers - a new pick OVERWRITES
            // the prior pick in place. See PMSelectionGlobalCoordsys in
            // ExportPropertyManager.LinkJointTab.cs for the full
            // rationale, including why AllowSelectInMultipleBoxes is
            // FALSE (semantic exclusivity: a site coord-sys cannot
            // simultaneously be the global origin or a joint origin in
            // the data model, so the picker must move the entity out
            // of any sibling box on a fresh pick).
            PMSelectionSiteCoordSys.AllowSelectInMultipleBoxes = false;
            PMSelectionSiteCoordSys.SingleEntityOnly = true;
            PMSelectionSiteCoordSys.AllowMultipleSelectOfSameEntity = false;
            PMSelectionSiteCoordSys.Height = 18;
            PMSelectionSiteCoordSys.SetSelectionFilters(coordSysFilterObj);
            PMSelectionSiteCoordSys.Mark = SiteCoordSysSelectionMark;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            PMButtonSiteAdd = (PropertyManagerPageButton)PMSitesTab.AddControl2(
                SitesAddButtonID, (short)controlType, "Add Site", 0, options,
                "Add the entered site to this link");

            PMButtonSiteRemove = (PropertyManagerPageButton)PMSitesTab.AddControl2(
                SitesRemoveButtonID, (short)controlType, "Remove Selected Site", 0, options,
                "Remove the selected site from the list");
        }

        // Reads the marked SelectionMgr entry for the site coord-sys
        // SelectionBox at click time. The SelectionBox-only design has
        // no echo combobox to fall back on, so a "no pick" is detected
        // by an empty mark.
        private void AddSiteFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            string name = (PMTextBoxSiteName.Text ?? "").Trim();
            string coord = ReadMarkedFeatureName(SiteCoordSysSelectionMark);
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a site name before adding the site.");
                return;
            }
            if (string.IsNullOrEmpty(coord))
            {
                MessageBox.Show("Please pick a reference coordinate system in the site SelectionBox before adding the site.");
                return;
            }
            if (node.Link.Sites == null)
            {
                node.Link.Sites = new List<SiteSpec>();
            }
            node.Link.Sites.Add(new SiteSpec(name, coord));
            PMTextBoxSiteName.Text = "";
            // Clear the site SelectionBox only so the user has a clean
            // slate for the next site. ClearSelection2(true) here would
            // wipe every sibling SelectionBox (visual / collision /
            // inertial component boxes, the joint coord-sys / axis /
            // global-coord-sys feature pickers).
            try
            {
                CommonSwOperations.DeselectAllAtMark(
                    ActiveSWModel, SiteCoordSysSelectionMark);
            }
            catch (Exception ex)
            {
                logger.Warn("DeselectAllAtMark after AddSiteFromForm failed: " + ex.Message);
            }
            RefreshSitesListbox(node);
        }

        private void RemoveSelectedSiteFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null || node.Link.Sites == null || node.Link.Sites.Count == 0)
            {
                return;
            }
            short selected = PMListBoxSites.CurrentSelection;
            if (selected < 0 || selected >= node.Link.Sites.Count)
            {
                return;
            }
            node.Link.Sites.RemoveAt(selected);
            RefreshSitesListbox(node);
        }

        public void RefreshSitesListbox(LinkNode node)
        {
            PMListBoxSites.Clear();
            if (node == null || node.Link.Sites == null)
            {
                return;
            }
            foreach (SiteSpec site in node.Link.Sites)
            {
                PMListBoxSites.AddItems(site.Name + " : " + site.CoordinateSystemName);
            }
        }
    }
}
