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
using SW2RD.Core;
using SW2RD.MJCF;
using SW2RD.ROS;
using SW2RD.URDF;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Xml.Serialization;

namespace SW2RD.Export
{
    // This class contains a long list of methods that are used throughout the export process.
    // Methods for building links and joints are contained in here.
    // Many of the methods are overloaded, but seek to reduce repeated code as much as possible
    // (i.e. the overloaded methods call eachother).
    // These methods are used by ExportPropertyManager (the canonical UI surface).
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

        /// <summary>
        /// The PMP-side <see cref="WorldNode"/> root used for the most recent
        /// <see cref="CreateRobotFromTreeView"/> call. Carries world-level
        /// metadata (global-origin coord-sys, world-direct visual / collision
        /// groups, world sites) that is NOT representable in the legacy
        /// <see cref="Robot"/> graph.
        ///
        /// Null when an export is constructed via the legacy
        /// non-WorldNode-rooted path (e.g. a unit test that hand-builds a
        /// LinkNode tree without a WorldNode parent), in which case the
        /// MJCF builder synthesises an empty world from
        /// <see cref="Robot.BaseLink.Joint.CoordinateSystemName"/> via
        /// <see cref="KinematicTreeAdapter.ToCore(Robot)"/>.
        /// </summary>
        public WorldNode ActiveWorldNode { get; set; }

        public Action AxisOverlayDirectionFlipped
        { get; set; }

        public string PackageName
        { get; set; }

        public string SavePath
        { get; set; }

        public readonly List<Link> Links;

        private readonly List<string> ReferenceCoordinateSystemNames;
        private readonly List<string> ReferenceAxesNames;
        private readonly Dictionary<string, MathTransform> coordinateSystemTransformCache =
            new Dictionary<string, MathTransform>();
        private readonly Dictionary<string, double[]> referenceAxisCache =
            new Dictionary<string, double[]>();
        private int featureLookupCacheDepth;

        // Native SW DragArrowManipulator used as the PropertyManager
        // joint axis direction overlay. We use a manipulator (not raw
        // IBody2.Display3 temp bodies) because manipulators are the
        // canonical SW API for "directional gizmo arrow on top of
        // geometry": they render through other bodies regardless of
        // depth, match the look of SW's own coord-system / mate flip
        // arrows, and need no IComponent2 anchor or inverse transform
        // bookkeeping.
        // Held so ClearAxisOverlay can call Remove() on every refresh
        // and on PM close - dropping the ref without Remove leaks the
        // arrow into the user's viewport across exports.
        private Manipulator axisManipulator;

        // Monotonic sequence id labelled on every diagnostic log line emitted
        // by DrawAxisOverlay. Same purpose as ExportPropertyManager.
        // axisPreviewLogSeq - lets us pair entry / exit log lines and spot
        // duplicate fires when the manipulator path is in a suspected loop.
        // Diagnostic only.
        private int axisOverlayLogSeq;

        private bool ComputeInertialValues;
        private bool ComputeVisualCollision;
        private bool ComputeJointKinematics;
        private bool ComputeJointLimits;

        #endregion class variables

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
            using (BeginFeatureLookupCache())
            {
                ExportRobotCore(exportSTL, meshFormat, outputFormat);
            }
        }

        private void ExportRobotCore(bool exportSTL,
            MeshExportFormat meshFormat,
            ExportFormat outputFormat)
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

            // Auxiliary information that the MJCF builder needs but the URDF tree
            // does not store. We populate it as we walk the tree below.
            Dictionary<string, LinkAuxiliary> mjcfAux =
                (outputFormat == ExportFormat.MJCF)
                    ? new Dictionary<string, LinkAuxiliary>()
                    : null;

            if (outputFormat == ExportFormat.URDF)
            {
                WriteROSPackageFiles(package);
            }

            // Reap any temporary export coord systems left behind by a crashed
            // prior run before SaveSTL starts creating new ones. Safe no-op when
            // the assembly has none.
            SweepOrphanedExportFrames();

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

                // World-level geometry / sites: MJCF only. Walks the
                // WorldNode's visual / collision / site groups through
                // the same SaveSTL / ComputeSiteTransforms paths used
                // for body geometry, anchored to the world's global-
                // origin coord-sys. URDF ignores world geometry by
                // construction (already warned in
                // KinematicTreeAdapter.ToLegacyRobot), and an empty
                // / null ActiveWorldNode produces no aux entry, so
                // legacy LinkNode-rooted callers see today's exact
                // output.
                if (mjcfAux != null && ActiveWorldNode != null)
                {
                    ProcessLinkMeshes(
                        ActiveWorldNode.Link,
                        MJCFBuilder.WorldAuxKey,
                        package,
                        exportSTL,
                        meshFormat,
                        mjcfAux);
                }
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
                // Prefer the WorldNode-rooted KinematicTree path so
                // world-level <geom> / <site> / <freejoint/> support flows
                // through. Falls back to the Robot path for legacy
                // callers that didn't populate ActiveWorldNode (e.g.
                // SW-less unit tests that hand-build a Robot).
                KinematicTree mjcfTree;
                if (ActiveWorldNode != null)
                {
                    mjcfTree = KinematicTreeAdapter.ToCore(ActiveWorldNode, URDFRobot.Name);
                }
                else
                {
                    mjcfTree = KinematicTreeAdapter.ToCore(URDFRobot);
                }
                MJCFModel mjcfModel = MJCFBuilder.Build(mjcfTree, package.MJCFMeshDir, mjcfAux);
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

            logger.Info("Copying log file");
            CopyLogFile(package);

            logger.Info("Resetting STL preferences");
            ResetUserPreferences();
            progressBar.End();
        }

        public void ExportRobot(KinematicTree tree,
            bool exportSTL = true,
            MeshExportFormat meshFormat = MeshExportFormat.STL,
            ExportFormat outputFormat = ExportFormat.URDF)
        {
            URDFRobot = KinematicTreeAdapter.ToLegacyRobot(tree);
            ExportRobot(exportSTL, meshFormat, outputFormat);
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
            Dictionary<string, LinkAuxiliary> mjcfAux = null)
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

            if (!link.isFixedFrame)
            {
                ProcessLinkMeshes(link, link.Name, package, exportSTL, meshFormat, mjcfAux);
            }
        }

        // Mesh-export work for one Link. Body links are keyed by link.Name in
        // mjcfAux; the WorldNode's pseudo-link is keyed by MJCFBuilder.WorldAuxKey
        // so the MJCF builder can lift its geoms/sites directly under <worldbody>.
        private void ProcessLinkMeshes(Link link, string assetKey, ExportPackage package,
            bool exportSTL, MeshExportFormat meshFormat, Dictionary<string, LinkAuxiliary> mjcfAux)
        {
            if (link == null)
            {
                return;
            }

            // Copy the texture file (if it was specified) to the textures directory.
            // Both URDF and MJCF use the same on-disk layout (<package>/textures/);
            // URDF references it via package://, MJCF references the basename via
            // <compiler texturedir="../textures/"> in the emitted XML.
            if (!String.IsNullOrWhiteSpace(link.Visual.Material.Texture.wFilename))
            {
                if (File.Exists(link.Visual.Material.Texture.wFilename))
                {
                    // Filename is the URDF-side <texture filename=...> URI, harmless
                    // for MJCF (the MJCF builder reads wFilename and computes its
                    // own path relative to <compiler texturedir>).
                    link.Visual.Material.Texture.Filename =
                        package.TexturesDirectory + Path.GetFileName(link.Visual.Material.Texture.wFilename);
                    package.EnsureTexturesDirectory();
                    string textureSavePath =
                        package.WindowsTexturesDirectory + Path.GetFileName(link.Visual.Material.Texture.wFilename);
                    File.Copy(link.Visual.Material.Texture.wFilename, textureSavePath, true);
                }
                else
                {
                    logger.Warn("Texture file '" + link.Visual.Material.Texture.wFilename +
                        "' for link '" + link.Name + "' does not exist; skipping copy. " +
                        "The exported model will reference a missing texture.");
                }
            }

            // SolidWorks names occasionally contain "/" which would create unwanted
            // sub-directories in mesh filenames. Replace them with "_".
            string linkName = string.Equals(assetKey, MJCFBuilder.WorldAuxKey, StringComparison.Ordinal)
                ? WorldNode.DefaultName
                : (link.Name ?? "").Replace('/', '_');
            string extension = (meshFormat == MeshExportFormat.THREEDXML) ? ".3dxml" : ".STL";

            // Make sure VisualGroups / CollisionGroups are populated. For brand-
            // new links the property setter creates a single default group from
            // the legacy SWComponents list; legacy configs land in
            // MigrateLegacyComponents during deserialization. After this call
            // every populated link has at least one VisualGroup with the
            // expected components.
            link.MigrateLegacyComponents();

            List<MeshGroup> visualGroups = link.VisualGroups ?? new List<MeshGroup>();
            List<MeshGroup> collisionGroups = link.CollisionGroups ?? new List<MeshGroup>();

            // Filter out groups with no components — those would produce empty
            // STLs and dangling geom references.
            List<MeshGroup> visualGroupsToExport = new List<MeshGroup>();
            foreach (MeshGroup g in visualGroups)
            {
                if (g != null && g.Components != null && g.Components.Count > 0)
                {
                    visualGroupsToExport.Add(g);
                }
            }
            List<MeshGroup> collisionGroupsToExport = new List<MeshGroup>();
            foreach (MeshGroup g in collisionGroups)
            {
                if (g != null && g.Components != null && g.Components.Count > 0)
                {
                    collisionGroupsToExport.Add(g);
                }
            }

            // User opted into reusing visual meshes for collision. Drop any
            // collision groups so the visual-fallback path below runs.
            if (link.CollisionUsesVisual)
            {
                collisionGroupsToExport.Clear();
            }

            // Per-link MJCF auxiliary, populated as we walk the groups below.
            LinkAuxiliary aux = null;
            if (mjcfAux != null)
            {
                aux = new LinkAuxiliary();
            }

            // ---- Visual groups -----------------------------------------------
            for (int i = 0; i < visualGroupsToExport.Count; i++)
            {
                MeshGroup group = visualGroupsToExport[i];

                // Single-group case: keep the legacy "<linkname>_visual" filename
                // so existing URDF consumers and downstream tools see no diff.
                string baseName = ChooseVisualMeshBaseName(linkName, group, i, visualGroupsToExport.Count);

                string meshShort = baseName + extension;
                string meshRel = package.MeshesDirectory + meshShort;
                string meshAbs = package.WindowsMeshesDirectory + meshShort;

                if (exportSTL)
                {
                    ExportLinkMesh(link, group.Components, meshAbs, meshFormat);
                }
                group.MeshFilename = meshRel;

                if (aux != null)
                {
                    aux.VisualMeshes.Add(new MeshAssetRef
                    {
                        Name = baseName,
                        File = Path.GetFileName(meshShort),
                    });
                }
            }

            // Set the legacy single-filename slot on link.Visual to the first
            // group's filename. This keeps compat with code that reads
            // link.Visual.Geometry.Mesh.Filename directly (e.g. legacy
            // visualisations); the URDF writer overrides it per-group.
            if (visualGroupsToExport.Count > 0)
            {
                link.Visual.Geometry.Mesh.Filename = visualGroupsToExport[0].MeshFilename;
            }

            // ---- Collision groups --------------------------------------------
            // When the user did not supply any collision groups, fall back to
            // reusing the visual meshes as collision (URDF/MJCF backward-compat).
            if (collisionGroupsToExport.Count == 0)
            {
                for (int i = 0; i < visualGroupsToExport.Count; i++)
                {
                    MeshGroup vg = visualGroupsToExport[i];
                    if (aux != null)
                    {
                        aux.CollisionMeshes.Add(new MeshAssetRef
                        {
                            Name = ChooseVisualMeshBaseName(linkName, vg, i, visualGroupsToExport.Count),
                            File = Path.GetFileName(vg.MeshFilename ?? string.Empty),
                        });
                    }
                }
                if (visualGroupsToExport.Count > 0)
                {
                    link.Collision.Geometry.Mesh.Filename = visualGroupsToExport[0].MeshFilename;
                }
            }
            else
            {
                for (int i = 0; i < collisionGroupsToExport.Count; i++)
                {
                    MeshGroup group = collisionGroupsToExport[i];

                    string baseName = ChooseCollisionMeshBaseName(linkName, group, i, collisionGroupsToExport.Count);

                    string meshShort = baseName + extension;
                    string meshRel = package.MeshesDirectory + meshShort;
                    string meshAbs = package.WindowsMeshesDirectory + meshShort;

                    if (exportSTL)
                    {
                        ExportLinkMesh(link, group.Components, meshAbs, meshFormat);
                    }
                    group.MeshFilename = meshRel;

                    if (aux != null)
                    {
                        aux.CollisionMeshes.Add(new MeshAssetRef
                        {
                            Name = baseName,
                            File = Path.GetFileName(meshShort),
                        });
                    }
                }

                link.Collision.Geometry.Mesh.Filename = collisionGroupsToExport[0].MeshFilename;
            }

            if (aux != null)
            {
                aux.Sites = ComputeSiteTransforms(link);
                mjcfAux[assetKey ?? link.Name] = aux;
            }
        }

        // Builds a stable, unique base name for a visual mesh file. When there
        // is exactly one visual group we keep the historical "<link>_visual"
        // filename so existing URDFs / model viewers / scripts are unaffected.
        // Multi-group links use "<link>_<group-name>" with the group name
        // sanitised to be filesystem-safe.
        private static string ChooseVisualMeshBaseName(
            string linkName, MeshGroup group, int index, int totalGroups)
        {
            if (totalGroups == 1)
            {
                return linkName + "_visual";
            }
            string sanitised = SanitiseGroupName(group != null ? group.Name : null);
            if (string.IsNullOrEmpty(sanitised))
            {
                sanitised = "visual" + (index + 1);
            }
            return linkName + "_" + sanitised;
        }

        // Same idea as ChooseVisualMeshBaseName but for collision groups.
        private static string ChooseCollisionMeshBaseName(
            string linkName, MeshGroup group, int index, int totalGroups)
        {
            if (totalGroups == 1)
            {
                return linkName + "_collision";
            }
            string sanitised = SanitiseGroupName(group != null ? group.Name : null);
            if (string.IsNullOrEmpty(sanitised))
            {
                sanitised = "collision" + (index + 1);
            }
            return linkName + "_" + sanitised;
        }

        // Removes characters that would interfere with mesh filenames or asset
        // names in URDF/MJCF (slashes, whitespace runs, control chars).
        private static string SanitiseGroupName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    sb.Append(c);
                }
                else if (char.IsWhiteSpace(c) || c == '/' || c == '\\' || c == '.')
                {
                    sb.Append('_');
                }
                // drop anything else (parens, brackets, quotes, etc.)
            }
            return sb.ToString();
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
        private List<SiteTransform> ComputeSiteTransforms(Link link)
        {
            List<SiteTransform> result = new List<SiteTransform>();
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
                    result.Add(new SiteTransform
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

                result.Add(new SiteTransform
                {
                    Name = spec.Name,
                    Position = pos,
                    Quaternion = quat,
                });
            }
            return result;
        }

        // TODO: this path has the same bare-name resolution bug that SaveSTL
        // had (passes only the bit before "<...>" to swFileSaveAsCoordinateSystem,
        // so sub-component coord systems with non-unique names cannot be
        // disambiguated). The LocalizeLink call below masks it for URDF by
        // injecting a compensating offset into link.Visual.Origin, but that
        // compensation is wrong for any consumer that interprets the 3dxml's
        // own frame as authoritative (e.g. a hypothetical MJCF-from-3dxml
        // path). The clean fix is to mirror SaveSTL: call
        // EnsureUniqueAssemblyExportFrame, swap exportCoordSysName into
        // SetLinkSpecificSTLPreferences, and drop the LocalizeLink workaround.
        // Deferred to avoid breaking existing URDF+3dxml consumers that rely
        // on the current LocalizeLink behavior.
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
            bool isBaseLink = string.IsNullOrEmpty(linkModelName);
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

            // SaveAs's swFileSaveAsCoordinateSystem only accepts coord systems
            // visible at the active document level; a sub-component coord system
            // like "Coordinate System1 <LINK-5>" cannot be addressed unambiguously
            // (the bare "Coordinate System1" might match nothing or several
            // instances). Materialize an equivalent assembly-level coord system
            // for the duration of this SaveAs and tear it down in the finally
            // below. For coord systems already at the assembly level
            // EnsureUniqueAssemblyExportFrame returns the existing name and
            // createdTempFrame stays false (no behavior change for those links).
            // This keeps mesh export frame resolution local to the package
            // writer without changing links whose coord systems are already
            // assembly-level features.
            bool createdTempFrame;
            string exportCoordSysName = EnsureUniqueAssemblyExportFrame(link, out createdTempFrame);
            logger.Info(link.Name + ": Using coord system '" + exportCoordSysName +
                "' for SaveAs (temp=" + createdTempFrame + ")");

            try
            {
                CommonSwOperations.ShowComponents(ActiveSWModel, components);

                int saveOptions = (int)swSaveAsOptions_e.swSaveAsOptions_Silent |
                    (int)swSaveAsOptions_e.swSaveAsOptions_Copy;
                SetLinkSpecificSTLPreferences(exportCoordSysName, link.STLQualityFine, ActiveDoc);

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
            finally
            {
                if (createdTempFrame)
                {
                    DeleteTempExportFrame(exportCoordSysName);
                }
            }
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
                package.EnsureTexturesDirectory();
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
