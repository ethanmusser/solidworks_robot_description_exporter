using SW2RD.UI;
using System.IO;

namespace SW2RD.Export
{
    // Format-aware package layout used by ExportHelper. URDF mode uses the existing
    // ROS-friendly layout (urdf/, meshes/, launch/, config/, package.xml,
    // CMakeLists.txt). MJCF mode is much simpler — just an mjcf/ folder for the
    // model XML and a shared meshes/ folder.
    public class ExportPackage
    {
        public static IMessageBox MessageBox = new MessageBoxHelper();

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
            MessageBox.Show("Creating " + Format + " package \"" +
                PackageName + "\" at:\n" + WindowsPackageDirectory);

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
            // textures/ is created for both URDF and MJCF so per-link <texture>
            // declarations have somewhere to write their copied files. Empty when
            // no link has a texture configured -- harmless extra directory.
            if (!Directory.Exists(WindowsTexturesDirectory))
            {
                Directory.CreateDirectory(WindowsTexturesDirectory);
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
