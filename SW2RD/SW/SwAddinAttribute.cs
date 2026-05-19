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

// In-project replacement for SolidWorksTools.SwAddinAttribute.
//
// The full SwAddinAttribute lives in solidworkstools.dll, which is NOT in the
// SolidWorks api/redist/ folder and therefore is NOT on SolidWorks's
// redistribution allow-list (see redist.txt in api/redist/). Our actual use
// of the attribute is limited to applying [SwAddin(...)] in SwAddin.cs and
// reflecting back over Description / Title / LoadAtStartup in
// RegisterFunction. SolidWorks resolves the attribute by fully-qualified
// name (SolidWorksTools.SwAddinAttribute) via reflection during add-in
// discovery, so an in-project shim with matching namespace and property
// shape is functionally identical and lets us drop the solidworkstools.dll
// dependency entirely.

namespace SolidWorksTools
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class SwAddinAttribute : System.Attribute
    {
        public string Description { get; set; }

        public string Title { get; set; }

        public bool LoadAtStartup { get; set; }
    }
}
