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

using System.IO;

namespace SW2RD.Export
{
    // Format-aware package layout used by ExportHelper. URDF mode uses the existing
    // ROS-friendly layout (urdf/, meshes/, launch/, config/, package.xml,
    // CMakeLists.txt). MJCF mode is much simpler — just an mjcf/ folder for the
    // model XML and a shared meshes/ folder.
    public class ExportPackage
    {
        public ExportFormat Format { get; }
        public string PackageName { get; }

        public string MeshesDirectory { get; }
        public string TexturesDirectory { get; }
        public string ModelsDirectory { get; }
        public string ConfigDirectory { get; }
        public string LaunchDirectory { get; }

        // For MJCF, the <compiler meshdir="..."> attribute must be a path relative to
        // the location of the model XML file. Because we keep the model file in a
        // sibling `mjcf/` folder next to `meshes/`, this is "../meshes/". For URDF
        // this is unused (URDF mesh URIs are package://-prefixed via MeshesDirectory).
        public string MJCFMeshDir { get; }

        public string WindowsPackageDirectory { get; }
        public string WindowsMeshesDirectory { get; }
        public string WindowsTexturesDirectory { get; }
        public string WindowsModelsDirectory { get; }
        public string WindowsLaunchDirectory { get; }
        public string WindowsConfigDirectory { get; }
        public string WindowsCMakeLists { get; }
        public string WindowsConfigYAML { get; }

        public string ModelExtension { get; }

        public ExportPackage(string name, string dir, ExportFormat format)
        {
            Format = format;
            PackageName = name;

            char last = dir[dir.Length - 1];
            dir = (last == '\\') ? dir : dir + @"\";
            WindowsPackageDirectory = dir + name + @"\";
            WindowsMeshesDirectory = WindowsPackageDirectory + @"meshes\";
            WindowsTexturesDirectory = WindowsPackageDirectory + @"textures\";

            string packageRef = @"package://" + name + @"/";
            MeshesDirectory = packageRef + @"meshes/";
            TexturesDirectory = packageRef + @"textures/";

            switch (format)
            {
                case ExportFormat.MJCF:
                    ModelExtension = ".xml";
                    WindowsModelsDirectory = WindowsPackageDirectory + @"mjcf\";
                    ModelsDirectory = packageRef + @"mjcf/";
                    // Path written into <compiler meshdir="..."> -- must be
                    // relative to the model file (which lives in mjcf/).
                    MJCFMeshDir = @"../meshes/";
                    WindowsLaunchDirectory = null;
                    LaunchDirectory = null;
                    WindowsConfigDirectory = null;
                    ConfigDirectory = null;
                    WindowsCMakeLists = null;
                    WindowsConfigYAML = null;
                    break;

                case ExportFormat.URDF:
                default:
                    ModelExtension = ".urdf";
                    WindowsModelsDirectory = WindowsPackageDirectory + @"urdf\";
                    ModelsDirectory = packageRef + @"urdf/";
                    MJCFMeshDir = null;
                    WindowsLaunchDirectory = WindowsPackageDirectory + @"launch\";
                    LaunchDirectory = packageRef + @"launch/";
                    WindowsConfigDirectory = WindowsPackageDirectory + @"config\";
                    ConfigDirectory = packageRef + @"config/";
                    WindowsCMakeLists = WindowsPackageDirectory + @"CMakeLists.txt";
                    WindowsConfigYAML = WindowsConfigDirectory + @"joint_names_" + name + ".yaml";
                    break;
            }
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
            if (!Directory.Exists(WindowsModelsDirectory))
            {
                Directory.CreateDirectory(WindowsModelsDirectory);
            }
            if (Format == ExportFormat.URDF)
            {
                if (!Directory.Exists(WindowsLaunchDirectory))
                {
                    Directory.CreateDirectory(WindowsLaunchDirectory);
                }
                if (!Directory.Exists(WindowsConfigDirectory))
                {
                    Directory.CreateDirectory(WindowsConfigDirectory);
                }
            }
        }

        public void EnsureTexturesDirectory()
        {
            if (!Directory.Exists(WindowsTexturesDirectory))
            {
                Directory.CreateDirectory(WindowsTexturesDirectory);
            }
        }

        public void CreateCMakeLists()
        {
            if (Format != ExportFormat.URDF)
            {
                return;
            }
            using (StreamWriter file = new StreamWriter(WindowsCMakeLists))
            {
                file.WriteLine("cmake_minimum_required(VERSION 2.8.3)\r\n");
                file.WriteLine("project(" + PackageName + ")\r\n");
                file.WriteLine("find_package(catkin REQUIRED)\r\n");
                file.WriteLine("catkin_package()\r\n");
                file.WriteLine("find_package(roslaunch)\r\n");
                file.WriteLine("foreach(dir config launch meshes urdf)");
                file.WriteLine("\tinstall(DIRECTORY ${dir}/");
                file.WriteLine("\t\tDESTINATION ${CATKIN_PACKAGE_SHARE_DESTINATION}/${dir})");
                file.WriteLine("endforeach(dir)");
            }
        }

        public void CreateConfigYAML(string[] jointNames)
        {
            if (Format != ExportFormat.URDF)
            {
                return;
            }
            using (StreamWriter file = new StreamWriter(WindowsConfigYAML))
            {
                file.Write("controller_joint_names: " + "[");
                foreach (string jname in jointNames)
                {
                    file.Write("'" + jname + "', ");
                }
                file.WriteLine("]");
            }
        }
    }
}
