using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SW2RD.URDF
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

        // Per-joint gate for "auto-derive Lower/Upper from a SolidWorks
        // limit mate at export time". Default true so existing configs
        // and new joints keep today's behavior. The field is normalized
        // to true during deserialization (see OnDeserializing) so an
        // older config that pre-dates this field comes back as
        // auto-compute=true rather than the zero-init false.
        [DataMember(IsRequired = false)]
        public bool AutoComputeLimits;

        // MJCF <joint ref> (joint position assumed by the model when
        // MuJoCo loads it). URDF has no analog; ignored on URDF export.
        // null = attribute omitted on export (MJCF default 0).
        [DataMember(IsRequired = false)]
        public double? Reference;

        // MJCF <joint armature> (equivalent rotor inertia of the
        // actuator). URDF has no analog; ignored on URDF export.
        // null = attribute omitted on export.
        [DataMember(IsRequired = false)]
        public double? Armature;

        // When true, the joint axis is derived from the SolidWorks
        // kinematic chain at export time and AxisName is ignored. When
        // false, AxisName is the user-picked SW reference axis and
        // EstimateAxis reads it directly. Maps the modernized
        // SelectionBox-only UI ("Auto-derive axis from kinematic chain"
        // checkbox) onto the existing CreateJoint /
        // EstimateGlobalJointFromComponents pipeline.
        //
        // Legacy configs stored the sentinel literal
        // "Automatically Generate" in AxisName instead. The
        // [OnDeserialized] callback below migrates those onto
        // AutoDeriveAxis=true + AxisName="" so the new UI sees a clean
        // boolean and an empty SelectionBox on first load.
        [DataMember(IsRequired = false)]
        public bool AutoDeriveAxis;

        // Re-default fields that pre-date them so old configs still
        // come back with sensible values. Required because
        // DataContractSerializer constructs the object via
        // FormatterServices.GetUninitializedObject and does NOT call
        // the parameterless constructor (see AGENTS.md "DataContract /
        // Link persistence gotchas").
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            AutoComputeLimits = true;
        }

        // Legacy "Automatically Generate" sentinel migration. Runs
        // AFTER field hydration so we can read the deserialized
        // AxisName / CoordinateSystemName values. Pre-AutoDeriveAxis
        // configs encode "auto" by writing the literal string into
        // AxisName; we map that onto the new boolean and clear the
        // string so the SelectionBox is empty on first reopen.
        [OnDeserialized]
        private void OnDeserializedNormalizeAutoAxis(StreamingContext context)
        {
            if (AxisName == "Automatically Generate")
            {
                AutoDeriveAxis = true;
                AxisName = "";
            }
        }

        public Joint() : base("joint", false)
        {
            AutoComputeLimits = true;
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
            AutoComputeLimits = joint.AutoComputeLimits;
            AutoDeriveAxis = joint.AutoDeriveAxis;
            Reference = joint.Reference;
            Armature = joint.Armature;
        }

        public void SetJointKinematics(Joint joint)
        {
            CoordinateSystemName = joint.CoordinateSystemName;
            AxisName = joint.AxisName;
            AxisFlipped = joint.AxisFlipped;
            AutoComputeLimits = joint.AutoComputeLimits;
            AutoDeriveAxis = joint.AutoDeriveAxis;
            Reference = joint.Reference;
            Armature = joint.Armature;
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