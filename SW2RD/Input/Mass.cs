using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <mass> element (kilograms), child of <inertial>.
    public class Mass
    {
        public double Value;

        public Mass()
        {
            Value = 0.0;
        }

        public Mass Clone()
        {
            return new Mass { Value = Value };
        }

        public void FillBoxes(TextBox box, string format)
        {
            box.Text = UrdfFormat.Format(Value, format);
        }

        public void Update(TextBox box)
        {
            if (UrdfFormat.TryParse(box.Text, out double parsed))
            {
                Value = parsed;
            }
            else
            {
                Value = 0.0;
            }
        }
    }
}
