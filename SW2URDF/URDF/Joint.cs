using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SW2URDF.URDF
{
    //The joint class. There is one for every link but the base link
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Joint : URDFElement
    {
        public static readonly List<string> AvailableTypes = new List<string>
        {
            "revolute", "continuous", "prismatic", "fixed", "floating", "planar"
        };

        [DataMember]
        private readonly URDFAttribute NameAttribute;

        public string Name
        {
            get => (string)NameAttribute.Value;
            set => NameAttribute.Value = value;
        }

        [DataMember]
        private readonly URDFAttribute TypeAttribute;

        public string Type
        {
            get => (string)TypeAttribute.Value;
            set => TypeAttribute.Value = value;
        }

        [DataMember]
        public readonly Origin Origin;

        [DataMember]
        public readonly ParentLink Parent;

        [DataMember]
        public readonly ChildLink Child;

        [DataMember]
        public readonly Axis Axis;

        [DataMember]
        public readonly Limit Limit;

        [DataMember]
        public readonly Calibration Calibration;

        [DataMember]
        public readonly Dynamics Dynamics;

        [DataMember]
        public readonly SafetyController Safety;

        [DataMember(IsRequired = false)]
        public readonly Mimic Mimic;

        [DataMember]
        public string CoordinateSystemName;

        [DataMember]
        public string AxisName;

        // Reverse-direction toggle for the joint axis. Mirrors the "Reverse
        // Direction" button on SolidWorks' own coord-system / extrude PMs.
        // When true, EstimateAxis negates the localized axis vector after
        // LocalizeAxis. IsRequired=false so older configs (which omit this
        // field entirely) deserialize cleanly with the default `false`.
        [DataMember(IsRequired = false)]
        public bool AxisFlipped;

        public Joint() : base("joint", false)
        {
            Origin = new Origin(false);
            Parent = new ParentLink();
            Child = new ChildLink();
            Axis = new Axis();

            Limit = new Limit();
            Calibration = new Calibration();
            Dynamics = new Dynamics();
            Safety = new SafetyController();
            Mimic = new Mimic();

            NameAttribute = new URDFAttribute("name", true, "");
            TypeAttribute = new URDFAttribute("type", true, "");

            Attributes.Add(NameAttribute);
            Attributes.Add(TypeAttribute);

            ChildElements.Add(Origin);
            ChildElements.Add(Parent);
            ChildElements.Add(Child);
            ChildElements.Add(Axis);

            ChildElements.Add(Limit);
            ChildElements.Add(Calibration);
            ChildElements.Add(Dynamics);
            ChildElements.Add(Safety);
            ChildElements.Add(Mimic);
        }

        public void FillBoxes(TextBox boxName, ComboBox boxType)
        {
            boxName.Text = Name;
            boxType.Text = Type;
        }

        public void Update(TextBox boxName, ComboBox boxType)
        {
            Name = boxName.Text;
            Type = boxType.Text;
        }

        public override bool ElementContainsData()
        {
            return !string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Type);
        }

        public override bool AreRequiredFieldsSatisfied()
        {
            Limit.SetRequired((Type == "prismatic" || Type == "revolute"));
            return base.AreRequiredFieldsSatisfied();
        }

        public override void AppendToCSVDictionary(List<string> context, OrderedDictionary dictionary)
        {
            string contextString = string.Join(".", context);

            string coordSysContext = contextString + ".CoordSysName";
            dictionary.Add(coordSysContext, CoordinateSystemName);

            string axisContext = contextString + ".AxisName";
            dictionary.Add(axisContext, AxisName);

            string axisFlippedContext = contextString + ".Joint.AxisFlipped";
            dictionary.Add(axisFlippedContext, AxisFlipped.ToString());

            base.AppendToCSVDictionary(context, dictionary);
        }

        public override void SetElement(URDFElement externalElement)
        {
            base.SetElement(externalElement);

            // The base method already performs the type check, so we don't have to for this cast
            Joint joint = (Joint)externalElement;

            // These plain fields aren't kept as URDFAttribute objects and so
            // are tracked separately. Without these manual copies, every
            // Link.Clone() after a DataContractSerializer reload silently
            // resets them to the zero-init default (see AGENTS.md
            // four-paths landmine, Joint-scope variant).
            CoordinateSystemName = joint.CoordinateSystemName;
            AxisName = joint.AxisName;
            AxisFlipped = joint.AxisFlipped;
        }

        public override void SetElementFromData(List<string> context, StringDictionary dictionary)
        {
            string contextString = string.Join(".", context);

            string coordSysContext = contextString + ".CoordSysName";
            CoordinateSystemName = dictionary[coordSysContext];

            string axisContext = contextString + ".AxisName";
            AxisName = dictionary[axisContext];

            string axisFlippedContext = contextString + ".Joint.AxisFlipped";
            string axisFlippedRaw = dictionary[axisFlippedContext];
            AxisFlipped = bool.TryParse(axisFlippedRaw, out bool parsed) && parsed;

            base.SetElementFromData(context, dictionary);
        }

        public void SetJointKinematics(Joint joint)
        {
            CoordinateSystemName = joint.CoordinateSystemName;
            AxisName = joint.AxisName;
            AxisFlipped = joint.AxisFlipped;
            Type = joint.Type;
            Axis.SetElement(joint.Axis);
            Origin.SetElement(joint.Origin);
        }

        public void SetJointNonKinematics(Joint joint)
        {
            Limit.SetElement(joint.Limit);
            Calibration.SetElement(joint.Calibration);
            Dynamics.SetElement(joint.Dynamics);
            Safety.SetElement(joint.Safety);
        }
    }
}