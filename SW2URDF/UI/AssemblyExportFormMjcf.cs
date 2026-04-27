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

using SW2URDF.MJCF;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    // MJCF-specific UI additions to the AssemblyExportForm. The URDF flow's controls live in the
    // Designer file; the bits added here are created at runtime to avoid touching the sensitive
    // Designer-managed layout when we don't need to. The only Designer-side change is the
    // re-layout of the existing URDF buttons and link tree to make room for the Sites group.
    public partial class AssemblyExportForm
    {
        private GroupBox groupBoxSites;
        private CheckedListBox checkedListBoxSites;
        private Button buttonExportMjcf;

        private void InitializeMjcfUi()
        {
            // Shrink the link tree to free vertical space for the Sites selector. Width stays the
            // same so the URDF experience doesn't feel cramped.
            Size treeSize = treeViewLinkProperties.Size;
            Point treeLocation = treeViewLinkProperties.Location;
            int sitesTop = treeLocation.Y + 300 + 10;
            treeViewLinkProperties.Size = new Size(treeSize.Width, 300);

            groupBoxSites = new GroupBox
            {
                Text = "Sites (MJCF)",
                Location = new Point(treeLocation.X, sitesTop),
                Size = new Size(treeSize.Width, 215),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom,
            };

            Label instructions = new Label
            {
                Text = "Check reference coord systems to expose as MJCF <site> elements " +
                       "on this link. Unchecked systems are ignored.",
                AutoSize = false,
                Location = new Point(6, 16),
                Size = new Size(treeSize.Width - 12, 32),
            };

            checkedListBoxSites = new CheckedListBox
            {
                Location = new Point(6, 52),
                Size = new Size(treeSize.Width - 12, 150),
                CheckOnClick = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left
                         | AnchorStyles.Right | AnchorStyles.Bottom,
            };

            groupBoxSites.Controls.Add(instructions);
            groupBoxSites.Controls.Add(checkedListBoxSites);
            panelLinkProperties.Controls.Add(groupBoxSites);

            // Slot the MJCF export button between the existing Previous and "Export URDF Only"
            // buttons so it doesn't wrap or overlap.
            buttonExportMjcf = new Button
            {
                Text = "Export MJCF...",
                Location = new Point(604, 603),
                Size = new Size(150, 21),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                UseVisualStyleBackColor = true,
            };
            buttonExportMjcf.Click += ButtonExportMjcfClick;
            panelLinkProperties.Controls.Add(buttonExportMjcf);
        }

        /// <summary>
        /// Refreshes the Sites check list to match the given link's options and saved selections.
        /// Called from FillLinkPropertyBoxes (see AssemblyExportFormExtension).
        /// </summary>
        public void FillSitesForLink(Link link)
        {
            if (checkedListBoxSites == null)
            {
                return;
            }

            checkedListBoxSites.BeginUpdate();
            try
            {
                checkedListBoxSites.Items.Clear();
                if (link == null)
                {
                    return;
                }

                System.Collections.Generic.List<string> available =
                    Exporter.GetRefCoordinateSystems();
                string jointCoordSys = link.Joint?.CoordinateSystemName;

                System.Collections.Generic.HashSet<string> savedSet =
                    new System.Collections.Generic.HashSet<string>(
                        link.SiteCoordSystemNames ?? new System.Collections.Generic.List<string>());

                foreach (string name in available)
                {
                    // Hide the coord system already acting as the link's joint frame: promoting it
                    // to a site would duplicate the body origin.
                    if (!string.IsNullOrWhiteSpace(jointCoordSys) && name == jointCoordSys)
                    {
                        continue;
                    }
                    checkedListBoxSites.Items.Add(name, savedSet.Contains(name));
                }
            }
            finally
            {
                checkedListBoxSites.EndUpdate();
            }
        }

        /// <summary>
        /// Persists the checked state back into the link. Called from
        /// SaveLinkDataFromPropertyBoxes.
        /// </summary>
        public void SaveSitesForLink(Link link)
        {
            if (checkedListBoxSites == null || link == null)
            {
                return;
            }

            link.SiteCoordSystemNames = new System.Collections.Generic.List<string>();
            foreach (object item in checkedListBoxSites.CheckedItems)
            {
                string name = item?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    link.SiteCoordSystemNames.Add(name);
                }
            }
        }

        private void ButtonExportMjcfClick(object sender, EventArgs e)
        {
            FinishMjcfExport();
        }

        private void FinishMjcfExport()
        {
            logger.Info("Completing MJCF export");
            SaveConfigTree(ActiveSWModel, BaseNode, false);

            LinkNode node = (LinkNode)treeViewLinkProperties.SelectedNode;
            if (node != null)
            {
                SaveLinkDataFromPropertyBoxes(node.Link);
            }

            Exporter.URDFRobot = CreateRobotFromTreeView(treeViewLinkProperties);

            string errors = CheckLinksForErrors(Exporter.URDFRobot.BaseLink);
            if (!string.IsNullOrWhiteSpace(errors))
            {
                logger.Info("Link errors encountered:\n " + errors);
                MessageBox.Show(
                    "The following links contained errors in either their link or joint " +
                    "properties. Please address before continuing\r\n\r\n" + errors,
                    "MJCF Errors");
                return;
            }

            string warnings = CheckLinksForWarnings(Exporter.URDFRobot.BaseLink);
            if (!string.IsNullOrWhiteSpace(warnings))
            {
                logger.Info("Link warnings encountered:\r\n" + warnings);
                DialogResult warningResult = MessageBox.Show(
                    "The following links contained issues that may cause problems. " +
                    "Do you wish to proceed?\r\n\r\n" + warnings,
                    "MJCF Warnings",
                    MessageBoxButtons.YesNo);
                if (warningResult == DialogResult.No)
                {
                    logger.Info("MJCF export canceled for user to review warnings");
                    return;
                }
            }

            MjcfOptions options;
            using (MjcfOptionsDialog dialog = new MjcfOptionsDialog(new MjcfOptions()))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    logger.Info("MJCF export canceled at the options dialog.");
                    return;
                }
                options = dialog.Options;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                RestoreDirectory = true,
                InitialDirectory = Exporter.SavePath,
                FileName = Exporter.PackageName,
            };

            try
            {
                bool saveResult = DialogResult.OK == saveFileDialog.ShowDialog();
                if (!saveResult)
                {
                    return;
                }

                Exporter.SavePath = Path.GetDirectoryName(saveFileDialog.FileName);
                Exporter.PackageName = Path.GetFileName(saveFileDialog.FileName);

                logger.Info("Saving MJCF package to " + saveFileDialog.FileName);
                Exporter.ExportMjcf(options);
                Close();
            }
            finally
            {
                saveFileDialog.Dispose();
            }
        }
    }
}
