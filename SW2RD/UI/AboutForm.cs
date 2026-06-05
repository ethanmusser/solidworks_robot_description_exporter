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

using SW2RD.Utilities;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace SW2RD.UI
{
    // Modal "About" dialog for the add-in. Surfaces the product name, version,
    // authorship, and - importantly - the third-party asset attributions the
    // add-in is obligated to display. The robot-arm toolbar / logo icon is a
    // Flaticon free-license asset, which REQUIRES visible attribution to its
    // author; this dialog is that visible location. Built entirely in code
    // (no designer / .resx) to keep the UI surface dependency-free.
    public sealed class AboutForm : Form
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        // Flaticon attribution. Per the Flaticon free license, this text must
        // be shown verbatim with a link back to the icon page. The author is
        // the Flaticon contributor credited on the icon's page
        // (https://www.flaticon.com/free-icon/robotic-arm_1839269); the
        // "Special Lineal color" family is published by Freepik. If the icon
        // is ever swapped, update BOTH this string and the linked URL.
        private const string RobotArmIconAuthor = "Freepik";
        private const string RobotArmIconUrl =
            "https://www.flaticon.com/free-icons/robotic-arm";

        private const string ProjectUrl =
            "https://github.com/ethanmusser/solidworks_robot_description_exporter";

        public AboutForm(string iconPath = null)
        {
            Text = "About Robot Description Exporter";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 430);
            Font = SystemFonts.MessageBoxFont;

            int left = 96;
            int contentWidth = ClientSize.Width - left - 16;

            // Logo (robot arm). Defensive: skip if the file can't be loaded.
            PictureBox logo = new PictureBox
            {
                Location = new Point(16, 16),
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
            };
            TryLoadLogo(logo, iconPath);
            Controls.Add(logo);

            Label title = new Label
            {
                Text = "SolidWorks Robot Description Exporter",
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                Location = new Point(left, 18),
                Size = new Size(contentWidth, 24),
                AutoEllipsis = true,
            };
            Controls.Add(title);

            Label subtitle = new Label
            {
                Text = "SW2RD - exports SolidWorks assemblies as URDF and MJCF robot descriptions.",
                Location = new Point(left, 44),
                Size = new Size(contentWidth, 34),
            };
            Controls.Add(subtitle);

            Label version = new Label
            {
                Text = "Version " + SafeVersion(),
                Location = new Point(left, 80),
                Size = new Size(contentWidth, 20),
            };
            Controls.Add(version);

            Label copyright = new Label
            {
                Text = "(C) 2026 Ethan J. Musser",
                Location = new Point(16, 112),
                Size = new Size(ClientSize.Width - 32, 20),
            };
            Controls.Add(copyright);

            Label basedOn = new Label
            {
                Text = "Based on the SolidWorks to URDF Exporter (SW2URDF), " +
                    "(C) 2015 Stephen Brawner and contributors.",
                Location = new Point(16, 134),
                Size = new Size(ClientSize.Width - 32, 34),
            };
            Controls.Add(basedOn);

            LinkLabel project = NewLink(
                "Project home (GitHub)", ProjectUrl,
                new Point(16, 170), new Size(ClientSize.Width - 32, 20));
            Controls.Add(project);

            // --- Attributions section ---
            Label attribHeader = new Label
            {
                Text = "Credits and attributions",
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold),
                Location = new Point(16, 208),
                Size = new Size(ClientSize.Width - 32, 20),
            };
            Controls.Add(attribHeader);

            Label iconCredit = new Label
            {
                Text = "Robot arm icon (toolbar / logo):",
                Location = new Point(16, 232),
                Size = new Size(ClientSize.Width - 32, 20),
            };
            Controls.Add(iconCredit);

            LinkLabel iconLink = NewLink(
                "\"Robotic arm\" icon created by " + RobotArmIconAuthor + " - Flaticon",
                RobotArmIconUrl,
                new Point(28, 254), new Size(ClientSize.Width - 44, 20));
            Controls.Add(iconLink);

            Label licenseHeader = new Label
            {
                Text = "License",
                Font = new Font(Font.FontFamily, Font.Size, FontStyle.Bold),
                Location = new Point(16, 290),
                Size = new Size(ClientSize.Width - 32, 20),
            };
            Controls.Add(licenseHeader);

            Label license = new Label
            {
                Text = "Released under the MIT License.",
                Location = new Point(16, 312),
                Size = new Size(ClientSize.Width - 32, 50),
            };
            Controls.Add(license);

            Button ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size(88, 28),
            };
            ok.Location = new Point(ClientSize.Width - ok.Width - 16, ClientSize.Height - ok.Height - 14);
            ok.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
        }

        // Loads the robot-arm logo into the PictureBox from a fully-decoded
        // copy (so the file is not left locked) and never throws - a missing
        // or unreadable icon just leaves the box empty.
        private void TryLoadLogo(PictureBox box, string iconPath)
        {
            try
            {
                if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                {
                    return;
                }
                using (Image fromFile = Image.FromFile(iconPath))
                {
                    box.Image = new Bitmap(fromFile);
                }
            }
            catch (Exception ex)
            {
                logger.Warn("AboutForm could not load logo from '" + iconPath + "': " + ex.Message);
            }
        }

        private LinkLabel NewLink(string text, string url, Point location, Size size)
        {
            LinkLabel link = new LinkLabel
            {
                Text = text,
                Location = location,
                Size = size,
                AutoEllipsis = true,
            };
            link.Links.Add(0, text.Length, url);
            link.LinkClicked += (s, e) => OpenUrl(e.Link.LinkData as string);
            return link;
        }

        private void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Warn("AboutForm failed to open URL '" + url + "': " + ex.Message);
            }
        }

        private static string SafeVersion()
        {
            try
            {
                string commit = Versioning.Version.GetCommitVersion();
                if (!string.IsNullOrEmpty(commit))
                {
                    return commit;
                }
            }
            catch
            {
                // fall through to the assembly version below
            }
            try
            {
                return Versioning.Version.GetBuildVersion();
            }
            catch
            {
                return "(unknown)";
            }
        }
    }
}
