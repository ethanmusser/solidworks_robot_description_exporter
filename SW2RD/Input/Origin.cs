using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <origin> element of a joint / visual / collision / inertial. Plain
    // C# input model: xyz in meters, rpy in radians (the URDF convention the
    // PMPage edits in). Converted to the canonical quaternion-based PoseModel
    // at the KinematicTreeAdapter boundary.
    public class Origin
    {
        private double[] xyz;
        private double[] rpy;

        public bool isCustomized;

        public Origin(bool isRequired)
        {
            isCustomized = false;
            xyz = new double[] { 0, 0, 0 };
            rpy = new double[] { 0, 0, 0 };
        }

        public double[] GetXYZ()
        {
            return (double[])xyz.Clone();
        }

        public void SetXYZ(double[] value)
        {
            xyz = value;
        }

        public double X
        {
            get => xyz[0];
            set => xyz[0] = value;
        }

        public double Y
        {
            get => xyz[1];
            set => xyz[1] = value;
        }

        public double Z
        {
            get => xyz[2];
            set => xyz[2] = value;
        }

        public double[] GetRPY()
        {
            return (double[])rpy.Clone();
        }

        public void SetRPY(double[] value)
        {
            rpy = value;
        }

        public double Roll
        {
            get => rpy[0];
            set => rpy[0] = value;
        }

        public double Pitch
        {
            get => rpy[1];
            set => rpy[1] = value;
        }

        public double Yaw
        {
            get => rpy[2];
            set => rpy[2] = value;
        }

        public Origin Clone()
        {
            return new Origin(false)
            {
                isCustomized = isCustomized,
                xyz = (double[])xyz.Clone(),
                rpy = (double[])rpy.Clone(),
            };
        }

        public void FillBoxes(TextBox boxX, TextBox boxY, TextBox boxZ, TextBox boxRoll,
            TextBox boxPitch, TextBox boxYaw, string format)
        {
            string[] xyzText = UrdfFormat.FormatArray(xyz, format);
            if (xyzText != null)
            {
                boxX.Text = xyzText[0];
                boxY.Text = xyzText[1];
                boxZ.Text = xyzText[2];
            }

            string[] rpyText = UrdfFormat.FormatArray(rpy, format);
            if (rpyText != null)
            {
                boxRoll.Text = rpyText[0];
                boxPitch.Text = rpyText[1];
                boxYaw.Text = rpyText[2];
            }
        }

        public void Update(TextBox boxX, TextBox boxY, TextBox boxZ,
            TextBox boxRoll, TextBox boxPitch, TextBox boxYaw)
        {
            xyz = UrdfFormat.ParseArray(new string[] { boxX.Text, boxY.Text, boxZ.Text });
            rpy = UrdfFormat.ParseArray(new string[] { boxRoll.Text, boxPitch.Text, boxYaw.Text });
        }
    }
}
