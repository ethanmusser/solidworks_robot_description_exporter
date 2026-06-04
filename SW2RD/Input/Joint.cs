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

using System.Collections.Generic;
using System;
using System.Windows.Forms;

namespace SW2RD.Input
{
    // The joint connecting a link to its parent. Plain-C# input/edit model:
    // angular quantities are in DEGREES (the SolidWorks PMP convention) and
    // converted to canonical radians at the KinematicTreeAdapter boundary.
    public class Joint
    {
        public static readonly List<string> AvailableTypes = new List<string>
        {
            "revolute", "continuous", "prismatic", "fixed", "floating", "planar"
        };

        public static bool UsesAngularUnits(string jointType)
        {
            return jointType == "revolute" || jointType == "continuous";
        }

        public static bool HasCompleteRangeLimit(Limit limit)
        {
            return limit != null && limit.LowerOrNull.HasValue && limit.UpperOrNull.HasValue;
        }

        public static bool HasPartialRangeLimit(Limit limit)
        {
            if (limit == null)
            {
                return false;
            }
            return limit.LowerOrNull.HasValue != limit.UpperOrNull.HasValue;
        }

        public static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        public static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        public static double AngularDampingPerDegreeToPerRadian(double dampingPerDegree)
        {
            return dampingPerDegree * 180.0 / Math.PI;
        }

        public string Name;

        public string Type;

        public Origin Origin;

        public ParentLink Parent;

        public ChildLink Child;

        public Axis Axis;

        public Limit Limit;

        public Dynamics Dynamics;

        public string CoordinateSystemName;

        public string AxisName;

        // Reverse-direction toggle for the joint axis. Mirrors the "Reverse
        // Direction" button on SolidWorks' own coord-system / extrude PMs.
        // When true, EstimateAxis negates the localized axis vector after
        // LocalizeAxis.
        public bool AxisFlipped;

        // Per-joint gate for "auto-derive Lower/Upper from a SolidWorks
        // limit mate at export time". Default true so new joints keep the
        // historical behavior.
        public bool AutoComputeLimits;

        // MJCF <joint ref> (joint position assumed by the model when MuJoCo
        // loads it). URDF has no analog; ignored on URDF export. null =
        // attribute omitted on export (MJCF default 0).
        public double? Reference;

        // MJCF <joint armature> (equivalent rotor inertia of the actuator).
        // URDF has no analog; ignored on URDF export. null = attribute omitted.
        public double? Armature;

        // When true, the joint axis is derived from the SolidWorks kinematic
        // chain at export time and AxisName is ignored. Legacy configs stored
        // the sentinel literal "Automatically Generate" in AxisName; that is
        // migrated onto AutoDeriveAxis=true + AxisName="" in
        // KinematicTreeAdapter.ApplyJoint (the Config-load path).
        public bool AutoDeriveAxis;

        public Joint()
        {
            AutoComputeLimits = true;
            Name = "";
            Type = "";
            Origin = new Origin(false);
            Parent = new ParentLink();
            Child = new ChildLink();
            Axis = new Axis();
            Limit = new Limit();
            Dynamics = new Dynamics();
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

        public Joint Clone()
        {
            Joint clone = new Joint
            {
                Name = Name,
                Type = Type,
                Origin = Origin.Clone(),
                Parent = Parent.Clone(),
                Child = Child.Clone(),
                Axis = Axis.Clone(),
                Limit = Limit.Clone(),
                Dynamics = Dynamics.Clone(),
                CoordinateSystemName = CoordinateSystemName,
                AxisName = AxisName,
                AxisFlipped = AxisFlipped,
                AutoComputeLimits = AutoComputeLimits,
                AutoDeriveAxis = AutoDeriveAxis,
                Reference = Reference,
                Armature = Armature,
            };
            return clone;
        }

        // In-place copy from another Joint (replaces the legacy
        // URDFElement.SetElement). Used by clone / duplicate-node paths.
        public void SetElement(Joint joint)
        {
            Name = joint.Name;
            Type = joint.Type;
            Origin = joint.Origin.Clone();
            Parent = joint.Parent.Clone();
            Child = joint.Child.Clone();
            Axis = joint.Axis.Clone();
            Limit = joint.Limit.Clone();
            Dynamics = joint.Dynamics.Clone();
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
            Axis = joint.Axis.Clone();
            Origin = joint.Origin.Clone();
        }
    }
}
