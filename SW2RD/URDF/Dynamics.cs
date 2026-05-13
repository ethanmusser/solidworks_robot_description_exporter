/*
Copyright (c) 2026 Ethan J. Musser

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System.Runtime.Serialization;
using System.Windows.Forms;

namespace SW2RD.URDF
{
    //The dynamics element of a joint.
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class Dynamics : URDFElement
    {
        [DataMember]
        private readonly URDFAttribute DampingAttribute;

        public double Damping
        {
            get => (double)DampingAttribute.Value;
            set => DampingAttribute.Value = value;
        }

        [DataMember]
        private readonly URDFAttribute FrictionAttribute;

        public double Friction
        {
            get => (double)FrictionAttribute.Value;
            set => FrictionAttribute.Value = value;
        }

        // Null-safe accessors mirroring Limit.{LowerOrNull,UpperOrNull}.
        // The non-nullable getters above unconditionally cast `Value` to
        // double, so they NPE on a default-constructed Dynamics (where
        // both URDFAttribute.Value are null). The KinematicTree adapter
        // and the Joint Properties UI use these to read damping /
        // friction without knowing whether the user has configured them.
        public double? DampingOrNull => DampingAttribute.IsSet() ? (double?)DampingAttribute.Value : null;

        public double? FrictionOrNull => FrictionAttribute.IsSet() ? (double?)FrictionAttribute.Value : null;

        // Direct setters for the underlying URDFAttributes. Used by the
        // Joint Properties UI on link save: empty textbox -> Value = null
        // (writer omits the attribute), populated -> Value = parsed
        // double. Centralizing the empty-string handling here keeps the
        // PMPage round-trip simple and matches the omit-on-blank
        // semantics the URDF / MJCF writers already implement.
        public void SetDampingOrClear(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                DampingAttribute.Value = null;
            }
            else
            {
                DampingAttribute.SetDoubleValueFromString(text);
            }
        }

        public void SetFrictionOrClear(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                FrictionAttribute.Value = null;
            }
            else
            {
                FrictionAttribute.SetDoubleValueFromString(text);
            }
        }

        public Dynamics() : base("dynamics", false)
        {
            DampingAttribute = new URDFAttribute("damping", false, null);
            FrictionAttribute = new URDFAttribute("friction", false, null);

            Attributes.Add(DampingAttribute);
            Attributes.Add(FrictionAttribute);
        }

        public void FillBoxes(TextBox boxDamping, TextBox boxFriction, string format)
        {
            boxDamping.Text = DampingAttribute.GetTextFromDoubleValue(format);
            boxFriction.Text = FrictionAttribute.GetTextFromDoubleValue(format);
        }

        public void SetValues(TextBox boxDamping, TextBox boxFriction)
        {
            DampingAttribute.SetDoubleValueFromString(boxDamping.Text);
            FrictionAttribute.SetDoubleValueFromString(boxFriction.Text);
        }
    }
}