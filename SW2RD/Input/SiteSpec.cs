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

namespace SW2RD.Input
{
    // Where a site's pose comes from.
    //   CoordinateSystem - a picked SW reference coordinate system; defines the
    //                      site's full 6-DOF pose relative to the parent body.
    //   ReferencePoint   - a picked SW reference point; defines only the site's
    //                      position. The orientation defaults to identity in the
    //                      parent link frame (the reference point carries no basis
    //                      vectors). Chosen for ergonomics: a point is far cheaper
    //                      to author than a coordinate system, and many site
    //                      consumers (e.g. MJCF position equality constraints) only
    //                      care about position.
    public enum SiteSourceType
    {
        CoordinateSystem = 0,
        ReferencePoint = 1,
    }

    // Specification for a named reference frame attached to a body. Sites are
    // configured per-link in the property manager: the user supplies a name and
    // picks EITHER a SolidWorks reference coordinate system (full pose) OR a
    // SolidWorks reference point (position only, identity rotation). At export time
    // the site's pos/quat is computed in the parent body's local frame. Exported to
    // both formats: an MJCF <site> child of the body, and in URDF an empty <link>
    // connected to the parent by a fixed <joint>.
    public class SiteSpec
    {
        public string Name;

        // Used when Source == CoordinateSystem.
        public string CoordinateSystemName;

        // Which kind of SW reference defines this site's location.
        public SiteSourceType Source;

        // Used when Source == ReferencePoint.
        public string ReferencePointName;

        public SiteSpec()
        {
            Name = "";
            CoordinateSystemName = "";
            Source = SiteSourceType.CoordinateSystem;
            ReferencePointName = "";
        }

        // Backward-compatible coordinate-system ctor (Source defaults to
        // CoordinateSystem). Retained for callers / tests that predate the
        // reference-point source.
        public SiteSpec(string name, string coordinateSystemName)
        {
            Name = name ?? "";
            CoordinateSystemName = coordinateSystemName ?? "";
            Source = SiteSourceType.CoordinateSystem;
            ReferencePointName = "";
        }

        public SiteSpec(
            string name,
            SiteSourceType source,
            string coordinateSystemName,
            string referencePointName)
        {
            Name = name ?? "";
            Source = source;
            CoordinateSystemName = coordinateSystemName ?? "";
            ReferencePointName = referencePointName ?? "";
        }

        public SiteSpec Clone()
        {
            return new SiteSpec(Name, Source, CoordinateSystemName, ReferencePointName);
        }
    }

    // The set of components from which a link's mass and inertia are computed.
    public enum InertialSource
    {
        Visual = 0,
        Collision = 1,
        Custom = 2,
    }

    // Where a moving joint's motion axis comes from.
    //   ReferenceAxis    - a picked SolidWorks reference axis feature (AxisName).
    //   CoordinateSystemX/Y/Z - a basis vector of the joint coordinate system.
    //   AutoDerive       - derived from the SolidWorks kinematic chain at export.
    public enum JointAxisSource
    {
        ReferenceAxis = 0,
        CoordinateSystemX = 1,
        CoordinateSystemY = 2,
        CoordinateSystemZ = 3,
        AutoDerive = 4,
    }
}
