/*
Copyright (c) 2015 Stephen Brawner

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

namespace SW2URDF.MJCF
{
    public enum MjcfIntegrator
    {
        Euler,
        RK4,
        Implicit,
        ImplicitFast,
    }

    public enum MjcfActuatorType
    {
        None,
        Motor,
        Position,
        Velocity,
    }

    // Holds all MJCF-specific knobs that the user exposes through the MjcfOptionsDialog. Every
    // field has a value explicitly chosen by the user in the dialog, so the writer never has to
    // assume a default on their behalf.
    public class MjcfOptions
    {
        public double Timestep { get; set; }

        public MjcfIntegrator Integrator { get; set; }

        public double[] Gravity { get; set; }

        public string MeshDir { get; set; }

        public MjcfActuatorType ActuatorType { get; set; }

        // Only meaningful when ActuatorType != None. For Motor the gear is applied; for Position
        // and Velocity this is used as the proportional gain (kp or kv respectively).
        public double ActuatorGain { get; set; }

        // When true, an <exclude body1="parent" body2="child"/> pair is emitted for every parent
        // -> child edge in the robot tree.
        public bool ExcludeAdjacentContacts { get; set; }

        // When true and the URDF Joint has a Mimic child, an <equality><joint/></equality>
        // constraint is emitted for it.
        public bool EmitMimicEqualities { get; set; }

        public MjcfOptions()
        {
            Timestep = 0.002;
            Integrator = MjcfIntegrator.RK4;
            Gravity = new double[] { 0.0, 0.0, -9.81 };
            MeshDir = "meshes";
            ActuatorType = MjcfActuatorType.None;
            ActuatorGain = 1.0;
            ExcludeAdjacentContacts = true;
            EmitMimicEqualities = true;
        }

        public string IntegratorToMjcf()
        {
            switch (Integrator)
            {
                case MjcfIntegrator.RK4: return "RK4";
                case MjcfIntegrator.Implicit: return "implicit";
                case MjcfIntegrator.ImplicitFast: return "implicitfast";
                case MjcfIntegrator.Euler:
                default:
                    return "Euler";
            }
        }
    }
}
