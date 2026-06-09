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
using SW2RD.Input;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SW2RD.Export
{
    // Per-link site editor: PMPage UI builder for the Sites tab, plus the
    // runtime add / remove / refresh operations. Sites are exported to both
    // formats - as an MJCF <site> child of the body, and in URDF as an empty
    // <link> connected to the parent link by a fixed <joint>.
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

            // No "Sites" heading label here - the accordion group caption
            // already reads "Sites", so repeating it in the body is redundant.
            // The help label below carries the usage hint. LabelSitesHeaderID
            // remains reserved (unused) in ExportPropertyManager.cs.
            PMSitesGroup.AddControl2(
                SitesHelpLabelID, (short)controlType,
                "Named reference frames attached to a body (MJCF <site>; URDF empty link + fixed joint). Select a site row, then edit its name and reference coordinate system below.",
                (short)alignment, options,
                "Named reference frames attached to a body. Exported as an MJCF <site> and as a URDF empty link joined by a fixed joint.");

            PMSitesGroup.AddControl2(
                SitesListLabelID, (short)controlType, "Sites defined for this link",
                (short)alignment, options,
                "Select a site to edit its name and coordinate system below.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            PMListBoxSites = (PropertyManagerPageListbox)PMSitesGroup.AddControl2(
                SitesListBoxID, (short)controlType, "", (short)alignment, options,
                "Sites already added to this link. Select one to edit it.");
            // Sized for readability when a link has several sites, rather than
            // a cramped two-row box. (The link-tree box height lives separately
            // in LinkTreeBoxHeight and is no longer the same value.)
            PMListBoxSites.Height = 163;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            PMSitesGroup.AddControl2(
                SitesNameLabelID, (short)controlType, "Site name", (short)alignment, options,
                "Identifier for the frame: the MJCF <site name=...> and the URDF empty <link name=...>.");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMTextBoxSiteName = (PropertyManagerPageTextbox)PMSitesGroup.AddControl2(
                SitesNameTextBoxID, (short)controlType, "", (short)alignment, options,
                "Site name (MJCF <site name=...> / URDF empty <link name=...>)");

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            PMSitesGroup.AddControl2(LabelSiteCoordSysHeaderID,
                (short)controlType, "Site coordinate system or point", (short)alignment, options,
                "Reference that defines the site's location relative to the parent body. Pick a coordinate system for a full pose, or a reference point for position only (identity rotation).");

            // The box accepts BOTH a coordinate system (full 6-DOF pose) and a
            // reference point (position only). The commit path detects which kind
            // was picked via Feature.GetTypeName2 ("CoordSys" vs "RefPoint") and
            // routes it to the matching SiteSpec field; the SelectionBox is the
            // single source of truth either way.
            object siteFilterObj = new swSelectType_e[]
            {
                swSelectType_e.swSelCOORDSYS,
                swSelectType_e.swSelDATUMPOINTS,
            };
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            PMSelectionSiteCoordSys = (PropertyManagerPageSelectionbox)PMSitesGroup.AddControl2(
                SelectionSiteCoordSysID, (short)controlType, "Pick site coord. system or point",
                (short)alignment, options,
                "Pick the reference coordinate system (full pose) OR reference point (position only) that defines the active site's location.");
            // SingleEntityOnly = true matches SW's own coord-system /
            // mate creation single-entity pickers - a new pick OVERWRITES
            // the prior pick in place. See PMSelectionJointCoordsys in
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
            PMSelectionSiteCoordSys.SetSelectionFilters(siteFilterObj);
            PMSelectionSiteCoordSys.Mark = SiteCoordSysSelectionMark;

            // Add / Remove buttons live AFTER the name + coord-system editors
            // so the user reads the section top-to-bottom (pick a site row ->
            // edit its name -> pick its coord system -> add another / delete).
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            PMButtonSiteAdd = (PropertyManagerPageButton)PMSitesGroup.AddControl2(
                SitesAddButtonID, (short)controlType, "New Site", 0, options,
                "Create a new site on this link and make it active.");

            PMButtonSiteRemove = (PropertyManagerPageButton)PMSitesGroup.AddControl2(
                SitesRemoveButtonID, (short)controlType, "Delete Selected Site", 0, options,
                "Delete the highlighted site from this link.");
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

            ApplySitePickToSpec(site);
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

            ApplySitePickToSpec(node.Link.Sites[activeSiteIndex]);
        }

        // Routes whatever the user picked in the dual-filter site SelectionBox to
        // the correct SiteSpec field. A reference point ("RefPoint") sets
        // Source = ReferencePoint and stores the point name; anything else
        // (coordinate systems) sets Source = CoordinateSystem. The opposite-kind
        // name is cleared so the persisted spec carries only the field its Source
        // actually uses. Empty marks (synthetic clears from rehydration /
        // teardown) leave the spec untouched.
        private void ApplySitePickToSpec(SiteSpec site)
        {
            if (site == null)
            {
                return;
            }
            string picked = ReadMarkedFeatureNameAndKind(SiteCoordSysSelectionMark, out string typeName);
            if (string.IsNullOrEmpty(picked))
            {
                return;
            }
            if (string.Equals(typeName, "RefPoint", StringComparison.Ordinal))
            {
                site.Source = SiteSourceType.ReferencePoint;
                site.ReferencePointName = picked;
                site.CoordinateSystemName = "";
            }
            else
            {
                site.Source = SiteSourceType.CoordinateSystem;
                site.CoordinateSystemName = picked;
                site.ReferencePointName = "";
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
                if (currentActiveSectionId == SitesGroupID)
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

                SiteSpec site = node.Link.Sites[activeSiteIndex];
                // Rehydrate the box with the reference the active site actually
                // uses, picking the SelectByID2 entity type that matches its
                // Source (coord systems and reference points are bound through
                // different selection types).
                string name;
                string selectByIdType;
                if (site.Source == SiteSourceType.ReferencePoint)
                {
                    name = site.ReferencePointName;
                    selectByIdType = "DATUMPOINT";
                }
                else
                {
                    name = site.CoordinateSystemName;
                    selectByIdType = "COORDSYS";
                }
                if (!IsRealFeatureName(name))
                {
                    return;
                }
                SelectFeatureIntoMark(name, selectByIdType, SiteCoordSysSelectionMark);
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
            // Annotate the source kind so a coord-sys site and a point site are
            // distinguishable in the listbox without reopening each one.
            string suffix = site.Source == SiteSourceType.ReferencePoint ? " (point)" : " (coord. sys.)";
            return site.Name + suffix;
        }
    }
}
