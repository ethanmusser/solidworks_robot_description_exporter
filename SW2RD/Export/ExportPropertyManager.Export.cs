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
            SaveActiveNode();

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

            // Sites-with-URDF warning runs pre-close so the user can flip the
            // output format without losing the panel context. Walks the
            // LinkNode tree directly instead of the Robot tree (which is only
            // built after PMPage.Close).
            ExportFormat outputFormat =
                (PMComboBoxOutputFormat != null && PMComboBoxOutputFormat.CurrentSelection == 1)
                    ? ExportFormat.MJCF
                    : ExportFormat.URDF;
            if (outputFormat == ExportFormat.URDF && AnyNodeHasSites((LinkNode)Tree.Nodes[0]))
            {
                UpdateValidationStatus("Status: URDF selected but sites are configured. Confirm in dialog.");
                DialogResult siteWarn = MessageBox.Show(
                    "Some links have MJCF <site> tags configured but you've selected URDF " +
                    "output. URDF does not support sites; they will be omitted from the " +
                    "exported file. Continue?",
                    "Sites will be dropped",
                    MessageBoxButtons.YesNo);
                if (siteWarn == DialogResult.No)
                {
                    UpdateValidationStatus("Status: Export cancelled (sites would be dropped).");
                    return;
                }
            }

            UpdateValidationStatus("Status: Validation passed. Building robot...");

            //It saves automatically when sending Okay as true;
            PMPage.Close(true);
            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;

            //This call can be a real sink of processing time if the model is large.
            //Unfortunately there isn't a way around it I believe.
            int result = assy.ResolveAllLightWeightComponents(true);

            // If the user confirms to resolve the components and they are successfully
            // resolved we can continue
            if (result == (int)swComponentResolveStatus_e.swResolveOk)
            {
                List<string> unresolvedComponents = new List<string>();
                CheckModelDocsExist((LinkNode)Tree.Nodes[0], unresolvedComponents);
                if (unresolvedComponents.Count > 0)
                {
                    string componentNames = string.Join("\r\n", unresolvedComponents);
                    logger.Error("SolidWorks told us the resolve succeeded, but ModelDocs" +
                        " could not be obtained for: " + componentNames);
                    MessageBox.Show("Model Documents could not be obtained for the following" +
                        " components. Please resolve them:\r\n" + componentNames);
                    return;
                }

                // Builds the links and joints from the PMPage configuration
                LinkNode BaseNode = (LinkNode)Tree.Nodes[0];
                automaticallySwitched = true;
                Tree.Nodes.Remove(BaseNode);

                bool exportSuccess = Exporter.CreateRobotFromTreeView(BaseNode);
                if (exportSuccess)
                {
                    FinishExport(outputFormat);
                }
            }
            else if (result == (int)swComponentResolveStatus_e.swResolveError ||
                result == (int)swComponentResolveStatus_e.swResolveNotPerformed)
            {
                logger.Warn("Resolving components failed. Warning user to do so on their own");
                MessageBox.Show("Resolving components failed. In order to export to URDF, " +
                    " this tool needs all components to be resolved. Try resolving " +
                    "lightweight components manually before attempting to export again");
            }
            else if (result == (int)swComponentResolveStatus_e.swResolveAbortedByUser)
            {
                logger.Warn("Components were not resolved by user");
                MessageBox.Show("In order to export to URDF, this tool needs all " +
                    "components to be resolved. You can resolve them manually or try " +
                    "exporting again");
            }
        }

        // Validates Exporter.URDFRobot, prompts the user for an output path,
        // and writes the description + meshes via Exporter.ExportRobot. The
        // PMPage is already closed by the time this runs, so user-visible
        // failures use MessageBox.Show; the pre-close in-page status panel
        // is unreachable from here.
        private void FinishExport(ExportFormat outputFormat)
        {
            logger.Info("Completing export");

            // Belt-and-suspenders against schema-level required fields that
            // CheckNodesComplete may not catch. The pre-close validators
            // already run on the LinkNode tree; this runs on the resolved
            // URDFRobot.BaseLink and reports anything left.
            string errors = CheckLinksForRequiredFieldErrors(Exporter.URDFRobot.BaseLink);
            if (!string.IsNullOrWhiteSpace(errors))
            {
                logger.Info("Link errors encountered:\n " + errors);
                MessageBox.Show(
                    "Some links are missing required fields and cannot be exported:\r\n\r\n" +
                    errors +
                    "\r\nReopen the Robot Description Exporter and fix these links before exporting.",
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

                MeshExportFormat meshFormat = MeshExportFormat.STL;
                if (PMComboBoxMeshFormat != null && PMComboBoxMeshFormat.CurrentSelection == 1)
                {
                    meshFormat = MeshExportFormat.THREEDXML;
                }

                bool exportMeshes = PMCheckExportMeshes == null || PMCheckExportMeshes.Checked;

                // Persist the per-user defaults so the next export PMPage
                // pre-populates with these same choices. The PMPage has
                // already closed by this point so we cannot read the
                // controls after this; capturing it before ExportRobot
                // also avoids losing the persistence on a mid-export
                // exception.
                ExportPreferences.SetLastOutputFormat((int)outputFormat);
                ExportPreferences.SetLastMeshFormat((int)meshFormat);
                ExportPreferences.SetLastExportMeshes(exportMeshes);

                logger.Info("Saving " + outputFormat + " package to " + dialog.FileName);
                Exporter.ExportRobot(exportMeshes, meshFormat, outputFormat);
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

        // Walks the link tree and returns a newline-delimited list of links
        // whose required URDF / MJCF fields are not satisfied (e.g. missing
        // joint name on a non-base link). Empty string means everything checks
        // out.
        private static string CheckLinksForRequiredFieldErrors(Link baseLink)
        {
            StringBuilder builder = new StringBuilder();
            CheckLinkForRequiredFieldErrors(baseLink, builder);
            return builder.ToString();
        }

        private static void CheckLinkForRequiredFieldErrors(Link link, StringBuilder builder)
        {
            if (!link.AreRequiredFieldsSatisfied())
            {
                builder.Append(link.Name).Append("\r\n");
            }
            foreach (Link child in link.Children)
            {
                CheckLinkForRequiredFieldErrors(child, builder);
            }
        }

        // Pre-close walk over the WinForms LinkNode tree. The Robot tree
        // doesn't exist yet at this point in ExportButtonPress, so we walk
        // the SelectedNode hierarchy directly.
        private static bool AnyNodeHasSites(LinkNode node)
        {
            if (node == null)
            {
                return false;
            }
            if (node.Link != null && node.Link.Sites != null && node.Link.Sites.Count > 0)
            {
                return true;
            }
            foreach (System.Windows.Forms.TreeNode child in node.Nodes)
            {
                if (AnyNodeHasSites(child as LinkNode))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
