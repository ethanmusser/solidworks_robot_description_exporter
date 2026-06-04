using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <color> element of <material>. Single rgba (0..1 each).
    public class Color
    {
        private double[] rgba;

        public Color()
        {
            rgba = new double[] { 1, 1, 1, 1 };
        }

        public double Red
        {
            get => rgba[0];
            set => rgba[0] = value;
        }

        public double Green
        {
            get => rgba[1];
            set => rgba[1] = value;
        }

        public double Blue
        {
            get => rgba[2];
            set => rgba[2] = value;
        }

        public double Alpha
        {
            get => rgba[3];
            set => rgba[3] = value;
        }

        public Color Clone()
        {
            return new Color { rgba = (double[])rgba.Clone() };
        }

        public void FillBoxes(DomainUpDown boxRed, DomainUpDown boxGreen,
            DomainUpDown boxBlue, DomainUpDown boxAlpha, string format)
        {
            string[] rgbaText = UrdfFormat.FormatArray(rgba, format);
            if (rgbaText != null)
            {
                boxRed.Text = rgbaText[0];
                boxGreen.Text = rgbaText[1];
                boxBlue.Text = rgbaText[2];
                boxAlpha.Text = rgbaText[3];
            }
        }

        public void Update(DomainUpDown boxRed, DomainUpDown boxGreen,
            DomainUpDown boxBlue, DomainUpDown boxAlpha)
        {
            rgba = UrdfFormat.ParseArray(
                new string[] { boxRed.Text, boxGreen.Text, boxBlue.Text, boxAlpha.Text });
        }

        public void SetColor(double[] value)
        {
            Red = value[0];
            Green = value[1];
            Blue = value[2];
            Alpha = value[3];
        }

        public double[] GetColor()
        {
            return new double[] { Red, Green, Blue, Alpha };
        }
    }
}
