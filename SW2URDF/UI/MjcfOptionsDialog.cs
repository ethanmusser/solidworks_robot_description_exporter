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
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SW2URDF.UI
{
    // Modal dialog surfacing the MJCF-specific export options the URDF flow doesn't need. All
    // controls are created programmatically so a single file is enough and no .resx is required;
    // the project can continue to ship without a dedicated designer surface for this small form.
    public class MjcfOptionsDialog : Form
    {
        public MjcfOptions Options { get; private set; }

        private TextBox textBoxTimestep;
        private ComboBox comboBoxIntegrator;
        private TextBox textBoxGravityX;
        private TextBox textBoxGravityY;
        private TextBox textBoxGravityZ;
        private TextBox textBoxMeshDir;
        private ComboBox comboBoxActuator;
        private TextBox textBoxActuatorGain;
        private CheckBox checkBoxExcludeContacts;
        private CheckBox checkBoxMimicEqualities;
        private Button buttonOk;
        private Button buttonCancel;

        public MjcfOptionsDialog(MjcfOptions seed)
        {
            Options = seed ?? new MjcfOptions();

            Text = "MJCF Export Options";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 340);

            BuildControls();
            PopulateFromOptions();
        }

        private void BuildControls()
        {
            int leftLabel = 16;
            int leftInput = 170;
            int inputWidth = 240;
            int y = 16;
            int rowHeight = 28;

            AddLabel("Timestep (s)", leftLabel, y);
            textBoxTimestep = AddTextBox(leftInput, y, 120);
            y += rowHeight;

            AddLabel("Integrator", leftLabel, y);
            comboBoxIntegrator = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(leftInput, y - 2),
                Size = new Size(160, 21),
            };
            foreach (MjcfIntegrator value in Enum.GetValues(typeof(MjcfIntegrator)))
            {
                comboBoxIntegrator.Items.Add(value);
            }
            Controls.Add(comboBoxIntegrator);
            y += rowHeight;

            AddLabel("Gravity (x y z)", leftLabel, y);
            textBoxGravityX = AddTextBox(leftInput, y, 75);
            textBoxGravityY = AddTextBox(leftInput + 80, y, 75);
            textBoxGravityZ = AddTextBox(leftInput + 160, y, 75);
            y += rowHeight;

            AddLabel("Mesh directory", leftLabel, y);
            textBoxMeshDir = AddTextBox(leftInput, y, inputWidth);
            y += rowHeight;

            AddLabel("Actuator type", leftLabel, y);
            comboBoxActuator = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(leftInput, y - 2),
                Size = new Size(160, 21),
            };
            foreach (MjcfActuatorType value in Enum.GetValues(typeof(MjcfActuatorType)))
            {
                comboBoxActuator.Items.Add(value);
            }
            Controls.Add(comboBoxActuator);
            y += rowHeight;

            AddLabel("Actuator gain / gear", leftLabel, y);
            textBoxActuatorGain = AddTextBox(leftInput, y, 120);
            y += rowHeight;

            checkBoxExcludeContacts = new CheckBox
            {
                Text = "Emit <contact><exclude/></contact> for every parent-child pair",
                Location = new Point(leftLabel, y),
                AutoSize = true,
            };
            Controls.Add(checkBoxExcludeContacts);
            y += rowHeight - 4;

            checkBoxMimicEqualities = new CheckBox
            {
                Text = "Emit <equality><joint/></equality> for URDF mimic joints",
                Location = new Point(leftLabel, y),
                AutoSize = true,
            };
            Controls.Add(checkBoxMimicEqualities);
            y += rowHeight + 4;

            buttonOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(ClientSize.Width - 180, ClientSize.Height - 40),
                Size = new Size(75, 25),
            };
            buttonOk.Click += OnOkClick;
            Controls.Add(buttonOk);

            buttonCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(ClientSize.Width - 95, ClientSize.Height - 40),
                Size = new Size(75, 25),
            };
            Controls.Add(buttonCancel);

            AcceptButton = buttonOk;
            CancelButton = buttonCancel;
        }

        private Label AddLabel(string text, int x, int y)
        {
            Label label = new Label
            {
                Text = text,
                Location = new Point(x, y + 2),
                AutoSize = true,
            };
            Controls.Add(label);
            return label;
        }

        private TextBox AddTextBox(int x, int y, int width)
        {
            TextBox box = new TextBox
            {
                Location = new Point(x, y - 2),
                Size = new Size(width, 20),
            };
            Controls.Add(box);
            return box;
        }

        private void PopulateFromOptions()
        {
            textBoxTimestep.Text = Options.Timestep.ToString("G6", CultureInfo.InvariantCulture);
            comboBoxIntegrator.SelectedItem = Options.Integrator;

            double[] gravity = Options.Gravity ?? new double[] { 0, 0, 0 };
            textBoxGravityX.Text = gravity.Length > 0
                ? gravity[0].ToString("G6", CultureInfo.InvariantCulture) : "0";
            textBoxGravityY.Text = gravity.Length > 1
                ? gravity[1].ToString("G6", CultureInfo.InvariantCulture) : "0";
            textBoxGravityZ.Text = gravity.Length > 2
                ? gravity[2].ToString("G6", CultureInfo.InvariantCulture) : "0";

            textBoxMeshDir.Text = Options.MeshDir ?? "meshes";
            comboBoxActuator.SelectedItem = Options.ActuatorType;
            textBoxActuatorGain.Text = Options.ActuatorGain.ToString("G6", CultureInfo.InvariantCulture);
            checkBoxExcludeContacts.Checked = Options.ExcludeAdjacentContacts;
            checkBoxMimicEqualities.Checked = Options.EmitMimicEqualities;
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            if (!TryParseDouble(textBoxTimestep.Text, out double timestep) || timestep <= 0)
            {
                ShowValidation("Timestep must be a positive number.");
                return;
            }
            if (!TryParseDouble(textBoxGravityX.Text, out double gx)
                || !TryParseDouble(textBoxGravityY.Text, out double gy)
                || !TryParseDouble(textBoxGravityZ.Text, out double gz))
            {
                ShowValidation("Gravity components must be numbers.");
                return;
            }
            if (string.IsNullOrWhiteSpace(textBoxMeshDir.Text))
            {
                ShowValidation("Mesh directory must not be empty.");
                return;
            }
            if (!TryParseDouble(textBoxActuatorGain.Text, out double gain))
            {
                ShowValidation("Actuator gain must be a number.");
                return;
            }

            Options.Timestep = timestep;
            Options.Integrator = (MjcfIntegrator)comboBoxIntegrator.SelectedItem;
            Options.Gravity = new double[] { gx, gy, gz };
            Options.MeshDir = textBoxMeshDir.Text.Trim();
            Options.ActuatorType = (MjcfActuatorType)comboBoxActuator.SelectedItem;
            Options.ActuatorGain = gain;
            Options.ExcludeAdjacentContacts = checkBoxExcludeContacts.Checked;
            Options.EmitMimicEqualities = checkBoxMimicEqualities.Checked;
        }

        private static bool TryParseDouble(string text, out double result)
        {
            return double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out result);
        }

        private void ShowValidation(string message)
        {
            MessageBox.Show(this, message, "MJCF Options", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
        }
    }
}
