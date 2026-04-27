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
    // A single MJCF site resolved into a link's local frame. The ExportHelper builds these from
    // SolidWorks coord-system transforms before handing them to MjcfWriter; the writer itself
    // stays SolidWorks-agnostic so it can be exercised from unit tests without the CAD runtime.
    public class MjcfSite
    {
        public string Name { get; set; }

        public double[] XYZ { get; set; }

        public double[] RPY { get; set; }

        public MjcfSite(string name, double[] xyz, double[] rpy)
        {
            Name = name;
            XYZ = xyz;
            RPY = rpy;
        }
    }
}
