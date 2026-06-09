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
using SW2RD.Input;
using SW2RD.UI;
using SW2RD.URDF;
using SW2RD.Utilities;
using SW2RD.Validation;
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
        // When set, WithComponentConfiguration reads in the part doc's
        // CURRENT active configuration instead of switching to the
        // component's referenced configuration. ShowConfiguration2 mutates
        // (and rebuilds) the part document; doing that while the export
        // PropertyManager page is open closes/crashes the page. The live
        // axis/coord-sys preview (PreviewAxisDirection) is contractually
        // side-effect-free, so it sets this flag for its duration. The
        // export pipeline leaves it false so config-dependent geometry is
        // resolved in the correct configuration.
        private bool suppressConfigSwitchForFeatureLookup;

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

        // Experimental per-part tessellation mesh export (Part A). When true,
        // SaveSTL reads body tessellation directly at a uniform, display-
        // independent tolerance and transforms it into the link frame, instead
        // of the whole-assembly SaveAs that requires hiding every non-member
        // component. Set from ExportPreferences.GetFastMeshExport() by the PMP
        // before ExportRobot. Default false keeps the proven SaveAs path.
        public bool UseTessellationMeshExport { get; set; }

        // Mesh quality level for the tessellation path: 0=Coarse, 1=Medium,
        // 2=Fine, 3=Very fine. Set from ExportPreferences.GetMeshQuality() by
        // the PMP before ExportRobot. Default Fine (2). Maps to a per-body
        // relative chord tolerance (fraction of the body's own bbox diagonal)
        // plus an angle tolerance - see MeshQualityToTolerances.
        public int MeshQualityLevel { get; set; } = 2;

        // How MJCF frame orientations are serialized (axisangle / quat / euler).
        // Set from ExportPreferences.GetRotationFormat() by the PMP before
        // ExportRobot. Default Axis-angle (the most readable). URDF output
        // ignores this entirely.
        internal MJCF.MJCFRotationFormat MJCFRotationFormat { get; set; } =
            MJCF.MJCFRotationFormat.AxisAngle;

        // Angular unit for MJCF output (degrees / radians). Set from
        // ExportPreferences.GetAngleUnit() by the PMP before ExportRobot.
        // Default Degree (MuJoCo's own default, so no <compiler angle> is
        // written). URDF output ignores this entirely (always radians).
        internal MJCF.MJCFAngleUnit MJCFAngleUnit { get; set; } =
            MJCF.MJCFAngleUnit.Degree;

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
        // Returns true when the export wrote its output successfully; false when
        // it bailed after surfacing its own failure dialog. Callers that don't
        // care about success (e.g. the SW-attached unit tests) simply ignore it.
        public bool ExportRobot(bool exportSTL = true,
            MeshExportFormat meshFormat = MeshExportFormat.STL,
            ExportFormat outputFormat = ExportFormat.URDF)
        {
            using (BeginFeatureLookupCache())
            {
                return ExportRobotCore(exportSTL, meshFormat, outputFormat);
            }
        }

        // True if any link in the robot (body tree + world node) carries at
        // least one visual or collision mesh group with components, i.e. the
        // export will actually write an STL. Used to skip the expensive
        // whole-assembly hide/show when there is nothing to write. Mirrors the
        // group membership ProcessLinkMeshes iterates, so it never reports a
        // mesh the export pipeline would not emit.
        private bool RobotHasExportableMesh()
        {
            if (LinkSubtreeHasMesh(URDFRobot?.BaseLink))
            {
                return true;
            }
            if (ActiveWorldNode != null && LinkHasMeshComponents(ActiveWorldNode.Link))
            {
                return true;
            }
            return false;
        }

        private static bool LinkSubtreeHasMesh(Link link)
        {
            if (link == null)
            {
                return false;
            }
            if (LinkHasMeshComponents(link))
            {
                return true;
            }
            if (link.Children != null)
            {
                foreach (Link child in link.Children)
                {
                    if (LinkSubtreeHasMesh(child))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool LinkHasMeshComponents(Link link)
        {
            if (link == null)
            {
                return false;
            }
            link.MigrateLegacyComponents();
            return GroupsHaveComponents(link.VisualGroups) || GroupsHaveComponents(link.CollisionGroups);
        }

        private static bool GroupsHaveComponents(List<MeshGroup> groups)
        {
            if (groups == null)
            {
                return false;
            }
            foreach (MeshGroup group in groups)
            {
                if (group != null && group.Components != null && group.Components.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private bool ExportRobotCore(bool exportSTL,
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

            // Auxiliary information that the writers need but the canonical tree
            // does not store: per-link mesh asset refs (MJCF) and per-site
            // body-local transforms (both formats). We populate it as we walk the
            // tree below. Built for both URDF and MJCF now that URDF emits sites
            // as empty link + fixed joint frames; URDF simply ignores the mesh
            // refs (its mesh URIs are stamped onto MeshGroupModel directly).
            Dictionary<string, LinkAuxiliary> auxByLinkName =
                new Dictionary<string, LinkAuxiliary>();

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
            // The hide-everything / show-only-this-link's-components pattern below
            // is what keeps each per-link STL clean (the assembly STL SaveAs in
            // SaveSTL exports all VISIBLE geometry, so every non-member component
            // must be hidden). Hiding lightweight components does NOT resolve them,
            // and the per-link SaveSTL only makes the link's already-resolved
            // components visible, so unused components stay lightweight + hidden
            // and are never loaded - that is what makes a sparse export of a large
            // assembly fast now that the up-front full resolve is gone (see
            // ExportPropertyManager.ResolveUsedComponents).
            //
            // Two cost controls wrap the hide / show because it scales with TOTAL
            // component count, not with how much geometry is exported:
            //   (1) We ONLY hide / show when at least one STL will actually be
            //       written (exportSTL AND some link has a non-empty visual /
            //       collision group). A default export with no geometry selected -
            //       or one with "Export Meshes" unchecked - writes no STL, so the
            //       whole-assembly SelectAll + HideComponent2 + ShowAllComponents
            //       graphics churn (minutes on a large lightweight assembly) is
            //       pure waste and is skipped. ExportFiles still runs to populate
            //       auxByLinkName (mesh refs + sites); its per-link ExportLinkMesh call
            //       is itself gated on exportSTL + group membership, so it is a
            //       cheap no-op when there is nothing to write.
            //   (2) When we DO hide / show, viewport graphics updates are suppressed
            //       for the duration. STL tessellation reads model visibility, not
            //       the rendered view, so suppressing per-operation redraws is safe
            //       and avoids SW redrawing / reloading graphics for every component
            //       on every hide / show. A single GraphicsRedraw2 restores the
            //       view at the end.
            // If this ever still dominates, the next lever is to SUPPRESS the
            // unused components for the export (suppressed components emit no
            // geometry, so the hide-all becomes unnecessary) and restore them
            // afterward - deferred as higher-risk than the current approach.
            AssemblyDoc assyDoc = (AssemblyDoc)ActiveSWModel;
            // The tessellation mesh path (Part A) reads each component's bodies
            // directly regardless of visibility, so it needs NO whole-assembly
            // hide/show at all - that is the entire point (it eliminates the
            // graphics purge + reload that dominates the export). It ONLY writes
            // STL, though: 3DXML still goes through the legacy Save3dxml SaveAs,
            // which exports VISIBLE geometry and therefore REQUIRES the hide-all.
            // So tessellation (and the hide/show skip) applies only when the
            // chosen format is STL - if the user checks "Fast mesh export" but
            // picks 3DXML, we must keep the hide/show or every link's 3DXML would
            // capture the whole visible assembly.
            bool useTessellation =
                UseTessellationMeshExport && meshFormat == MeshExportFormat.STL;
            bool willExportAnyMesh =
                exportSTL && RobotHasExportableMesh() && !useTessellation;

            List<string> hiddenComponents = null;
            ModelView activeView = ActiveSWModel.ActiveView as ModelView;
            bool priorGraphicsUpdate = true;
            if (willExportAnyMesh)
            {
                if (activeView != null)
                {
                    priorGraphicsUpdate = activeView.EnableGraphicsUpdate;
                    activeView.EnableGraphicsUpdate = false;
                }

                hiddenComponents = CommonSwOperations.FindHiddenComponents(assyDoc.GetComponents(false));
                logger.Info("Found " + hiddenComponents.Count + " hidden components " + String.Join(", ", hiddenComponents));
                logger.Info("Hiding all components");
                ActiveSWModel.Extension.SelectAll();
                ActiveSWModel.HideComponent2();
            }
            else
            {
                logger.Info("No STL will be written (no geometry selected or mesh export disabled); " +
                    "skipping the whole-assembly hide/show.");
            }

            bool success = false;
            try
            {
                logger.Info("Beginning individual files export");
                ExportFiles(URDFRobot.BaseLink, package, 0, exportSTL, meshFormat, auxByLinkName);

                // World-level geometry / sites: MJCF only. Walks the
                // WorldNode's visual / collision / site groups through
                // the same SaveSTL / ComputeSiteTransforms paths used
                // for body geometry, anchored to the world's global-
                // origin coord-sys. URDF drops world-level geometry /
                // sites by construction (already warned in URDFBuilder),
                // and an empty / null ActiveWorldNode produces no aux
                // entry, so legacy LinkNode-rooted callers see today's
                // exact output.
                if (outputFormat == ExportFormat.MJCF && ActiveWorldNode != null)
                {
                    ProcessLinkMeshes(
                        ActiveWorldNode.Link,
                        MJCFBuilder.WorldAuxKey,
                        package,
                        exportSTL,
                        meshFormat,
                        auxByLinkName);
                }
                success = true;
            }
            catch (Exception e)
            {
                logger.Error("An exception was thrown attempting to export the model", e);
            }
            finally
            {
                if (willExportAnyMesh)
                {
                    // Timing split: the restore (ShowAllComponents) and the
                    // viewport refresh (GraphicsRedraw2) both reload graphics for
                    // the ~components the hide-all purged, so historically the
                    // single "Showing..." -> "Resetting..." gap conflated the two.
                    // Log each independently so we can see which one dominates.
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    logger.Info("Showing all components except previously hidden components");
                    CommonSwOperations.ShowAllComponents(ActiveSWModel, hiddenComponents);
                    logger.Info("ShowAllComponents took " + sw.ElapsedMilliseconds + " ms");

                    if (activeView != null)
                    {
                        activeView.EnableGraphicsUpdate = priorGraphicsUpdate;
                        sw.Restart();
                        ActiveSWModel.GraphicsRedraw2();
                        logger.Info("GraphicsRedraw2 took " + sw.ElapsedMilliseconds + " ms");
                    }
                }

                logger.Info("Resetting STL preferences");
                ResetUserPreferences();
            }

            if (!success)
            {
                UserNotifier.Show("Exporting the model failed unexpectedly. Email your maintainer " +
                    "with the log file found at " + Logger.GetFileName());
                return false;
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
                MaybeCaptureGoldenSnapshot(SnapshotReplay.MjcfFormat, windowsModelFileName, mjcfTree,
                    package.MJCFMeshDir, auxByLinkName);
                MJCFModel mjcfModel = MJCFBuilder.Build(mjcfTree, package.MJCFMeshDir, auxByLinkName, MJCFRotationFormat, MJCFAngleUnit);
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
                // URDF mesh filenames (package:// URIs) are stamped onto the
                // URDFRobot's MeshGroups by ExportFiles, so the canonical tree
                // is built from URDFRobot rather than ActiveWorldNode.
                KinematicTree urdfTree = KinematicTreeAdapter.ToCore(URDFRobot);
                MaybeCaptureGoldenSnapshot(SnapshotReplay.UrdfFormat, windowsModelFileName, urdfTree,
                    null, auxByLinkName);
                URDFWriter uWriter = new URDFWriter(windowsModelFileName);
                try
                {
                    URDFBuilder.Write(urdfTree, uWriter.writer, auxByLinkName);
                }
                finally
                {
                    uWriter.writer.Close();
                }
            }

            logger.Info("Copying log file");
            CopyLogFile(package);

            logger.Info("Resetting STL preferences");
            ResetUserPreferences();
            progressBar.End();
            return true;
        }

        // When the SW2RD_CAPTURE_GOLDEN environment variable is set (to anything
        // non-empty), writes a `<output>.snapshot.json` alongside the exported
        // URDF / MJCF file. The snapshot freezes the exact writer input (the
        // canonical KinematicTree plus MJCF mesh-asset / site auxiliary data and
        // the writer options) so the golden tests can replay it SW-free and the
        // committed `expected.*` outputs can be regenerated without re-running
        // SolidWorks. This is a developer / maintainer fixture-blessing hook and
        // is a no-op in normal user exports. Failures here never abort an export.
        private void MaybeCaptureGoldenSnapshot(
            string format,
            string outputFileName,
            KinematicTree tree,
            string mjcfMeshDir,
            Dictionary<string, LinkAuxiliary> auxByLinkName)
        {
            string flag = System.Environment.GetEnvironmentVariable("SW2RD_CAPTURE_GOLDEN");
            if (string.IsNullOrEmpty(flag))
            {
                return;
            }
            try
            {
                ExportSnapshot snapshot = new ExportSnapshot
                {
                    Format = format,
                    ModelName = tree?.Name ?? URDFRobot?.Name ?? "",
                    Tree = tree,
                    MeshDir = mjcfMeshDir,
                    Auxiliary = auxByLinkName ?? new Dictionary<string, LinkAuxiliary>(),
                    MjcfRotationFormat = MJCFRotationFormat,
                    MjcfAngleUnit = MJCFAngleUnit,
                };
                string snapshotPath = Path.ChangeExtension(outputFileName, ".snapshot.json");
                File.WriteAllText(snapshotPath, ExportSnapshotSerializer.Serialize(snapshot));
                logger.Info("Captured golden snapshot to " + snapshotPath);
            }
            catch (Exception e)
            {
                logger.Warn("Failed to capture golden snapshot: " + e.Message);
            }
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
            Dictionary<string, LinkAuxiliary> auxByLinkName = null)
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
                    ExportFiles(child, package, count, exportSTL, meshFormat, auxByLinkName);
                }
            }

            if (!link.isFixedFrame)
            {
                ProcessLinkMeshes(link, link.Name, package, exportSTL, meshFormat, auxByLinkName);
            }
        }

        // Mesh-export work for one Link. Body links are keyed by link.Name in
        // auxByLinkName; the WorldNode's pseudo-link is keyed by MJCFBuilder.WorldAuxKey
        // so the MJCF builder can lift its geoms/sites directly under <worldbody>.
        private void ProcessLinkMeshes(Link link, string assetKey, ExportPackage package,
            bool exportSTL, MeshExportFormat meshFormat, Dictionary<string, LinkAuxiliary> auxByLinkName)
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

            // Make sure legacy flat component lists have been migrated into
            // VisualGroups / CollisionGroups. New links may intentionally have
            // zero visual groups until the user adds one.
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
            if (auxByLinkName != null)
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
                auxByLinkName[assetKey ?? link.Name] = aux;
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
            // Part A: per-part tessellation path. Reads body geometry directly
            // at a uniform, display-independent tolerance and transforms it into
            // the link frame - no whole-assembly hide/show, no SaveAs.
            if (UseTessellationMeshExport)
            {
                return SaveSTLViaTessellation(link, windowsMeshFilename, components);
            }

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

        // === Part A: per-part tessellation mesh export ===
        //
        // Writes the link's mesh by reading each member component's solid-body
        // tessellation directly (at a uniform, display-INDEPENDENT tolerance)
        // and transforming the vertices into the link's coordinate frame, then
        // emitting one binary STL. This avoids the whole-assembly SaveAs (which
        // exports only VISIBLE geometry and therefore forces hiding every
        // non-member component, purging + reloading their graphics - the
        // dominant export cost). Equivalent output frame to the SaveAs path:
        // SaveAs uses swSaveAsCoordinateSystem = the link's joint coord-sys, and
        // here we map global -> that same coord-sys via its MathTransform.
        //
        // Quality: ITessellation.SurfacePlaneTolerance / SurfacePlaneAngleTolerance
        // are driven from SW's STL deviation/angle preferences (the same values
        // the STL quality slider controls), so the mesh matches an STL export at
        // the chosen Fine/Coarse quality and does NOT depend on the on-screen
        // display tessellation.
        private bool SaveSTLViaTessellation(Link link, string windowsMeshFilename, List<Component2> components)
        {
            string coordsysName = link.Joint != null ? link.Joint.CoordinateSystemName : null;
            logger.Info(link.Name + ": Exporting STL via tessellation, frame=" + coordsysName);

            MathTransform linkToGlobal = string.IsNullOrEmpty(coordsysName)
                ? null : GetCoordinateSystemTransform(coordsysName);
            Matrix<double> globalToLink;
            if (linkToGlobal == null)
            {
                logger.Warn(link.Name + ": could not resolve link coordinate system '" + coordsysName +
                    "'; tessellation will be written in assembly-global coordinates.");
                globalToLink = Matrix<double>.Build.DenseIdentity(4);
            }
            else
            {
                globalToLink = MathOps.GetTransformation(linkToGlobal).Inverse();
            }

            // Quality is resolved PER BODY (in the loop below) relative to that
            // body's own bounding box, NOT from SW's swSTLDeviation pref. The
            // pref is computed from the WHOLE-ASSEMBLY box and applied uniformly,
            // so it leaves small parts badly faceted (the user-reported "poor
            // quality"). Selecting the chord tolerance from each body's own size
            // gives uniform PERCEIVED detail across every part - including parts
            // nested inside a sub-assembly group, which ExpandWithChildren has
            // already decomposed into their individual leaf bodies. The quality
            // level only picks the relative fraction + angle tolerance here.
            MeshQualityToTolerances(MeshQualityLevel, out double qualityFraction, out double angle);
            logger.Info(link.Name + ": tessellation quality level=" + MeshQualityLevel +
                " (bbox fraction=" + qualityFraction + ", angle=" + angle + " rad).");

            // Expand sub-assembly group members to their leaf parts (mirrors the
            // SaveAs path's ShowComponents expansion) so a group that names a
            // sub-assembly exports the union of its descendant parts as one mesh.
            List<Component2> expanded = CommonSwOperations.ExpandWithChildren(components);
            List<double[]> triangles = new List<double[]>();
            foreach (Component2 comp in expanded)
            {
                if (comp == null)
                {
                    continue;
                }
                EnsureComponentResolvedForTessellation(comp);
                object bodiesObj = comp.GetBodies3((int)swBodyType_e.swSolidBody, out _);
                if (!(bodiesObj is object[] bodies) || bodies.Length == 0)
                {
                    continue;
                }

                // Leaf-part-local -> assembly-global, then global -> link frame.
                Matrix<double> compToGlobal = MathOps.GetTransformation(comp.GetTotalTransform(true));
                Matrix<double> compToLink = globalToLink * compToGlobal;

                foreach (object bodyObj in bodies)
                {
                    if (bodyObj is Body2 body)
                    {
                        double deviation = ComputeBodyChordTolerance(body, qualityFraction);
                        AppendBodyTessellation(body, compToLink, deviation, angle, triangles);
                    }
                }
            }

            WriteBinaryStl(windowsMeshFilename, triangles);
            logger.Info(link.Name + ": tessellation wrote " + triangles.Count +
                " triangle(s) to " + windowsMeshFilename);
            if (triangles.Count == 0)
            {
                logger.Warn(link.Name + ": tessellation produced ZERO triangles - the resulting mesh " +
                    "is empty and will not load in MuJoCo. Check that the link's components are resolved " +
                    "(not lightweight/suppressed) and contain solid bodies.");
                return false;
            }
            return true;
        }

        // Tessellate one solid body at the given absolute tolerances and append
        // its triangles (transformed into the link frame) to `triangles`.
        private void AppendBodyTessellation(Body2 body, Matrix<double> compToLink,
            double deviation, double angle, List<double[]> triangles)
        {
            Tessellation tess = (Tessellation)body.GetTessellation(null);
            if (tess == null)
            {
                return;
            }
            tess.NeedFaceFacetMap = false;
            tess.NeedVertexNormal = false;
            tess.NeedVertexParams = false;
            tess.NeedEdgeFinMap = false;
            tess.ImprovedQuality = true;
            tess.SurfacePlaneTolerance = deviation;
            tess.SurfacePlaneAngleTolerance = angle;
            if (!tess.Tessellate())
            {
                logger.Warn("Tessellate() returned false for a body; skipping it.");
                return;
            }

            int facetCount = tess.GetFacetCount();
            for (int f = 0; f < facetCount; f++)
            {
                // Each facet has 3 fins (half-edges) forming a closed loop:
                // fin0 = (a,b), fin1 = (b,c) or (c,b). Recover the 3 vertices.
                if (!(tess.GetFacetFins(f) is int[] fins) || fins.Length < 3)
                {
                    continue;
                }
                if (!(tess.GetFinVertices(fins[0]) is int[] e0) || e0.Length < 2 ||
                    !(tess.GetFinVertices(fins[1]) is int[] e1) || e1.Length < 2)
                {
                    continue;
                }
                int a = e0[0];
                int b = e0[1];
                int c = (e1[0] != a && e1[0] != b) ? e1[0] : e1[1];
                if (!(tess.GetVertexPoint(a) is double[] pa) ||
                    !(tess.GetVertexPoint(b) is double[] pb) ||
                    !(tess.GetVertexPoint(c) is double[] pc) ||
                    pa.Length < 3 || pb.Length < 3 || pc.Length < 3)
                {
                    continue;
                }
                double[] la = TransformPoint(compToLink, pa);
                double[] lb = TransformPoint(compToLink, pb);
                double[] lc = TransformPoint(compToLink, pc);
                triangles.Add(new double[]
                {
                    la[0], la[1], la[2],
                    lb[0], lb[1], lb[2],
                    lc[0], lc[1], lc[2],
                });
            }
        }

        // Resolves a lightweight leaf so its solid bodies can be read for
        // tessellation. swExtRefOpenReadOnly is forced true around the export
        // (ExportPropertyManager), so this does NOT check out or modify the part
        // file in PDM. Left resolved for the rest of the session (memory only).
        private void EnsureComponentResolvedForTessellation(Component2 comp)
        {
            try
            {
                int state = comp.GetSuppression2();
                if (state == (int)swComponentSuppressionState_e.swComponentLightweight ||
                    state == (int)swComponentSuppressionState_e.swComponentFullyLightweight)
                {
                    logger.Info("Resolving lightweight leaf '" + comp.Name2 + "' for tessellation.");
                    comp.SetSuppression2((int)swComponentSuppressionState_e.swComponentFullyResolved);
                }
            }
            catch (Exception e)
            {
                logger.Warn("Could not check/resolve component suppression before tessellation", e);
            }
        }

        // Absolute clamps on the per-body chord tolerance (meters). The lower
        // bound stops a tiny body from generating a runaway triangle count; the
        // upper bound stops a very large body from coming out visibly faceted
        // even at Coarse.
        private const double MinBodyChordTolerance = 1.0e-5;  // 0.01 mm
        private const double MaxBodyChordTolerance = 5.0e-3;  // 5 mm

        // Maps the mesh-quality level (0=Coarse..3=Very fine) to a relative
        // chord-tolerance fraction (of each body's bbox diagonal) and an angle
        // tolerance (radians). Finer levels -> smaller fraction + tighter angle.
        private static void MeshQualityToTolerances(int level, out double fraction, out double angleRad)
        {
            switch (level)
            {
                case 0: // Coarse
                    fraction = 0.010;
                    angleRad = 30.0 * Math.PI / 180.0;
                    break;
                case 1: // Medium
                    fraction = 0.005;
                    angleRad = 20.0 * Math.PI / 180.0;
                    break;
                case 3: // Very fine
                    fraction = 0.001;
                    angleRad = 8.0 * Math.PI / 180.0;
                    break;
                case 2: // Fine (default)
                default:
                    fraction = 0.002;
                    angleRad = 12.0 * Math.PI / 180.0;
                    break;
            }
        }

        // Per-body chord (surface-plane) tolerance in meters: the body's own
        // bounding-box diagonal times the quality fraction, clamped. Using the
        // body's OWN size keeps perceived detail uniform regardless of how big
        // the part is or where it sits in the assembly. Falls back to the Fine-
        // level absolute clamp midpoint if the body box can't be read.
        private double ComputeBodyChordTolerance(Body2 body, double fraction)
        {
            double diagonal = 0.0;
            try
            {
                if (body.GetBodyBox() is double[] box && box.Length >= 6)
                {
                    double dx = box[3] - box[0];
                    double dy = box[4] - box[1];
                    double dz = box[5] - box[2];
                    diagonal = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                }
            }
            catch (Exception e)
            {
                logger.Warn("GetBodyBox failed; using minimum chord tolerance for this body", e);
            }
            if (!(diagonal > 0.0))
            {
                return MinBodyChordTolerance;
            }
            return MathOps.Envelope(diagonal * fraction, MinBodyChordTolerance, MaxBodyChordTolerance);
        }

        // Transforms a 3D point by a 4x4 homogeneous matrix (column-vector
        // convention matching MathOps.GetTransformation: p' = M * p).
        private static double[] TransformPoint(Matrix<double> m, double[] p)
        {
            double x = p[0], y = p[1], z = p[2];
            return new double[]
            {
                m[0, 0] * x + m[0, 1] * y + m[0, 2] * z + m[0, 3],
                m[1, 0] * x + m[1, 1] * y + m[1, 2] * z + m[1, 3],
                m[2, 0] * x + m[2, 1] * y + m[2, 2] * z + m[2, 3],
            };
        }

        // Writes a binary STL (zeroed 80-byte header - so CorrectSTLMesh is not
        // needed - little-endian uint count, then 50 bytes per triangle).
        // Vertices are in meters (SW API geometry is always SI), matching the
        // SaveAs path's swExportStlUnits = meters.
        private static void WriteBinaryStl(string filename, List<double[]> triangles)
        {
            using (FileStream fs = new FileStream(filename, FileMode.Create, FileAccess.Write))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                bw.Write(new byte[80]);
                bw.Write((uint)triangles.Count);
                foreach (double[] t in triangles)
                {
                    double[] n = ComputeTriangleNormal(t);
                    bw.Write((float)n[0]); bw.Write((float)n[1]); bw.Write((float)n[2]);
                    for (int i = 0; i < 9; i++)
                    {
                        bw.Write((float)t[i]);
                    }
                    bw.Write((ushort)0);
                }
            }
        }

        // Unit facet normal from triangle winding (v1-v0) x (v2-v0). Zero vector
        // for degenerate triangles; STL consumers recompute normals regardless.
        private static double[] ComputeTriangleNormal(double[] t)
        {
            double ux = t[3] - t[0], uy = t[4] - t[1], uz = t[5] - t[2];
            double vx = t[6] - t[0], vy = t[7] - t[1], vz = t[8] - t[2];
            double nx = uy * vz - uz * vy;
            double ny = uz * vx - ux * vz;
            double nz = ux * vy - uy * vx;
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len < 1e-15)
            {
                return new double[] { 0.0, 0.0, 0.0 };
            }
            return new double[] { nx / len, ny / len, nz / len };
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

            // Single-part export uses the Visual/Collision template directly
            // rather than the multi-group lists. The records-native URDFBuilder
            // reads only MeshGroups, so publish the mesh into a single visual
            // group and let the collision fall back to visual.
            URDFRobot.BaseLink.VisualGroups = new List<MeshGroup>
            {
                new MeshGroup(MeshGroup.DefaultVisualName()) { MeshFilename = meshFileName },
            };
            URDFRobot.BaseLink.CollisionGroups = new List<MeshGroup>();
            URDFRobot.BaseLink.CollisionUsesVisual = true;

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
            KinematicTree urdfTree = KinematicTreeAdapter.ToCore(URDFRobot);
            URDFWriter uWriter = new URDFWriter(windowsURDFFileName);
            try
            {
                URDFBuilder.Write(urdfTree, uWriter.writer);
            }
            finally
            {
                uWriter.writer.Close();
            }

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
                    UserNotifier.Show("The log file was expected to be located at " + log_filename +
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
