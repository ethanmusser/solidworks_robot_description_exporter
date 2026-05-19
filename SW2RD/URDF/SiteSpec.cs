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

namespace SW2RD.URDF
{
    // Specification for a MJCF <site> attached to a body. Sites are configured per-link
    // in the property manager: the user supplies a name and a SolidWorks reference
    // coordinate system. At export time the site's pos/quat is computed as the transform
    // from the parent body's coordinate system to the site's coordinate system.
    [DataContract(IsReference = true, Namespace = "http://schemas.datacontract.org/2004/07/SW2URDF")]
    public class SiteSpec
    {
        [DataMember]
        public string Name;

        [DataMember]
        public string CoordinateSystemName;

        public SiteSpec()
        {
            Name = "";
            CoordinateSystemName = "";
        }

        public SiteSpec(string name, string coordinateSystemName)
        {
            Name = name ?? "";
            CoordinateSystemName = coordinateSystemName ?? "";
        }

        public SiteSpec Clone()
        {
            return new SiteSpec(Name, CoordinateSystemName);
        }
    }

    // The set of components from which a link's mass and inertia are computed.
    public enum InertialSource
    {
        Visual = 0,
        Collision = 1,
        Custom = 2,
    }
}
