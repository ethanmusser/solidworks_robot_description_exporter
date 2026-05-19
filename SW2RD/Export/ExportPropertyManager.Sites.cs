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
using SW2RD.URDF;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SW2RD.Export
{
    // Per-link MJCF <site> editor: PMPage UI builder for the Sites tab,
    // plus the runtime add / remove / refresh operations. Sites are
    // MJCF-only (no URDF analog).
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        // Builds every control on the Sites tab. Layout mirrors the
        // Visual / Collision groups editor: a listbox selects the active
        // site, and the fields below live-edit that selected row.
        private void BuildSitesTab()
        {
            int controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            int alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            int options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            // Static heading so the tab's visual hierarchy reads
            // "Sites (MJCF)".
            PMSitesTab.AddControl2(LabelSitesHeaderID,
                (short)controlType, "Sites (MJCF)", (short)alignment, options,
                "MJCF-only frames attached to a body. Ignored when exporting URDF.");

            PMSitesTab.AddControl2(
                SitesHelpLabelID, (short)controlType,
                "Select a site row, then edit its name and reference coordinate system below.",
                (short)alignment, options,
                "Sites are MJCF-only frames attached to a body. They are ignored when exporting URDF.");

            PMSitesTab.AddControl2(
                SitesListLabelID, (short)controlType, "Sites defined for this link",
                (short)alignment, options,
                "Select a site to edit its name and coordinate system below.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            PMListBoxSites = (PropertyManagerPageListbox)PMSitesTab.AddControl2(
                SitesListBoxID, (short)controlType, "", (short)alignment, options,
                "Sites already added to this link. Select one to edit it.");
            PMListBoxSites.Height = 50;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            PMButtonSiteAdd = (PropertyManagerPageButton)PMSitesTab.AddControl2(
                SitesAddButtonID, (short)controlType, "New Site", 0, options,
                "Create a new site on this link and make it active.");

            PMButtonSiteRemove = (PropertyManagerPageButton)PMSitesTab.AddControl2(
                SitesRemoveButtonID, (short)controlType, "Delete Selected Site", 0, options,
                "Delete the highlighted site from this link.");

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
                "Pick the reference coordinate system that defines the active site's pose.");
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
        }

        private void AddSiteFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }

            EnsureSitesInitialized(node);
            SaveActiveSiteFields(node);

            string linkName = (PMTextBoxLinkName != null && !string.IsNullOrWhiteSpace(PMTextBoxLinkName.Text))
                ? PMTextBoxLinkName.Text.Trim()
                : node.Link.Name;
            string name = NextDefaultSiteName(node.Link.Sites, linkName);
            node.Link.Sites.Add(new SiteSpec(name, ""));
            activeSiteIndex = node.Link.Sites.Count - 1;
            RefreshSitesListbox(node);
            LoadActiveSiteIntoForm(node);
        }

        private void RemoveSelectedSiteFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureSitesInitialized(node);
            if (node.Link.Sites.Count == 0)
            {
                activeSiteIndex = -1;
                LoadActiveSiteIntoForm(node);
                return;
            }

            short selected = PMListBoxSites.CurrentSelection;
            if (selected < 0 || selected >= node.Link.Sites.Count)
            {
                selected = (short)activeSiteIndex;
            }
            if (selected < 0 || selected >= node.Link.Sites.Count)
            {
                return;
            }
            node.Link.Sites.RemoveAt(selected);
            activeSiteIndex = node.Link.Sites.Count == 0
                ? -1
                : Math.Min(selected, node.Link.Sites.Count - 1);
            RefreshSitesListbox(node);
            LoadActiveSiteIntoForm(node);
        }

        public void RefreshSitesListbox(LinkNode node)
        {
            bool prior = suppressSiteListboxSelectionChanged;
            suppressSiteListboxSelectionChanged = true;
            try
            {
                PMListBoxSites.Clear();
                if (node == null || node.Link.Sites == null)
                {
                    return;
                }
                for (int i = 0; i < node.Link.Sites.Count; i++)
                {
                    SiteSpec site = node.Link.Sites[i];
                    PMListBoxSites.AddItems(SiteDisplayName(site));
                }
                if (activeSiteIndex >= 0 && activeSiteIndex < node.Link.Sites.Count)
                {
                    PMListBoxSites.CurrentSelection = (short)activeSiteIndex;
                }
            }
            finally
            {
                suppressSiteListboxSelectionChanged = prior;
            }
        }

        private void SaveActiveSiteFields(LinkNode node)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            EnsureSitesInitialized(node);
            if (activeSiteIndex < 0 || activeSiteIndex >= node.Link.Sites.Count)
            {
                return;
            }

            SiteSpec site = node.Link.Sites[activeSiteIndex];
            if (PMTextBoxSiteName != null)
            {
                site.Name = (PMTextBoxSiteName.Text ?? "").Trim();
            }

            string picked = ReadMarkedFeatureName(SiteCoordSysSelectionMark);
            if (!string.IsNullOrEmpty(picked))
            {
                site.CoordinateSystemName = picked;
            }
        }

        private void CommitActiveSiteCoordSysSelection(LinkNode node)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            EnsureSitesInitialized(node);
            if (activeSiteIndex < 0 || activeSiteIndex >= node.Link.Sites.Count)
            {
                return;
            }

            string picked = ReadMarkedFeatureName(SiteCoordSysSelectionMark);
            if (!string.IsNullOrEmpty(picked))
            {
                node.Link.Sites[activeSiteIndex].CoordinateSystemName = picked;
            }
        }

        private void LoadActiveSiteIntoForm(LinkNode node)
        {
            bool prior = suppressSiteEditorEvents;
            suppressSiteEditorEvents = true;
            try
            {
                if (node == null || node.Link == null)
                {
                    return;
                }
                EnsureSitesInitialized(node);
                if (activeSiteIndex < 0 || activeSiteIndex >= node.Link.Sites.Count)
                {
                    PMTextBoxSiteName.Text = "";
                    ClearSiteCoordSysSelection();
                    return;
                }

                SiteSpec site = node.Link.Sites[activeSiteIndex];
                PMTextBoxSiteName.Text = site.Name ?? "";
                if (currentActiveTabId == SitesTabID)
                {
                    LoadActiveSiteCoordSysIntoSelectionBox(node);
                }
                else
                {
                    ClearSiteCoordSysSelection();
                }
            }
            finally
            {
                suppressSiteEditorEvents = prior;
            }
        }

        private void LoadActiveSiteCoordSysIntoSelectionBox(LinkNode node)
        {
            bool prior = suppressGroupListboxRefresh;
            suppressGroupListboxRefresh = true;
            try
            {
                ClearSiteCoordSysSelection();
                if (node == null || node.Link == null)
                {
                    return;
                }
                EnsureSitesInitialized(node);
                if (activeSiteIndex < 0 || activeSiteIndex >= node.Link.Sites.Count)
                {
                    return;
                }

                string name = node.Link.Sites[activeSiteIndex].CoordinateSystemName;
                if (!IsRealFeatureName(name))
                {
                    return;
                }
                SelectFeatureIntoMark(name, "COORDSYS", SiteCoordSysSelectionMark);
            }
            finally
            {
                suppressGroupListboxRefresh = prior;
            }
        }

        private void ClearSiteCoordSysSelection()
        {
            try
            {
                CommonSwOperations.DeselectAllAtMark(ActiveSWModel, SiteCoordSysSelectionMark);
            }
            catch (Exception ex)
            {
                logger.Warn("DeselectAllAtMark for site coord-sys failed: " + ex.Message);
            }
        }

        private static void EnsureSitesInitialized(LinkNode node)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            if (node.Link.Sites == null)
            {
                node.Link.Sites = new List<SiteSpec>();
            }
        }

        private static string NextDefaultSiteName(List<SiteSpec> sites, string linkName)
        {
            string baseName = string.IsNullOrWhiteSpace(linkName)
                ? "site"
                : linkName + "_site";
            HashSet<string> existing = new HashSet<string>();
            if (sites != null)
            {
                foreach (SiteSpec site in sites)
                {
                    if (!string.IsNullOrEmpty(site.Name))
                    {
                        existing.Add(site.Name);
                    }
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

        private static string SiteDisplayName(SiteSpec site)
        {
            if (site == null || string.IsNullOrWhiteSpace(site.Name))
            {
                return "(unnamed)";
            }
            return site.Name;
        }
    }
}
