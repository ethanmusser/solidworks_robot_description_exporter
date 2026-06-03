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

namespace SW2RD.MJCF
{
    // Units for the ANGULAR quantities written in the exported MJCF: the
    // axisangle angle, the euler triple, and hinge-joint range / ref. MuJoCo's
    // <compiler angle="..."> attribute selects the unit model-wide and defaults
    // to "degree"; we only emit the attribute when it deviates from that default
    // (i.e. for Radian). This does NOT affect length quantities (pos, slide
    // joint range/ref) or velocity-scaled quantities (damping / frictionloss),
    // which MuJoCo always interprets in radians/meters regardless of this
    // setting.
    //
    // Integer values map directly to the Setup-tab combobox item order and the
    // HKCU ExportPreferences value. The UI default and the library default are
    // both Degree, matching MuJoCo's own default so existing output is
    // unchanged unless the user opts into Radian.
    internal enum MJCFAngleUnit
    {
        // No <compiler angle> attribute is written (MuJoCo default).
        Degree = 0,

        // <compiler angle="radian"> is written and all angular quantities are
        // emitted in radians.
        Radian = 1,
    }
}
