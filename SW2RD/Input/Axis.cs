using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <axis> element of a joint. xyz is a (possibly unnormalized) direction
    // vector in the joint frame.
    public class Axis
    {
        private double[] xyz;

        public Axis()
        {
            xyz = new double[] { 0, 0, 0 };
        }

        public double[] GetXYZ()
        {
            return (double[])xyz.Clone();
        }

        public void SetXYZ(double[] value)
        {
            xyz = (double[])value.Clone();
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

        public Axis Clone()
        {
            return new Axis { xyz = (double[])xyz.Clone() };
        }

        public void FillBoxes(TextBox boxX, TextBox boxY, TextBox boxZ, string format)
        {
            string[] xyzText = InvariantNumberFormat.FormatArray(xyz, format);
            if (xyzText != null)
            {
                boxX.Text = xyzText[0];
                boxY.Text = xyzText[1];
                boxZ.Text = xyzText[2];
            }
        }

        public void Update(TextBox boxX, TextBox boxY, TextBox boxZ)
        {
            xyz = InvariantNumberFormat.ParseArray(new string[] { boxX.Text, boxY.Text, boxZ.Text });
        }
    }
}
