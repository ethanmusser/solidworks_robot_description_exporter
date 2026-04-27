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

using System.IO;

namespace SW2URDF.MJCF
{
    // Lean package layout for MJCF exports. Unlike URDFPackage this does not create ROS metadata
    // (CMakeLists, package.xml, launch files, config yaml) because MuJoCo has no equivalent
    // notion - users just point simulate at the xml file.
    public class MjcfPackage
    {
        public string PackageName { get; }

        public string WindowsPackageDirectory { get; }
        public string WindowsMeshesDirectory { get; }
        public string MeshesRelativeDirectory { get; }
        public string WindowsXmlFileName { get; }

        public MjcfPackage(string name, string dir, string meshesDirName)
        {
            PackageName = name;
            char last = dir[dir.Length - 1];
            dir = (last == '\\' || last == '/') ? dir : dir + @"\";

            WindowsPackageDirectory = dir + name + @"\";
            MeshesRelativeDirectory = string.IsNullOrWhiteSpace(meshesDirName) ? "meshes" : meshesDirName;
            WindowsMeshesDirectory = WindowsPackageDirectory + MeshesRelativeDirectory + @"\";
            WindowsXmlFileName = WindowsPackageDirectory + name + ".xml";
        }

        public void CreateDirectories()
        {
            if (!Directory.Exists(WindowsPackageDirectory))
            {
                Directory.CreateDirectory(WindowsPackageDirectory);
            }
            if (!Directory.Exists(WindowsMeshesDirectory))
            {
                Directory.CreateDirectory(WindowsMeshesDirectory);
            }
        }
    }
}
