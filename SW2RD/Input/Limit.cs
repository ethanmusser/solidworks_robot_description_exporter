using SW2RD.Utilities;
using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <limit> element of a joint. Every field is optional (null = the
    // attribute is omitted on export); the non-nullable getters exist for
    // legacy call sites and assume the value has been set.
    public class Limit
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        private double? lower;
        private double? upper;
        private double? effort;
        private double? velocity;

        public double Lower
        {
            get => lower.Value;
            set => lower = value;
        }

        public double Upper
        {
            get => upper.Value;
            set => upper = value;
        }

        public double Effort
        {
            get => effort.Value;
            set => effort = value;
        }

        public double Velocity
        {
            get => velocity.Value;
            set => velocity = value;
        }

        // Null-safe accessors used by the KinematicTree adapter and anywhere
        // else that needs to read a limit field without knowing whether the
        // user has configured it.
        public double? LowerOrNull => lower;

        public double? UpperOrNull => upper;

        public double? EffortOrNull => effort;

        public double? VelocityOrNull => velocity;

        // Setters used by all limit write paths. Empty textbox / null value ->
        // unset (the writer omits the attribute); populated -> parsed double.
        public void SetLowerOrClear(string text) => lower = ParseOrClear(text, lower);

        public void SetUpperOrClear(string text) => upper = ParseOrClear(text, upper);

        public void SetEffortOrClear(string text) => effort = ParseOrClear(text, effort);

        public void SetVelocityOrClear(string text) => velocity = ParseOrClear(text, velocity);

        public void SetLower(double? value) => lower = value;

        public void SetUpper(double? value) => upper = value;

        public void SetEffort(double? value) => effort = value;

        public void SetVelocity(double? value) => velocity = value;

        private static double? ParseOrClear(string text, double? current)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            if (InvariantNumberFormat.TryParse(text, out double result))
            {
                return result;
            }
            logger.Warn("Ignoring invalid limit value '" + text + "'.");
            return current;
        }

        public Limit Clone()
        {
            return new Limit
            {
                lower = lower,
                upper = upper,
                effort = effort,
                velocity = velocity,
            };
        }

        public void FillBoxes(TextBox boxLower, TextBox boxUpper,
            TextBox boxEffort, TextBox boxVelocity, string format)
        {
            boxLower.Text = InvariantNumberFormat.Format(lower, format);
            boxUpper.Text = InvariantNumberFormat.Format(upper, format);
            boxEffort.Text = InvariantNumberFormat.Format(effort, format);
            boxVelocity.Text = InvariantNumberFormat.Format(velocity, format);
        }

        public void SetValues(TextBox boxLower, TextBox boxUpper,
            TextBox boxEffort, TextBox boxVelocity)
        {
            SetLowerOrClear(boxLower.Text);
            SetUpperOrClear(boxUpper.Text);
            SetEffortOrClear(boxEffort.Text);
            SetVelocityOrClear(boxVelocity.Text);
        }
    }
}
