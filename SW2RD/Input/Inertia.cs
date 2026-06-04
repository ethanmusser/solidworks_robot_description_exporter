using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <inertia> element (moment of inertia, kg*m^2), child of <inertial>.
    public class Inertia
    {
        public double Ixx;
        public double Ixy;
        public double Ixz;
        public double Iyy;
        public double Iyz;
        public double Izz;

        public Inertia Clone()
        {
            return new Inertia
            {
                Ixx = Ixx,
                Ixy = Ixy,
                Ixz = Ixz,
                Iyy = Iyy,
                Iyz = Iyz,
                Izz = Izz,
            };
        }

        public void SetMomentMatrix(double[] array)
        {
            Ixx = array[0];
            Ixy = -array[1];
            Ixz = -array[2];
            Iyy = array[4];
            Iyz = -array[5];
            Izz = array[8];
        }

        public void FillBoxes(TextBox boxIxx, TextBox boxIxy, TextBox boxIxz,
            TextBox boxIyy, TextBox boxIyz, TextBox boxIzz, string format)
        {
            boxIxx.Text = UrdfFormat.Format(Ixx, format);
            boxIxy.Text = UrdfFormat.Format(Ixy, format);
            boxIxz.Text = UrdfFormat.Format(Ixz, format);
            boxIyy.Text = UrdfFormat.Format(Iyy, format);
            boxIyz.Text = UrdfFormat.Format(Iyz, format);
            boxIzz.Text = UrdfFormat.Format(Izz, format);
        }

        public void Update(TextBox boxIxx, TextBox boxIxy, TextBox boxIxz,
            TextBox boxIyy, TextBox boxIyz, TextBox boxIzz)
        {
            Ixx = Parse(boxIxx.Text);
            Ixy = Parse(boxIxy.Text);
            Ixz = Parse(boxIxz.Text);
            Iyy = Parse(boxIyy.Text);
            Iyz = Parse(boxIyz.Text);
            Izz = Parse(boxIzz.Text);
        }

        private static double Parse(string text)
        {
            return UrdfFormat.TryParse(text, out double parsed) ? parsed : 0.0;
        }

        internal double[] GetMoment()
        {
            return new double[] { Ixx, Ixy, Ixz, Ixy, Iyy, Iyz, Ixz, Iyz, Izz };
        }
    }
}
