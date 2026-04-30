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

using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.MJCF;
using SW2URDF.ROS;
using SW2URDF.URDF;
using SW2URDF.URDFExport.CSV;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Xml.Serialization;

namespace SW2URDF.URDFExport
{
    // This class contains a long list of methods that are used throughout the export process.
    // Methods for building links and joints are contained in here.
    // Many of the methods are overloaded, but seek to reduce repeated code as much as possible
    // (i.e. the overloaded methods call eachother).
    // These methods are used by the PartExportForm, the AssemblyExportForm and the PropertyManager Page
    public partial class ExportHelper
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();

        [XmlIgnore]
        public ISldWorks iSwApp = null;

        [XmlIgnore]
        private bool mBinary;

        private bool mshowInfo;
        private bool mSTLPreview;
        private bool mTranslateToPositive;
        private bool mSaveComponentsIntoOneFile;
        private int mSTLUnits;
        private int mSTLQuality;
        private double mHideTransitionSpeed;

        private UserProgressBar progressBar;

        [XmlIgnore]
        public ModelDoc2 ActiveSWModel;

        [XmlIgnore]
        public MathUtility swMath;

        [XmlIgnore]
        public Object SWMathPID
        { get; set; }

        public Robot URDFRobot
        { get; set; }

        public string PackageName
        { get; set; }

        public string SavePath
        { get; set; }

        public readonly List<Link> Links;

        private readonly List<string> ReferenceCoordinateSystemNames;
        private readonly List<string> ReferenceAxesNames;

        private bool ComputeInertialValues;
        private bool ComputeVisualCollision;
        private bool ComputeJointKinematics;
        private bool ComputeJointLimits;

        #endregion class variables

        // Constructor for SW2URDF Exporter class
        public ExportHelper(SldWorks iSldWorksApp)
        {
            ConstructExporter(iSldWorksApp);
            iSwApp.GetUserProgressBar(out progressBar);

            SavePath = System.Environment.ExpandEnvironmentVariables("%HOMEDRIVE%%HOMEPATH%");
            PackageName = ActiveSWModel.GetTitle();

            ReferenceCoordinateSystemNames = FindRefGeoNames("CoordSys");
            ReferenceAxesNames = FindRefGeoNames("RefAxis");

            ComputeInertialValues = true;
            ComputeVisualCollision = true;
            ComputeJointKinematics = true;
            ComputeJointLimits = true;
        }

        public void SetComputeInertial(bool computeInertial)
        {
            ComputeInertialValues = computeInertial;
        }

        public void SetComputeVisualCollision(bool computeVisual)
        {
            ComputeVisualCollision = computeVisual;
        }

        public void SetComputeJointKinematics(bool computeKinematics)
        {
            ComputeJointKinematics = computeKinematics;
        }

        public void SetComputeJointLimits(bool computeJointLimits)
        {
            ComputeJointLimits = computeJointLimits;
        }

        private void ConstructExporter(SldWorks iSldWorksApp)
        {
            iSwApp = iSldWorksApp;
            ActiveSWModel = (ModelDoc2)iSwApp.ActiveDoc;
            swMath = iSwApp.GetMathUtility();
        }

        #region Export Methods

        // Beginning method for exporting the full package. Defaults preserve the
        // original URDF behavior so existing call sites continue to work unchanged.
        public void ExportRobot(bool exportSTL = true,
            MeshExportFormat meshFormat = MeshExportFormat.STL,
            ExportFormat outputFormat = ExportFormat.URDF)
        {
            //Setting up the progress bar
            logger.Info("Beginning the export process (format: " + outputFormat + ")");
            int progressBarBound = CommonSwOperations.GetCount(URDFRobot.BaseLink);
            iSwApp.GetUserProgressBar(out progressBar);
            progressBar.Start(0, progressBarBound, "Creating package directories");

            //Creating package directories
            logger.Info("Creating package directories with name " + PackageName + " and save path " + SavePath);
            ExportPackage package = new ExportPackage(PackageName, SavePath, outputFormat);
            package.CreateDirectories();
            URDFRobot.Name = PackageName;

            string windowsModelFileName = package.WindowsModelsDirectory + URDFRobot.Name + package.ModelExtension;
            string windowsCSVFileName = package.WindowsModelsDirectory + URDFRobot.Name + ".csv";

            // Auxiliary information that the MJCF builder needs but the URDF tree
            // does not store. We populate it as we walk the tree below.
            Dictionary<string, MJCFBuilder.LinkAuxiliary> mjcfAux =
                (outputFormat == ExportFormat.MJCF)
                    ? new Dictionary<string, MJCFBuilder.LinkAuxiliary>()
                    : null;

            if (outputFormat == ExportFormat.URDF)
            {
                WriteROSPackageFiles(package);
            }

            //Customizing STL preferences to how I want them
            logger.Info("Saving existing STL preferences");
            SaveUserPreferences();

            logger.Info("Modifying STL preferences");
            SetSTLExportPreferences();

            //Saving part as STL mesh
            AssemblyDoc assyDoc = (AssemblyDoc)ActiveSWModel;
            List<string> hiddenComponents = CommonSwOperations.FindHiddenComponents(assyDoc.GetComponents(false));
            logger.Info("Found " + hiddenComponents.Count + " hidden components " + String.Join(", ", hiddenComponents));
            logger.Info("Hiding all components");
            ActiveSWModel.Extension.SelectAll();
            ActiveSWModel.HideComponent2();

            bool success = false;
            try
            {
                logger.Info("Beginning individual files export");
                ExportFiles(URDFRobot.BaseLink, package, 0, exportSTL, meshFormat, mjcfAux);
                success = true;
            }
            catch (Exception e)
            {
                logger.Error("An exception was thrown attempting to export the model", e);
            }
            finally
            {
                logger.Info("Showing all components except previously hidden components");
                CommonSwOperations.ShowAllComponents(ActiveSWModel, hiddenComponents);

                logger.Info("Resetting STL preferences");
                ResetUserPreferences();
            }

            if (!success)
            {
                MessageBox.Show("Exporting the model failed unexpectedly. Email your maintainer " +
                    "with the log file found at " + Logger.GetFileName());
                return;
            }

            if (outputFormat == ExportFormat.MJCF)
            {
                logger.Info("Writing MJCF file to " + windowsModelFileName);
                MJCFModel mjcfModel = MJCFBuilder.Build(URDFRobot, package.MJCFMeshDir, mjcfAux);
                MJCFWriter mjcfWriter = new MJCFWriter(windowsModelFileName);
                try
                {
                    mjcfModel.WriteMJCF(mjcfWriter.writer);
                }
                finally
                {
                    mjcfWriter.Close();
                }
            }
            else
            {
                logger.Info("Writing URDF file to " + windowsModelFileName);
                URDFWriter uWriter = new URDFWriter(windowsModelFileName);
                URDFRobot.WriteURDF(uWriter.writer);
            }

            ImportExport.WriteRobotToCSV(URDFRobot, windowsCSVFileName);

            logger.Info("Copying log file");
            CopyLogFile(package);

            logger.Info("Resetting STL preferences");
            ResetUserPreferences();
            progressBar.End();
        }

        // ROS-specific package files (CMakeLists, package.xml, launch files, joint
        // names YAML). Only emitted for the URDF output format.
        private void WriteROSPackageFiles(ExportPackage package)
        {
            string windowsPackageXMLFileName = package.WindowsPackageDirectory + "package.xml";

            logger.Info("Creating CMakeLists.txt at " + package.WindowsCMakeLists);
            package.CreateCMakeLists();

            logger.Info("Creating joint names config at " + package.WindowsConfigYAML);
            package.CreateConfigYAML(URDFRobot.GetJointNames(false));

            logger.Info("Creating package.xml at " + windowsPackageXMLFileName);
            PackageXMLWriter packageXMLWriter = new PackageXMLWriter(windowsPackageXMLFileName);
            PackageXML packageXML = new PackageXML(PackageName);
            packageXML.WriteElement(packageXMLWriter);

            Rviz rviz = new Rviz(PackageName, URDFRobot.Name + package.ModelExtension);
            logger.Info("Creating RVIZ launch file in " + package.WindowsLaunchDirectory);
            rviz.WriteFiles(package.WindowsLaunchDirectory);

            Gazebo gazebo = new Gazebo(URDFRobot.Name, PackageName, URDFRobot.Name + package.ModelExtension);
            logger.Info("Creating Gazebo launch file in " + package.WindowsLaunchDirectory);
            gazebo.WriteFile(package.WindowsLaunchDirectory);
        }

        public List<string> GetJointNames()
        {
            List<string> jointNames = new List<string>();

            Queue<Link> queue = new Queue<Link>();
            queue.Enqueue(URDFRobot.BaseLink);
            while (queue.Count > 0)
            {
                Link current = queue.Dequeue();
                if (current.Parent != null)
                {
                    jointNames.Add(current.Joint.Name);
                }

                foreach (Link child in current.Children)
                {
                    queue.Enqueue(child);
                }
            }

            return jointNames;
        }

        //Recursive method for exporting each link's mesh files. Splits visual and
        // collision into separate STL passes so the URDF (and the new MJCF) can
        // reference distinct meshes.
        private void ExportFiles(Link link, ExportPackage package, int count,
            bool exportSTL = true,
            MeshExportFormat meshFormat = MeshExportFormat.STL,
            Dictionary<string, MJCFBuilder.LinkAuxiliary> mjcfAux = null)
        {
            progressBar.UpdateProgress(count);
            progressBar.UpdateTitle("Exporting mesh: " + link.Name);
            logger.Info("Exporting link: " + link.Name);
            logger.Info("Link " + link.Name + " has " + link.Children.Count + " children");
            foreach (Link child in link.Children)
            {
                count += 1;
                if (!child.isFixedFrame)
                {
                    ExportFiles(child, package, count, exportSTL, meshFormat, mjcfAux);
                }
            }

            // Copy the texture file (if it was specified) to the textures directory.
            // Only the URDF package layout includes a textures directory.
            if (!link.isFixedFrame &&
                package.Format == ExportFormat.URDF &&
                !String.IsNullOrWhiteSpace(link.Visual.Material.Texture.wFilename))
            {
                if (File.Exists(link.Visual.Material.Texture.wFilename))
                {
                    link.Visual.Material.Texture.Filename =
                        package.TexturesDirectory + Path.GetFileName(link.Visual.Material.Texture.wFilename);
                    string textureSavePath =
                        package.WindowsTexturesDirectory + Path.GetFileName(link.Visual.Material.Texture.wFilename);
                    File.Copy(link.Visual.Material.Texture.wFilename, textureSavePath, true);
                }
            }

            // SolidWorks names occasionally contain "/" which would create unwanted
            // sub-directories in mesh filenames. Replace them with "_".
            string linkName = link.Name.Replace('/', '_');
            string extension = (meshFormat == MeshExportFormat.THREEDXML) ? ".3dxml" : ".STL";

            // Visual pass — uses link.VisualComponents.
            string visualMeshShort = linkName + "_visual" + extension;
            string visualMeshRel = package.MeshesDirectory + visualMeshShort;
            string visualMeshAbs = package.WindowsMeshesDirectory + visualMeshShort;

            // Collision pass — uses link.CollisionComponents (falls back to visual
            // when the user didn't pick a separate set, matching the legacy behavior
            // where the URDF visual mesh doubled as collision).
            List<Component2> visualComponents = link.VisualComponents ?? link.SWComponents ?? new List<Component2>();
            List<Component2> collisionComponents = link.CollisionComponents ?? new List<Component2>();
            bool hasDistinctCollision = collisionComponents.Count > 0;

            string collisionMeshShort = hasDistinctCollision
                ? linkName + "_collision" + extension
                : visualMeshShort;
            string collisionMeshRel = hasDistinctCollision
                ? package.MeshesDirectory + collisionMeshShort
                : visualMeshRel;
            string collisionMeshAbs = hasDistinctCollision
                ? package.WindowsMeshesDirectory + collisionMeshShort
                : visualMeshAbs;

            if (exportSTL && visualComponents.Count > 0)
            {
                ExportLinkMesh(link, visualComponents, visualMeshAbs, meshFormat);
            }
            if (exportSTL && hasDistinctCollision)
            {
                ExportLinkMesh(link, collisionComponents, collisionMeshAbs, meshFormat);
            }

            link.Visual.Geometry.Mesh.Filename = visualMeshRel;
            link.Collision.Geometry.Mesh.Filename = collisionMeshRel;

            // For MJCF, the asset dictionary references each mesh by a name
            // (independent of the file path). Only emit a mesh entry when there is
            // an actual STL on disk for the role.
            if (mjcfAux != null && !link.isFixedFrame)
            {
                MJCFBuilder.LinkAuxiliary aux = new MJCFBuilder.LinkAuxiliary();
                if (visualComponents.Count > 0)
                {
                    aux.VisualMeshName = linkName + "_visual";
                    aux.VisualMeshFile = Path.GetFileName(visualMeshShort);
                }
                if (hasDistinctCollision)
                {
                    aux.CollisionMeshName = linkName + "_collision";
                    aux.CollisionMeshFile = Path.GetFileName(collisionMeshShort);
                }
                else if (visualComponents.Count > 0)
                {
                    // Reuse the visual mesh as the collision geom so the body still
                    // participates in physics (matches URDF backward-compat).
                    aux.CollisionMeshName = linkName + "_visual";
                    aux.CollisionMeshFile = Path.GetFileName(visualMeshShort);
                }
                aux.Sites = ComputeSiteTransforms(link);
                mjcfAux[link.Name] = aux;
            }
        }

        // Single-pass mesh export for a specified component subset. Hides everything
        // but the requested components, exports, and restores state. The choice of
        // STL vs 3dxml is driven by `meshFormat`; the resulting file is always saved
        // in body-local coordinates (which matters for transform consistency between
        // URDF/MJCF outputs).
        private void ExportLinkMesh(Link link, List<Component2> components,
            string windowsMeshFilename, MeshExportFormat meshFormat)
        {
            switch (meshFormat)
            {
                case MeshExportFormat.STL:
                    SaveSTL(link, windowsMeshFilename, components);
                    break;
                case MeshExportFormat.THREEDXML:
                    Save3dxml(link, windowsMeshFilename, components);
                    break;
                default:
                    SaveSTL(link, windowsMeshFilename, components);
                    break;
            }
        }

        // Computes pos/quat for each <site> spec attached to the link. Sites live in
        // the parent body's local frame, so the transform we want is
        //     T = (joint_global)^(-1) * (site_global)
        // i.e. the same construction used for joint origins, but with the site's
        // coordinate system in place of the child's joint coordinate system.
        private List<MJCFBuilder.SiteTransform> ComputeSiteTransforms(Link link)
        {
            List<MJCFBuilder.SiteTransform> result = new List<MJCFBuilder.SiteTransform>();
            if (link.Sites == null || link.Sites.Count == 0)
            {
                return result;
            }
            // The body frame is attached to link.Joint.CoordinateSystemName for non-base
            // links and to whatever the user chose for the base link.
            string parentCoordSys = link.Joint != null ? link.Joint.CoordinateSystemName : null;
            if (string.IsNullOrEmpty(parentCoordSys))
            {
                logger.Warn("Cannot compute site transforms for link " + link.Name +
                    " because its body coordinate system is not set");
                return result;
            }

            MathTransform parentTransform = GetCoordinateSystemTransform(parentCoordSys);
            if (parentTransform == null)
            {
                logger.Warn("Failed to resolve parent coordinate system " +
                    parentCoordSys + " when computing site transforms for link " + link.Name);
                return result;
            }
            Matrix<double> parentMat = MathOps.GetTransformation(parentTransform);
            Matrix<double> parentInv = parentMat.Inverse();

            foreach (SiteSpec spec in link.Sites)
            {
                if (string.IsNullOrEmpty(spec.CoordinateSystemName))
                {
                    logger.Warn("Site " + spec.Name + " on link " + link.Name +
                        " has no coordinate system; using parent body frame as identity.");
                    result.Add(new MJCFBuilder.SiteTransform
                    {
                        Name = spec.Name,
                        Position = new double[] { 0, 0, 0 },
                        Quaternion = new double[] { 1, 0, 0, 0 },
                    });
                    continue;
                }

                MathTransform siteTransform = GetCoordinateSystemTransform(spec.CoordinateSystemName);
                if (siteTransform == null)
                {
                    logger.Warn("Failed to resolve site coordinate system " +
                        spec.CoordinateSystemName + " for link " + link.Name);
                    continue;
                }
                Matrix<double> siteMat = MathOps.GetTransformation(siteTransform);
                Matrix<double> local = parentInv * siteMat;

                double[] pos = MathOps.GetXYZ(local);
                pos = MathOps.Threshold(pos, 0.00001);
                double[] quat = MathOps.RotationMatrixToQuaternion(local);

                result.Add(new MJCFBuilder.SiteTransform
                {
                    Name = spec.Name,
                    Position = pos,
                    Quaternion = quat,
                });
            }
            return result;
        }

        private void Save3dxml(Link link, string windowsMeshFilename, List<Component2> components)
        {
            int errors = 0;
            int warnings = 0;

            string coordsysName = link.Joint.CoordinateSystemName;

            logger.Info(link.Name + ": Exporting 3dxml with coordinate frame " + coordsysName);

            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
            ModelDoc2 ActiveDoc = ActiveSWModel;

            logger.Info(link.Name + ": Reference geometry name " + names["component"]);

            CommonSwOperations.ShowComponents(ActiveSWModel, components);

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            SetLinkSpecificSTLPreferences(names["geo"], link.STLQualityFine, ActiveDoc);

            logger.Info("Saving 3dxml to " + windowsMeshFilename);

            // === 3dxml Localize Link === //

            // Remove suffix from coordinate-system name.
            // ex. "Joint Origin <Arm_link-1>" -> "Joint Origin"
            // Suffix is included when coordinate is inside sub-assembly.
            string linkModelName = names["component"];
            string linkModelSuffix = " <" + linkModelName + ">";
            if(coordsysName.Contains(linkModelSuffix))
            {
                coordsysName = coordsysName.Replace(linkModelSuffix, "");
                logger.Info($"Suffix of {linkModelName} was removed from coordsysName : {coordsysName}");
            }

            // Get the model document of the link.
            ModelDoc2 linkModel;
            bool isBaseLink = linkModelName == "";
            if (isBaseLink)
            {
                linkModel = ActiveDoc;
            }
            else
            {
                if (link.SWMainComponent != null)
                {
                    linkModel = link.SWMainComponent.GetModelDoc2();
                }
                else
                {
                    logger.Warn("Could not get linkModel because SWMainComponent was null");
                    linkModel = null;
                }
            }

            // Localize the link to the certain place.
            if (linkModel != null)
            {
                MathTransform coordSysTransform =
                    linkModel.Extension.GetCoordinateSystemTransformByName(coordsysName);
                if (coordSysTransform != null)
                {
                    logger.Info("Localizing Link : " + coordsysName);
                    Matrix<double> GlobalTransform = MathOps.GetTransformation(coordSysTransform);
                    LocalizeLink(link, GlobalTransform);
                }
                else
                {
                    logger.Warn("coordSysTransform was null : " + coordsysName);
                }
            }
            else
            { 
                logger.Warn("Link model was null.");
            }
            // === 3dxml Localize Link === //

            ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null, ref errors, ref warnings);

            if (errors + warnings != 0)
            {
                logger.Warn("Exporting 3dxml for link " + link.Name + " failed with error " + errors +
                    " or warnings " + warnings);
            }
            CommonSwOperations.HideComponents(ActiveSWModel, components);
        }

        private bool SaveSTL(Link link, string windowsMeshFilename, List<Component2> components)
        {
            int errors = 0;
            int warnings = 0;

            string coordsysName = link.Joint.CoordinateSystemName;

            logger.Info(link.Name + ": Exporting STL with coordinate frame " + coordsysName);

            Dictionary<string, string> names = GetComponentRefGeoNames(coordsysName);
            ModelDoc2 ActiveDoc = ActiveSWModel;

            logger.Info(link.Name + ": Reference geometry name " + names["component"]);

            CommonSwOperations.ShowComponents(ActiveSWModel, components);

            int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
            SetLinkSpecificSTLPreferences(names["geo"], link.STLQualityFine, ActiveDoc);

            logger.Info("Saving STL to " + windowsMeshFilename);
            ActiveDoc.Extension.SaveAs(windowsMeshFilename,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, saveOptions, null, ref errors, ref warnings);
            if (errors + warnings != 0)
            {
                logger.Warn("Exporting STL for link " + link.Name + " failed with error " + errors + 
                    " or warnings " + warnings);
            }
            CommonSwOperations.HideComponents(ActiveSWModel, components);

            bool success = CorrectSTLMesh(windowsMeshFilename);
            if (!success)
            {
                logger.Warn("There was an issue exporting the STL for " + link.Name + ". It " +
                    "may not be readable by CAD programs that aren't SolidWorks");
            }
            return success;
        }

        public void ExportLink(bool zIsUp)
        {
            CreateBaseRefOrigin(zIsUp);
            MathTransform coordSysTransform =
                ActiveSWModel.Extension.GetCoordinateSystemTransformByName("Origin_global");
            Matrix<double> GlobalTransform = MathOps.GetTransformation(coordSysTransform);

            LocalizeLink(URDFRobot.BaseLink, GlobalTransform);

            //Creating package directories
            ExportPackage package = new ExportPackage(PackageName, SavePath, ExportFormat.URDF);
            package.CreateDirectories();
            string meshFileName = package.MeshesDirectory + URDFRobot.BaseLink.Name + ".STL";
            string windowsMeshFileName = package.WindowsMeshesDirectory + URDFRobot.BaseLink.Name + ".STL";
            string windowsURDFFileName = package.WindowsModelsDirectory + URDFRobot.Name + ".urdf";
            string windowsManifestFileName = package.WindowsPackageDirectory + "manifest.xml";

            //Creating manifest file
            PackageXMLWriter manifestWriter = new PackageXMLWriter(windowsManifestFileName);
            PackageXML Manifest = new PackageXML(URDFRobot.Name);
            Manifest.WriteElement(manifestWriter);

            //Customizing STL preferences to how I want them
            SaveUserPreferences();
            SetSTLExportPreferences();
            SetLinkSpecificSTLPreferences("", URDFRobot.BaseLink.STLQualityFine, ActiveSWModel);
            int errors = 0;
            int warnings = 0;

            //Saving part as STL mesh

            ActiveSWModel.Extension.SaveAs(windowsMeshFileName, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref errors, ref warnings);
            URDFRobot.BaseLink.Visual.Geometry.Mesh.Filename = meshFileName;
            URDFRobot.BaseLink.Collision.Geometry.Mesh.Filename = meshFileName;

            URDFRobot.BaseLink.Visual.Material.Texture.Filename =
                package.TexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
            string textureSavePath =
                package.WindowsTexturesDirectory + Path.GetFileName(URDFRobot.BaseLink.Visual.Material.Texture.wFilename);
            if (!String.IsNullOrWhiteSpace(URDFRobot.BaseLink.Visual.Material.Texture.wFilename))
            {
                File.Copy(URDFRobot.BaseLink.Visual.Material.Texture.wFilename, textureSavePath, true);
            }

            //Writing URDF to file
            URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
            //mRobot.addLink(mLink);
            URDFRobot.WriteURDF(uWriter.writer);

            ResetUserPreferences();
        }

        //Writes an empty header to the STL to get rid of the BS that SolidWorks adds to a binary STL file
        public static bool CorrectSTLMesh(string filename)
        {
            logger.Info("Removing SW header in STL file");
            try
            {
                using (FileStream fileStream = new FileStream(filename, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    byte[] emptyHeader = new byte[80];
                    fileStream.Write(emptyHeader, 0, emptyHeader.Length);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Correcting the STL " + filename + " failed. This STL may not be " +
                    "readable by ROS or other CAD programs", e);
                return false;
            }
            return true;
        }

        #endregion Export Methods

        private static void CopyLogFile(ExportPackage package)
        {
            string destination = package.WindowsPackageDirectory + "export.log";
            string log_filename = Logger.GetFileName();

            if (log_filename != null)
            {
                if (!File.Exists(log_filename))
                {
                    System.Windows.Forms.MessageBox.Show("The log file was expected to be located at " + log_filename +
                        ", but it was not found. Please contact your maintainer with this error message.");
                }
                else
                {
                    logger.Info("Copying " + log_filename + " to " + destination);
                    File.Copy(log_filename, destination, true);
                }
            }
        }

        #region STL Preference shuffling

        //Saves the preferences that the user had setup so that I can change them and revert back to their configuration
        private void SaveUserPreferences()
        {
            logger.Info("Saving users preferences");
            mBinary = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat);
            mTranslateToPositive = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive);
            mSTLUnits = iSwApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits);
            mSTLQuality = iSwApp.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality);
            mshowInfo = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave);
            mSTLPreview = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview);
            mHideTransitionSpeed = iSwApp.GetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent);
            mSaveComponentsIntoOneFile = iSwApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile);
        }

        //This is how the STL export preferences need to be to properly export
        private void SetSTLExportPreferences()
        {
            logger.Info("Setting STL preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, true);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, true);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, 2);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Coarse);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, false);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, false);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, 0);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, true);
        }

        //This resets the user preferences back to what they were.
        private void ResetUserPreferences()
        {
            logger.Info("Returning STL preferences to user preferences");
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLBinaryFormat, mBinary);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLDontTranslateToPositive, mTranslateToPositive);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swExportStlUnits, mSTLUnits);
            iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, mSTLQuality);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLShowInfoOnSave, mshowInfo);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLPreview, mSTLPreview);
            iSwApp.SetUserPreferenceDoubleValue((int)swUserPreferenceDoubleValue_e.swViewTransitionHideShowComponent, mHideTransitionSpeed);
            iSwApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swSTLComponentsIntoOneFile, mSaveComponentsIntoOneFile);
        }

        //If the user selected something specific for a particular link, that is handled here.
        private void SetLinkSpecificSTLPreferences(string CoordinateSystemName, bool qualityFine, ModelDoc2 doc)
        {
            doc.Extension.SetUserPreferenceString((int)swUserPreferenceStringValue_e.swFileSaveAsCoordinateSystem,
                (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified, CoordinateSystemName);
            if (qualityFine)
            {
                iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Fine);
            }
            else
            {
                iSwApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swSTLQuality, (int)swSTLQuality_e.swSTLQuality_Coarse);
            }
        }

        #endregion STL Preference shuffling
    }
}
