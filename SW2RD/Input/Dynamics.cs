using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <dynamics> element of a joint. Both fields optional (null = omitted).
    public class Dynamics
    {
        private double? damping;
        private double? friction;

        public double Damping
        {
            get => damping.Value;
            set => damping = value;
        }

        public double Friction
        {
            get => friction.Value;
            set => friction = value;
        }

        public double? DampingOrNull => damping;

        public double? FrictionOrNull => friction;

        public void SetDampingOrClear(string text) => damping = ParseOrClear(text, damping);

        public void SetFrictionOrClear(string text) => friction = ParseOrClear(text, friction);

        public void SetDamping(double? value) => damping = value;

        public void SetFriction(double? value) => friction = value;

        private static double? ParseOrClear(string text, double? current)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            return InvariantNumberFormat.TryParse(text, out double result) ? (double?)result : current;
        }

        public Dynamics Clone()
        {
            return new Dynamics { damping = damping, friction = friction };
        }

        public void FillBoxes(TextBox boxDamping, TextBox boxFriction, string format)
        {
            boxDamping.Text = InvariantNumberFormat.Format(damping, format);
            boxFriction.Text = InvariantNumberFormat.Format(friction, format);
        }

        public void SetValues(TextBox boxDamping, TextBox boxFriction)
        {
            SetDampingOrClear(boxDamping.Text);
            SetFrictionOrClear(boxFriction.Text);
        }
    }
}
