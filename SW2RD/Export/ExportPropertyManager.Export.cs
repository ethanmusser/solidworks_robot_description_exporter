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
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SW2RD.Export
{
    // Export-time PMPage workflow: validate -> close PMPage -> resolve
    // lightweight components -> build Robot from Tree -> save package on disk.
    // Split out of ExportPropertyManager.cs as part of the Phase 1
    // partial-class refactor; no behavior changes.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        private void ExportButtonPress()
        {
            // SaveActiveNode commits the active link's config-editing controls
            // (link name, joint props, component groups) back into the tree.
            // Those controls only exist in the Configure PMP; in Export mode
            // the tree was loaded read-only from the saved attribute and there
            // is nothing to commit (and SaveActiveNode would touch null
            // Configure-only controls). Skip it in Export mode.
            if (mode == ExportPmMode.Configure)
            {
                SaveActiveNode();
            }

            // Compute toggles are no longer surfaced in the PMPage; the
            // ExportHelper defaults (all true) drive every export. The
            // SetCompute* API on ExportHelper is retained for tests that
            // exercise the gate logic via TestExportHelper.

            // Pre-close validation. Each step updates the in-page status panel
            // so the user sees progress / failure WHILE the PMPage is still
            // visible; failures that already surface via MessageBox (unique
            // names, incomplete nodes) also stamp a short summary on the
            // panel so the panel and the dialog don't disagree.
            UpdateValidationStatus("Status: Checking link and joint names...");
            if (!CheckIfNamesAreUnique((LinkNode)Tree.Nodes[0]))
            {
                UpdateValidationStatus("Status: Duplicate link or joint names. Fix the conflicts above before exporting.");
                return;
            }

            UpdateValidationStatus("Status: Checking required link fields...");
            if (!CheckNodesComplete(Tree))
            {
                UpdateValidationStatus("Status: One or more links are incomplete. See dialog for details.");
                return;
            }

            // Output format drives the site-name check below (sites collide with
            // link names only in URDF) and is threaded into FinishExport. Read
            // pre-close (the LinkNode tree, not the Robot tree, which is only
            // built after PMPage.Close).
            ExportFormat outputFormat =
                (PMComboBoxOutputFormat != null && PMComboBoxOutputFormat.CurrentSelection == 1)
                    ? ExportFormat.MJCF
                    : ExportFormat.URDF;

            // Site names must be unique among themselves (both formats), and for
            // URDF must not collide with any link name (each site is exported as
            // an empty <link>, so a name clash would emit a duplicate link ->
            // invalid URDF). Runs pre-close so the user can fix names or flip the
            // output format while the panel context is still visible.
            UpdateValidationStatus("Status: Checking site names...");
            if (!CheckSiteNamesAreValid((LinkNode)Tree.Nodes[0], outputFormat))
            {
                UpdateValidationStatus("Status: Site name conflict. Fix the conflicts above before exporting.");
                return;
            }

            UpdateValidationStatus("Status: Validation passed. Building robot...");

            // Snapshot every PMPage control we still need WHILE THE PAGE IS OPEN.
            // After PMPage.Close(true) the controls return their initialization
            // value, not the user's runtime toggle - the output-format combobox
            // above reads correctly only because it is read pre-close. Mesh
            // format / export-meshes / fast-mesh-export were previously read
            // inside FinishExport (post-close), so any value the user changed
            // away from its default (the experimental "Fast mesh export" box is
            // the first such control) silently reverted to the default. Capture
            // here and thread the values through to FinishExport.
            MeshExportFormat meshFormat = MeshExportFormat.STL;
            if (PMComboBoxMeshFormat != null && PMComboBoxMeshFormat.CurrentSelection == 1)
            {
                meshFormat = MeshExportFormat.THREEDXML;
            }
            bool exportMeshes = PMCheckExportMeshes == null || PMCheckExportMeshes.Checked;
            bool fastMeshExport = PMCheckFastMeshExport != null && PMCheckFastMeshExport.Checked;
            bool keepResolved = PMCheckKeepResolved != null && PMCheckKeepResolved.Checked;
            int meshQuality = PMComboBoxMeshQuality != null
                ? ExportPreferences.ClampMeshQuality(PMComboBoxMeshQuality.CurrentSelection)
                : ExportPreferences.GetMeshQuality();
            int rotationFormat = PMComboBoxRotationFormat != null
                ? ExportPreferences.ClampRotationFormat(PMComboBoxRotationFormat.CurrentSelection)
                : ExportPreferences.GetRotationFormat();
            int angleUnit = PMComboBoxAngleUnit != null
                ? ExportPreferences.ClampAngleUnit(PMComboBoxAngleUnit.CurrentSelection)
                : ExportPreferences.GetAngleUnit();

            //It saves automatically when sending Okay as true;
            PMPage.Close(true);
            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;

            LinkNode baseNodeForResolve = (LinkNode)Tree.Nodes[0];

            // Resolve ONLY the components the export will actually read, rather
            // than the entire assembly. The legacy call here was
            // assy.ResolveAllLightWeightComponents(true), which loads and
            // rebuilds every lightweight component at every subassembly depth -
            // its cost scales with the TOTAL component count even when the
            // export references only a handful of parts, so it dominated export
            // time on large/deep assemblies (and popped the "N lightweight
            // components must load" prompt). The used set is the union of every
            // link's visual / collision / inertial components plus the owners of
            // any sub-component coordinate-system / axis references; everything
            // outside that set stays lightweight. Components we resolve are
            // recorded so the finally below can revert them.
            List<Component2> resolvedByUs = new List<Component2>();

            // PDM-friendliness: force any sub-component the resolve loads to open
            // READ-ONLY for the duration of the export. SOLIDWORKS PDM makes files
            // that are not checked out read-only on disk, and users typically check
            // out only the top-level assembly they are exporting. Resolving a
            // writable / out-of-date sub-component can rebuild it and flag it
            // "modified", which then blocks check-in or prompts to save files the
            // user never intended to touch. swExtRefOpenReadOnly ("open referenced
            // documents read-only") makes SW load the referenced part docs
            // read-only, so it never tries to write them and never flags them
            // modified. Combined with the targeted resolve (we touch only the used
            // components, not the whole tree) and the revert-to-lightweight below
            // (which unloads the resolved models and discards any in-memory rebuild),
            // the user's not-checked-out files are left untouched on disk. Saved and
            // restored like the STL preferences so a cancelled export does not change
            // the user's setting.
            bool priorExtRefReadOnly =
                swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swExtRefOpenReadOnly);
            try
            {
                swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swExtRefOpenReadOnly, true);

                ResolveUsedComponents(baseNodeForResolve, assy, resolvedByUs);

                // Safety guard: confirm every component the pipeline will read
                // actually has a ModelDoc. If the targeted resolve missed
                // something (e.g. a coordinate system anchored in an unexpected
                // component), escalate to a full assembly resolve as a last
                // resort so we never regress correctness relative to the legacy
                // behavior.
                List<UnresolvedComponentEntry> unresolvedComponents = new List<UnresolvedComponentEntry>();
                CheckModelDocsExist(baseNodeForResolve, unresolvedComponents);
                if (unresolvedComponents.Count > 0)
                {
                    logger.Warn("Targeted resolve left " + unresolvedComponents.Count +
                        " used component(s) unresolved; falling back to a full assembly resolve.");
                    int result = assy.ResolveAllLightWeightComponents(true);
                    if (result == (int)swComponentResolveStatus_e.swResolveAbortedByUser)
                    {
                        logger.Warn("Components were not resolved by user");
                        MessageBox.Show("In order to export, this tool needs the components used " +
                            "by your links to be resolved. You can resolve them manually or try " +
                            "exporting again");
                        return;
                    }
                    if (result == (int)swComponentResolveStatus_e.swResolveError ||
                        result == (int)swComponentResolveStatus_e.swResolveNotPerformed)
                    {
                        logger.Warn("Resolving components failed. Warning user to do so on their own");
                        MessageBox.Show("Resolving components failed. In order to export, this tool " +
                            "needs the components used by your links to be resolved. Try resolving " +
                            "lightweight components manually before attempting to export again");
                        return;
                    }

                    unresolvedComponents.Clear();
                    CheckModelDocsExist(baseNodeForResolve, unresolvedComponents);
                    if (unresolvedComponents.Count > 0)
                    {
                        string detail = FormatUnresolvedComponents(unresolvedComponents);
                        logger.Error("SolidWorks told us the resolve succeeded, but ModelDocs" +
                            " could not be obtained for:" + detail);
                        MessageBox.Show(
                            "Model documents could not be obtained for the components listed " +
                            "below, so the export cannot continue. Each one is usually suppressed, " +
                            "or its part file was renamed/moved/deleted (in PDM, not gotten-latest " +
                            "or not checked out).\r\n\r\n" +
                            "To fix each one, open the Robot Description Exporter, select the link " +
                            "shown, expand the listed section in the accordion, and either re-select " +
                            "the correct component or remove it from that group:\r\n" + detail +
                            "\r\nTip: if a component shows as suppressed or \"not found\" in the " +
                            "SolidWorks FeatureManager, resolve it there first, then re-export.",
                            "Unresolved components");
                        return;
                    }
                }

                // Builds the links and joints from the PMPage configuration
                LinkNode BaseNode = baseNodeForResolve;
                automaticallySwitched = true;
                Tree.Nodes.Remove(BaseNode);

                bool exportSuccess = Exporter.CreateRobotFromTreeView(BaseNode);
                if (exportSuccess)
                {
                    FinishExport(outputFormat, meshFormat, exportMeshes, fastMeshExport, meshQuality, rotationFormat, angleUnit, keepResolved);
                }
            }
            finally
            {
                // Clear the SwProgress registration of the export progress bar.
                // Bracketed here (not at each export Start/End pair) so a mid-
                // export exception can never leave SwProgress pointing at a
                // dead/ended bar and break later PMP busy indicators.
                SwProgress.DetachExternal();

                // Restore the components we resolved back to lightweight so the
                // user's session returns to its prior low-memory state. Only the
                // components we flipped are touched; anything the user had already
                // resolved is left alone. If the full-resolve fallback ran, the
                // extra components it resolved are intentionally left resolved,
                // matching the legacy behavior. When the user opts to keep
                // components resolved, both the targeted used set and the
                // fast-mesh tessellation leaves stay resolved so a later export
                // in the same session skips the resolve cost entirely.
                if (!keepResolved)
                {
                    RevertComponentsToLightweight(resolvedByUs);
                    RevertComponentsToLightweight(Exporter.TessellationResolvedComponents);
                }
                else
                {
                    logger.Info("Keeping " + resolvedByUs.Count + " targeted and " +
                        Exporter.TessellationResolvedComponents.Count +
                        " tessellation-resolved component(s) resolved per user setting.");
                }

                // Restore the user's "open referenced documents read-only" setting.
                swApp.SetUserPreferenceToggle(
                    (int)swUserPreferenceToggle_e.swExtRefOpenReadOnly, priorExtRefReadOnly);
            }
        }

        // Resolves ONLY the components the export will read: the union of every
        // link's visual / collision / inertial components, plus the owners of any
        // sub-component coordinate-system / axis references. Components that were
        // lightweight before we touched them are appended to `resolved` so the
        // caller can revert them after export. Anything outside the used set is
        // deliberately left in its current (lightweight) state - that is what
        // makes a sparse export of a large assembly fast.
        private void ResolveUsedComponents(LinkNode baseNode, AssemblyDoc assy, List<Component2> resolved)
        {
            Dictionary<string, Component2> used = new Dictionary<string, Component2>(StringComparer.Ordinal);
            GatherUsedComponents(baseNode, used);
            AddFeatureOwnerComponents(baseNode, assy, used);

            foreach (Component2 comp in used.Values)
            {
                if (comp == null)
                {
                    continue;
                }
                int state = comp.GetSuppression2();
                if (state == (int)swComponentSuppressionState_e.swComponentLightweight ||
                    state == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                {
                    logger.Info("Resolving lightweight component " + comp.Name2);
                    comp.SetSuppression2((int)swComponentSuppressionState_e.swComponentFullyResolved);
                    resolved.Add(comp);
                }
            }
            logger.Info("Targeted resolve touched " + resolved.Count + " of " + used.Count +
                " used component(s); the rest of the assembly stays lightweight.");
        }

        // Recursively collects the distinct visual / collision / inertial
        // Component2 set used by the link tree, keyed by Component2.Name2 (unique
        // within an assembly) so duplicates across groups / links are folded.
        private static void GatherUsedComponents(LinkNode node, Dictionary<string, Component2> used)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            AddComponentsToSet(node.Link.VisualComponents, used);
            AddComponentsToSet(node.Link.CollisionComponents, used);
            AddComponentsToSet(node.Link.InertialComponents, used);
            foreach (LinkNode child in node.Nodes)
            {
                GatherUsedComponents(child, used);
            }
        }

        private static void AddComponentsToSet(List<Component2> components, Dictionary<string, Component2> used)
        {
            if (components == null)
            {
                return;
            }
            foreach (Component2 comp in components)
            {
                if (comp == null)
                {
                    continue;
                }
                string name = comp.Name2;
                if (!string.IsNullOrEmpty(name) && !used.ContainsKey(name))
                {
                    used[name] = comp;
                }
            }
        }

        // Joint coordinate systems / axes can live inside a sub-component, in
        // which case their name carries a "<Component-Name>" suffix. The owning
        // part must be resolved for GetCoordinateSystemTransformByName /
        // EstimateAxis to read it without triggering a stray resolve. We only pay
        // the O(all-components) GetComponents walk when at least one such suffixed
        // reference exists and is not already in the used set.
        private static void AddFeatureOwnerComponents(
            LinkNode baseNode, AssemblyDoc assy, Dictionary<string, Component2> used)
        {
            HashSet<string> ownerNames = new HashSet<string>(StringComparer.Ordinal);
            CollectFeatureOwnerNames(baseNode, ownerNames);
            ownerNames.ExceptWith(used.Keys);
            if (ownerNames.Count == 0)
            {
                return;
            }

            object[] components = assy.GetComponents(false);
            if (components == null)
            {
                return;
            }
            foreach (Component2 comp in components)
            {
                if (comp == null)
                {
                    continue;
                }
                string name = comp.Name2;
                if (name != null && ownerNames.Contains(name) && !used.ContainsKey(name))
                {
                    used[name] = comp;
                }
            }
        }

        private static void CollectFeatureOwnerNames(LinkNode node, HashSet<string> ownerNames)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            Joint joint = node.Link.Joint;
            if (joint != null)
            {
                AddComponentSuffixName(joint.CoordinateSystemName, ownerNames);
                AddComponentSuffixName(joint.AxisName, ownerNames);
            }
            // Sites can reference a coordinate system OR a reference point that
            // lives inside a sub-component; record those owners too so the
            // targeted lightweight resolve loads them (a coord-sys / point read
            // inside an unresolved component yields no transform).
            if (node.Link.Sites != null)
            {
                foreach (SiteSpec site in node.Link.Sites)
                {
                    if (site == null)
                    {
                        continue;
                    }
                    AddComponentSuffixName(site.CoordinateSystemName, ownerNames);
                    AddComponentSuffixName(site.ReferencePointName, ownerNames);
                }
            }
            foreach (LinkNode child in node.Nodes)
            {
                CollectFeatureOwnerNames(child, ownerNames);
            }
        }

        // Parses the "<Component-Name>" suffix out of a feature reference like
        // "Coordinate System1 <LINK-5>" and records the bare component name.
        // No-op for assembly-level features (no suffix).
        private static void AddComponentSuffixName(string nameWithSuffix, HashSet<string> ownerNames)
        {
            if (string.IsNullOrEmpty(nameWithSuffix))
            {
                return;
            }
            int indexFirst = nameWithSuffix.IndexOf('<');
            if (indexFirst < 0)
            {
                return;
            }
            int indexLast = nameWithSuffix.IndexOf('>', indexFirst);
            if (indexLast <= indexFirst)
            {
                return;
            }
            string componentName = nameWithSuffix.Substring(indexFirst + 1, indexLast - indexFirst - 1);
            if (!string.IsNullOrEmpty(componentName))
            {
                ownerNames.Add(componentName);
            }
        }

        // Restores the given components to lightweight. Called from the export
        // finally; failures are logged and swallowed so a single stubborn
        // component cannot mask the export result.
        private void RevertComponentsToLightweight(List<Component2> components)
        {
            if (components == null || components.Count == 0)
            {
                return;
            }
            foreach (Component2 comp in components)
            {
                if (comp == null)
                {
                    continue;
                }
                try
                {
                    comp.SetSuppression2((int)swComponentSuppressionState_e.swComponentLightweight);
                }
                catch (Exception e)
                {
                    logger.Warn("Failed to revert component to lightweight after export", e);
                }
            }
            logger.Info("Reverted " + components.Count + " component(s) to lightweight after export.");
        }

        // Validates Exporter.URDFRobot, prompts the user for an output path,
        // and writes the description + meshes via Exporter.ExportRobot. The
        // PMPage is already closed by the time this runs, so user-visible
        // failures use MessageBox.Show; the pre-close in-page status panel
        // is unreachable from here.
        private void FinishExport(ExportFormat outputFormat, MeshExportFormat meshFormat,
            bool exportMeshes, bool fastMeshExport, int meshQuality, int rotationFormat, int angleUnit,
            bool keepResolved)
        {
            logger.Info("Completing export");

            // Belt-and-suspenders against export-format constraints that
            // CheckNodesComplete may not catch until after SolidWorks-derived
            // joint limits have been computed.
            string errors = CheckExportFieldErrors(Exporter.URDFRobot.BaseLink, outputFormat);
            if (!string.IsNullOrWhiteSpace(errors))
            {
                logger.Info("Export validation errors encountered:\n " + errors);
                MessageBox.Show(
                    "Some joints are missing fields required for the selected export format:\r\n\r\n" +
                    errors +
                    "\r\nReopen the Robot Description Exporter and fix these joints before exporting.",
                    "Required fields missing");
                return;
            }

            string suggestedName = GetSuggestedExportFileName(Exporter.PackageName, outputFormat);

            using (SaveFileDialog dialog = new SaveFileDialog
            {
                RestoreDirectory = true,
                InitialDirectory = Exporter.SavePath,
                FileName = suggestedName,
            })
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                Exporter.SavePath = Path.GetDirectoryName(dialog.FileName);
                Exporter.PackageName = Path.GetFileName(dialog.FileName);

                // meshFormat / exportMeshes / fastMeshExport / meshQuality were
                // captured in ExportButtonPress BEFORE PMPage.Close(true); reading
                // the controls here (post-close) would return their init values,
                // not the user's runtime choices.

                // Persist the per-user defaults so the next export PMPage
                // pre-populates with these same choices.
                ExportPreferences.SetLastOutputFormat((int)outputFormat);
                ExportPreferences.SetLastMeshFormat((int)meshFormat);
                ExportPreferences.SetLastExportMeshes(exportMeshes);
                ExportPreferences.SetFastMeshExport(fastMeshExport);
                ExportPreferences.SetKeepResolvedAfterExport(keepResolved);
                ExportPreferences.SetMeshQuality(meshQuality);
                ExportPreferences.SetRotationFormat(rotationFormat);
                ExportPreferences.SetAngleUnit(angleUnit);

                Exporter.UseTessellationMeshExport = fastMeshExport;
                Exporter.MeshQualityLevel = meshQuality;
                Exporter.MJCFRotationFormat = (MJCF.MJCFRotationFormat)ExportPreferences.ClampRotationFormat(rotationFormat);
                Exporter.MJCFAngleUnit = (MJCF.MJCFAngleUnit)ExportPreferences.ClampAngleUnit(angleUnit);

                logger.Info("Saving " + outputFormat + " package to " + dialog.FileName +
                    " (fast mesh export=" + fastMeshExport + ", mesh quality=" + meshQuality +
                    ", rotation format=" + (MJCF.MJCFRotationFormat)ExportPreferences.ClampRotationFormat(rotationFormat) +
                    ", angle unit=" + (MJCF.MJCFAngleUnit)ExportPreferences.ClampAngleUnit(angleUnit) + ")");
                bool exportOk = Exporter.ExportRobot(exportMeshes, meshFormat, outputFormat);
                if (exportOk)
                {
                    NotifyExportComplete(dialog.FileName);
                }
            }
        }

        // Non-modal, auto-dismissing SolidWorks bubble tooltip announcing a
        // completed export. Lives only on the PMPage interactive path (NOT in
        // ExportHelper.ExportRobotCore), so the SW-attached unit tests that call
        // ExportRobot directly never trigger it - that is the structural reason
        // this can't reintroduce the headless-test breakage the old blocking
        // popup caused. A tooltip failure must never fail the export, so we
        // swallow + log. Safe to call after PMPage.Close: this is an ISldWorks
        // call, not a PMPage control, so the "update controls before Close"
        // invariant does not apply.
        private void NotifyExportComplete(string outputPath)
        {
            try
            {
                System.Drawing.Rectangle area = Screen.PrimaryScreen.WorkingArea;
                int x = area.Right - 40;
                int y = area.Top + 120;
                swApp.ShowBubbleTooltipAt2(
                    x, y,
                    (int)swArrowPosition.swArrowRightTop,
                    "Export complete",
                    outputPath,
                    (int)swBitMaps.swBitMapNone, "",
                    "", 0, 0, "", "");
            }
            catch (Exception ex)
            {
                logger.Warn("Export-complete bubble tooltip failed: " + ex.Message);
            }
        }

        internal static string GetSuggestedExportFileName(string packageName, ExportFormat outputFormat)
        {
            // The output format no longer changes the default file name; keep it
            // in the signature so tests cover both export paths that call here.
            _ = outputFormat;
            return packageName ?? string.Empty;
        }

        // Updates the in-page status panel. PMPage controls reject writes
        // after their parent page has closed, so callers must invoke this
        // ONLY before PMPage.Close(true). We guard against a null label
        // (control wasn't created) but not against a closed page; the latter
        // is enforced by call ordering, not by COM.
        private void UpdateValidationStatus(string message)
        {
            if (PMLabelValidationStatus != null)
            {
                try
                {
                    PMLabelValidationStatus.Caption = message;
                }
                catch (Exception ex)
                {
                    // Defensive: SolidWorks throws InvalidComObjectException
                    // if a caller forgets the pre-close ordering invariant.
                    // Log and keep moving rather than crash the export.
                    logger.Warn("UpdateValidationStatus failed: " + ex.Message);
                }
            }
        }

        // Walks the built Robot tree and returns a detailed, newline-delimited
        // list of post-compute validation failures. Empty string means the
        // selected format can represent the configured joints.
        internal static string CheckExportFieldErrors(Link baseLink, ExportFormat outputFormat)
        {
            StringBuilder builder = new StringBuilder();
            CheckExportFieldErrors(baseLink, outputFormat, builder);
            return builder.ToString();
        }

        private static void CheckExportFieldErrors(Link link, ExportFormat outputFormat, StringBuilder builder)
        {
            if (link == null)
            {
                return;
            }

            foreach (Link child in link.Children)
            {
                CheckJointExportFieldErrors(child, outputFormat, builder);
                CheckExportFieldErrors(child, outputFormat, builder);
            }
        }

        private static void CheckJointExportFieldErrors(Link child, ExportFormat outputFormat, StringBuilder builder)
        {
            Joint joint = child?.Joint;
            if (joint == null)
            {
                return;
            }

            string jointType = joint.Type ?? "";
            if (!IsSupportedUiJointType(jointType) && jointType != "continuous")
            {
                builder.Append("    ")
                    .Append(DescribeJoint(child, joint))
                    .Append(": Joint type '")
                    .Append(string.IsNullOrWhiteSpace(jointType) ? "(empty)" : jointType)
                    .Append("' is not supported. Choose fixed, revolute, or prismatic.\r\n");
                return;
            }

            if (Joint.HasPartialRangeLimit(joint.Limit))
            {
                builder.Append("    ")
                    .Append(DescribeJoint(child, joint))
                    .Append(": Lower and Upper limits must either both be set or both be blank.\r\n");
                return;
            }

            bool missingRange = !Joint.HasCompleteRangeLimit(joint.Limit);
            if (outputFormat == ExportFormat.URDF && jointType == "prismatic" && missingRange)
            {
                builder.Append("    ")
                    .Append(DescribeJoint(child, joint))
                    .Append(": Lower and Upper limits are missing. URDF prismatic joints require a limited range.\r\n")
                    .Append("        ")
                    .Append(GetMissingLimitGuidance(joint))
                    .Append("\r\n");
            }
        }

        private static bool IsSupportedUiJointType(string jointType)
        {
            return jointType == "fixed" || jointType == "revolute" || jointType == "prismatic";
        }

        private static string DescribeJoint(Link child, Joint joint)
        {
            string linkName = string.IsNullOrWhiteSpace(child?.Name) ? "(unnamed link)" : child.Name;
            string jointName = string.IsNullOrWhiteSpace(joint?.Name) ? "(unnamed joint)" : joint.Name;
            string jointType = string.IsNullOrWhiteSpace(joint?.Type) ? "(empty)" : joint.Type;
            return linkName + " / " + jointName + " (" + jointType + ")";
        }

        private static string GetMissingLimitGuidance(Joint joint)
        {
            if (joint != null && joint.AutoComputeLimits)
            {
                return "Auto-compute Lower/Upper was enabled, but no compatible SolidWorks limit mate was found. " +
                    "Add a distance limit mate for this prismatic joint, or uncheck Auto-compute and enter Lower/Upper manually.";
            }
            return "Enter Lower and Upper manually in the Joint Properties section.";
        }
    }
}
