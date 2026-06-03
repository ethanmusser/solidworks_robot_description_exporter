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
    // How a frame orientation is serialized in the exported MJCF. MuJoCo
    // accepts several mutually-exclusive orientation attributes that all
    // normalize internally to a unit quaternion; the choice is purely about
    // human readability of the emitted XML. Quaternion is the canonical
    // in-memory representation, so the writer converts to the other two at
    // emit time.
    //
    // Integer values map directly to the Setup-tab combobox item order and
    // the HKCU ExportPreferences value; the UI default is AxisAngle (index
    // 0) while the library default (the MJCFBuilder.Build optional argument)
    // stays Quaternion so existing golden-test output is byte-identical.
    internal enum MJCFRotationFormat
    {
        // axisangle="x y z a" - rotation axis plus angle (degrees).
        AxisAngle = 0,

        // quat="w x y z" - the canonical representation, no conversion.
        Quaternion = 1,

        // euler="r p y" (degrees) with <compiler eulerseq="XYZ"> so the
        // sequence matches the URDF roll-pitch-yaw (extrinsic XYZ) convention.
        Euler = 2,
    }
}
