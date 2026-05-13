using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SW2RD.URDF
{
    //The limit element of a joint.
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Limit : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute LowerAttribute;

        [DataMember]
        private readonly URDFAttribute UpperAttribute;

        [DataMember]
        private readonly URDFAttribute EffortAttribute;

        [DataMember]
        private readonly URDFAttribute VelocityAttribute;

        public double Lower
        {
            get => (double)LowerAttribute.Value;
            set => LowerAttribute.Value = value;
        }

        public double Upper
        {
            get => (double)UpperAttribute.Value;
            set => UpperAttribute.Value = value;
        }

        public double Effort
        {
            get => (double)EffortAttribute.Value;
            set => EffortAttribute.Value = value;
        }

        public double Velocity
        {
            get => (double)VelocityAttribute.Value;
            set => VelocityAttribute.Value = value;
        }

        // Null-safe accessors used by the KinematicTree adapter and
        // anywhere else that needs to read a limit field without knowing
        // whether the user has configured it. The non-nullable getters
        // above unconditionally cast `Value` to double, so they NPE on
        // a default-constructed Limit (where every URDFAttribute.Value is
        // null). These wrappers return the field as `double?` and yield
        // `null` whenever the underlying URDFAttribute has not been set.
        public double? LowerOrNull => LowerAttribute.IsSet() ? (double?)LowerAttribute.Value : null;

        public double? UpperOrNull => UpperAttribute.IsSet() ? (double?)UpperAttribute.Value : null;

        public double? EffortOrNull => EffortAttribute.IsSet() ? (double?)EffortAttribute.Value : null;

        public double? VelocityOrNull => VelocityAttribute.IsSet() ? (double?)VelocityAttribute.Value : null;

        // Setters used by the Joint Properties UI on link save. Empty
        // textbox -> Value = null (the writer omits the attribute);
        // populated -> Value = parsed double. Centralizing the
        // empty-string handling here keeps the PMPage round-trip simple
        // and matches the omit-on-blank semantics the URDF / MJCF
        // writers implement for limits.
        public void SetLowerOrClear(string text) => SetOrClear(LowerAttribute, text);

        public void SetUpperOrClear(string text) => SetOrClear(UpperAttribute, text);

        public void SetEffortOrClear(string text) => SetOrClear(EffortAttribute, text);

        public void SetVelocityOrClear(string text) => SetOrClear(VelocityAttribute, text);

        public void SetLower(double? value) => SetOptional(LowerAttribute, value);

        public void SetUpper(double? value) => SetOptional(UpperAttribute, value);

        public void SetEffort(double? value) => SetOptional(EffortAttribute, value);

        public void SetVelocity(double? value) => SetOptional(VelocityAttribute, value);

        private static void SetOrClear(URDFAttribute attr, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                attr.Value = null;
            }
            else
            {
                attr.SetDoubleValueFromString(text);
            }
        }

        private static void SetOptional(URDFAttribute attr, double? value)
        {
            attr.Value = value.HasValue ? (object)value.Value : null;
        }

        public Limit() : base("limit", false)
        {
            EffortAttribute = new URDFAttribute("effort", true, null);
            VelocityAttribute = new URDFAttribute("velocity", true, null);
            LowerAttribute = new URDFAttribute("lower", false, null);
            UpperAttribute = new URDFAttribute("upper", false, null);

            Attributes.Add(LowerAttribute);
            Attributes.Add(UpperAttribute);
            Attributes.Add(EffortAttribute);
            Attributes.Add(VelocityAttribute);
        }

        public void FillBoxes(TextBox boxLower, TextBox boxUpper,
            TextBox boxEffort, TextBox boxVelocity, string format)
        {
            boxLower.Text = LowerAttribute.GetTextFromDoubleValue(format);
            boxUpper.Text = UpperAttribute.GetTextFromDoubleValue(format);
            boxEffort.Text = EffortAttribute.GetTextFromDoubleValue(format);
            boxVelocity.Text = VelocityAttribute.GetTextFromDoubleValue(format);
        }

        public void SetValues(TextBox boxLower, TextBox boxUpper,
            TextBox boxEffort, TextBox boxVelocity)
        {
            if (string.IsNullOrWhiteSpace(boxLower.Text) &&
                string.IsNullOrWhiteSpace(boxUpper.Text) &&
                string.IsNullOrWhiteSpace(boxEffort.Text) &&
                string.IsNullOrWhiteSpace(boxVelocity.Text) &&
                !IsRequired())
            {
                // If all text boxes are empty and this element isn't required, then leave blank
                return;
            }
            LowerAttribute.SetDoubleValueFromString(boxLower.Text);
            UpperAttribute.SetDoubleValueFromString(boxUpper.Text);
            EffortAttribute.SetDoubleValueFromString(boxEffort.Text);
            VelocityAttribute.SetDoubleValueFromString(boxVelocity.Text);
        }

        public override void SetRequired(bool required)
        {
            base.SetRequired(required);
            UpperAttribute.SetRequired(required);
            LowerAttribute.SetRequired(required);
        }

        public override bool AreRequiredFieldsSatisfied()
        {
            // If a limit is required, then these fields should be as well.
            UpperAttribute.SetRequired(IsRequired());
            LowerAttribute.SetRequired(IsRequired());
            return base.AreRequiredFieldsSatisfied();
        }
    }
}