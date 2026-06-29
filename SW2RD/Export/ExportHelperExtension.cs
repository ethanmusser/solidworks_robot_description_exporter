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
using MathNet.Numerics.LinearAlgebra.Double;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SW2RD.Input;
using SW2RD.UI;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace SW2RD.Export
{
    public partial class ExportHelper
    {
        private string referenceSketchName;
        private string ExportErrorWhy;

        #region SW to Robot and link methods

        //Used right now only by the Part Exporter, but this starts the building of the robot
        public void CreateRobotFromActiveModel()
        {
            URDFRobot = new Robot();
            URDFRobot.Name = ActiveSWModel.GetTitle();

            SolidWorks.Interop.sldworks.Configuration swConfig =
                ActiveSWModel.ConfigurationManager.ActiveConfiguration;
            foreach (string state in swConfig.GetDisplayStates())
            {
                if (state.Equals("Robot Description Export"))
                {
                    swConfig.ApplyDisplayState("Robot Description Export");
                }
            }

            //Each Robot contains a single base link, build this link
            Link baseLink = CreateBaseLinkFromActiveModel();
            URDFRobot.SetBaseLink(baseLink);
        }

        // This method now only works for the part exporter
        private Link CreateBaseLinkFromActiveModel()
        {
            // If the model is a part
            if (ActiveSWModel.GetType() == (int)swDocumentTypes_e.swDocPART)
            {
                return CreateLinkFromPartModel(ActiveSWModel);
            }
            return null;
        }

        // This creates a Link from a Part ModelDoc. It basically just extracts the material
        // properties and saves them to the appropriate fields.
        private static Link CreateLinkFromPartModel(ModelDoc2 swModel)
        {
            Link Link = new Link(null);
            Link.Name = swModel.GetTitle();

            Link.isFixedFrame = false;

            //Get link properties from SolidWorks part
            IMassProperty swMass = swModel.Extension.CreateMassProperty();
            Link.Inertial.Mass.Value = swMass.Mass;

            // returned as double with values [Lxx, Lxy, Lxz, Lyx, Lyy, Lyz, Lzx, Lzy, Lzz]
            double[] moment = swMass.GetMomentOfInertia(
                (int)swMassPropertyMoment_e.swMassPropertyMomentAboutCenterOfMass);
            Link.Inertial.Inertia.SetMomentMatrix(moment);

            double[] centerOfMass = swMass.CenterOfMass;
            Link.Inertial.Origin.SetXYZ(centerOfMass);
            Link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });

            // Will this ever not be zeros?
            Link.Visual.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            Link.Visual.Origin.SetRPY(new double[3] { 0, 0, 0 });
            Link.Collision.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            Link.Collision.Origin.SetRPY(new double[3] { 0, 0, 0 });

            // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
            double[] values = swModel.MaterialPropertyValues;
            Link.Visual.Material.Color.Red = values[0];
            Link.Visual.Material.Color.Green = values[1];
            Link.Visual.Material.Color.Blue = values[2];
            Link.Visual.Material.Color.Alpha = 1.0 - values[7];
            Link.Visual.Material.Name = "material_" + Link.Name;

            return Link;
        }

        //This is only used by the Part Exporter, but it localizes the link to the Origin_global
        // coordinate system
        private static void LocalizeLink(Link Link, Matrix<double> GlobalTransform)
        {
            Matrix<double> GlobalTransformInverse = GlobalTransform.Inverse();
            Matrix<double> linkCoMTransform = MathOps.GetTranslation(Link.Inertial.Origin.GetXYZ());
            Matrix<double> localLinkCoMTransform = GlobalTransformInverse * linkCoMTransform;

            Matrix<double> linkVisualTransform =
                MathOps.GetTransformation(Link.Visual.Origin.GetXYZ(), Link.Visual.Origin.GetRPY());
            Matrix<double> localVisualTransform = GlobalTransformInverse * linkVisualTransform;

            Matrix<double> linkCollisionTransform =
                MathOps.GetTransformation(Link.Collision.Origin.GetXYZ(), Link.Collision.Origin.GetRPY());
            Matrix<double> localCollisionTransform =
                GlobalTransformInverse * linkCollisionTransform;

            // The linear array in Link.Inertial.Inertia.Moment is in row major order, but this
            // matrix constructor uses column major order. It's a rotation matrix, so this
            // shouldn't matter. If it does, just transpose linkGlobalMomentInertia. These three
            // matrices are 3x3 as opposed to the 4x4 transformation matrices above.
            // You're welcome for the confusion.
            Matrix<double> linkGlobalMomentInertia =
                new DenseMatrix(3, 3, Link.Inertial.Inertia.GetMoment());
            Matrix<double> GlobalRotMat =
                GlobalTransform.SubMatrix(0, 3, 0, 3);
            Matrix<double> linkLocalMomentInertia =
                GlobalRotMat * linkGlobalMomentInertia * GlobalRotMat.Transpose();

            Link.Inertial.Origin.SetXYZ(MathOps.GetXYZ(localLinkCoMTransform));
            Link.Inertial.Origin.SetRPY(new double[] { 0, 0, 0 });

            // Wait are you saying that even though the matrix was trasposed from column major
            // order, you are writing it in row-major order here. Yes, yes I am.
            double[] moment = linkLocalMomentInertia.ToRowMajorArray();
            Link.Inertial.Inertia.SetMomentMatrix(moment);

            Link.Collision.Origin.SetXYZ(MathOps.GetXYZ(localCollisionTransform));
            Link.Collision.Origin.SetRPY(MathOps.GetRPY(localCollisionTransform));

            Link.Visual.Origin.SetXYZ(MathOps.GetXYZ(localVisualTransform));
            Link.Visual.Origin.SetRPY(MathOps.GetRPY(localVisualTransform));
        }

        /// <summary>
        /// Build the legacy <see cref="Robot"/> graph from the PMP's
        /// <see cref="LinkNode"/> tree.
        ///
        /// The <paramref name="rootNode"/> may be:
        /// 1. A <see cref="WorldNode"/> root (current shape). The world's
        ///    <c>GlobalOriginCoordinateSystemName</c> drives the
        ///    <c>Origin_global</c> auto-generation once per export, and
        ///    each top-level body becomes a candidate URDF base link.
        ///    Today's URDF/MJCF pipeline still flows through a single
        ///    <see cref="Robot.BaseLink"/>, so we pick the FIRST top-level
        ///    body as the base link and warn if there are more (matching
        ///    the contract documented in
        ///    <c>KinematicTreeAdapter.ToLegacyRobot</c>).
        /// 2. A bare <see cref="LinkNode"/> (legacy shape). Built as
        ///    today: that node IS the base link.
        /// </summary>
        public bool CreateRobotFromTreeView(LinkNode rootNode)
        {
            using (BeginFeatureLookupCache())
            {
                ExportErrorWhy = "";
                URDFRobot = new Robot();

                // Tier B1: the joint-creation traversal below fixes/unfixes
                // components and re-solves the assembly once per joint. Each
                // Fix/Unfix/Suppress would otherwise force a viewport redraw,
                // so suppress graphics updates for the WHOLE pass and do a
                // single GraphicsRedraw2 in the finally. Tier B2: components we
                // fix are accumulated in sessionFixedComponents (fixed at most
                // once across the pass) and unfixed in one bulk round trip by
                // UnfixSessionFixedComponents, also in the finally. Both run on
                // every exit path (early returns / exceptions) via try/finally.
                ModelView jointPassView = ActiveSWModel.ActiveView as ModelView;
                bool priorJointPassGraphics = true;
                if (ComputeJointKinematics && jointPassView != null)
                {
                    priorJointPassGraphics = jointPassView.EnableGraphicsUpdate;
                    jointPassView.EnableGraphicsUpdate = false;
                }
                sessionFixedComponents = new Dictionary<string, Component2>();
                try
                {

                progressBar.Start(0, CommonSwOperations.GetCount(rootNode.Nodes) + 1, "Building links");
                // Register the live export bar with SwProgress so a slow shared
                // resolver (e.g. the in-context flexible-subassembly coord-sys
                // fallback) can retitle it via SwProgress.SetTitle. Ownership of
                // Start/End stays here; SwProgress.DetachExternal in the export
                // finally clears the registration.
                SwProgress.AttachExternal(progressBar);
                int count = 0;

                progressBar.UpdateProgress(count);
                progressBar.UpdateTitle("Building link: " + rootNode.Name);

                // The world's global-origin coord-sys is the single source of
                // truth for "the assembly's global frame" - auto-generate
                // Origin_global ONCE per export when it's empty / placeholder,
                // then propagate the resolved name to every top-level body
                // whose own coord-sys also wants the global default. This
                // replaces the per-base CreateBaseRefOrigin call that used to
                // live inside CreateBaseLinkFromComponents (which rendered an
                // Origin_global per top-level body and silently overrode any
                // user-set body coord-sys).
                LinkNode topLevelBaseNode;
                // The world's resolved global-origin coord-sys name, captured
                // out of the WorldNode branch so the base link can be localized
                // against it once it is built (see the LocalizeJoint call after
                // CreateLink). Null on the legacy LinkNode-rooted path.
                string worldGlobalOriginName = null;
                if (rootNode is WorldNode worldNode)
                {
                    // Stash the WorldNode so ExportRobot can pick up
                    // world-level visual/collision/site geometry. This is
                    // the only path from the PMP that carries world data;
                    // legacy LinkNode-rooted callers leave ActiveWorldNode
                    // null and the MJCF builder falls back to an empty
                    // synthesised world.
                    ActiveWorldNode = worldNode;

                    string resolvedGlobalName = ResolveAndGenerateGlobalOrigin(worldNode);
                    worldGlobalOriginName = resolvedGlobalName;

                    List<LinkNode> topLevels = new List<LinkNode>();
                    foreach (LinkNode child in worldNode.Nodes)
                    {
                        topLevels.Add(child);
                    }
                    if (topLevels.Count == 0)
                    {
                        ExportErrorWhy = "World has no top-level body. Add at least one child to the World node before exporting.";
                        UserNotifier.Show(ExportErrorWhy);
                        logger.Warn(ExportErrorWhy);
                        progressBar.End();
                        return false;
                    }
                    if (topLevels.Count > 1)
                    {
                        // URDF describes a single robot in isolation; the
                        // Robot graph can only carry one BaseLink. Mirror the
                        // KinematicTreeAdapter.ToLegacyRobot warning here so
                        // the user sees the same message regardless of which
                        // path produced the legacy Robot.
                        System.Text.StringBuilder dropped = new System.Text.StringBuilder();
                        for (int i = 1; i < topLevels.Count; i++)
                        {
                            if (dropped.Length > 0) dropped.Append(", ");
                            dropped.Append(topLevels[i].Name ?? "<unnamed>");
                        }
                        logger.Warn("URDF/MJCF export: " + topLevels.Count +
                            " top-level bodies under World; URDF can only describe a single robot. " +
                            "First body '" + (topLevels[0].Name ?? "<unnamed>") + "' becomes base_link; " +
                            "dropping additional top-level bodies: " + dropped + ".");
                    }
                    topLevelBaseNode = topLevels[0];

                    // For each top-level body whose own coord-sys is empty or
                    // the legacy "Automatically Generate" sentinel, inherit
                    // the world's resolved global-origin so today's behavior
                    // (welded-at-world: identity world->body offset) is
                    // preserved through the legacy Robot path.
                    foreach (LinkNode topLevel in topLevels)
                    {
                        if (topLevel?.Link?.Joint == null) continue;
                        string body = topLevel.Link.Joint.CoordinateSystemName;
                        if (string.IsNullOrEmpty(body) ||
                            body == "Automatically Generate")
                        {
                            topLevel.Link.Joint.CoordinateSystemName = resolvedGlobalName;
                        }
                    }
                }
                else
                {
                    // Legacy: rootNode IS the base link.
                    ActiveWorldNode = null;
                    topLevelBaseNode = rootNode;
                }

                Link baseLink = CreateLink(topLevelBaseNode, 1);
                if (baseLink == null || !string.IsNullOrWhiteSpace(ExportErrorWhy))
                {
                    UserNotifier.Show(ExportErrorWhy);
                    logger.Warn(ExportErrorWhy);
                    progressBar.End();
                    return false;
                }
                URDFRobot.SetBaseLink(baseLink);
                topLevelBaseNode.Link = baseLink;

                // CreateLink builds the base link with a null parent, so its
                // joint origin was never localized. Under a World node the base
                // link can sit anywhere relative to the world's global-origin
                // frame, so stamp the world->base_link offset now. The MJCF
                // top-level body reads Link.Joint.Origin for its pos/quat, so
                // without this a body whose frame differs from the global
                // origin (e.g. a vehicle resting on top of a ground plane)
                // spawns at the world origin instead of its modelled pose -
                // and a Free attachment then free-floats from the wrong spot.
                // When the base frame coincides with the global origin this
                // resolves to identity, matching the welded-at-world default,
                // so welded/origin-coincident examples are unaffected.
                if (worldGlobalOriginName != null &&
                    baseLink.Joint != null &&
                    !string.IsNullOrEmpty(baseLink.Joint.CoordinateSystemName))
                {
                    LocalizeJoint(baseLink.Joint, worldGlobalOriginName);
                }

                progressBar.End();
                return true;
                }
                finally
                {
                    // Restore the user's session: unfix everything WE fixed and
                    // re-enable + redraw the viewport once. Runs on every exit
                    // path (success, early return, exception).
                    UnfixSessionFixedComponents();
                    if (ComputeJointKinematics && jointPassView != null)
                    {
                        jointPassView.EnableGraphicsUpdate = priorJointPassGraphics;
                        ActiveSWModel.GraphicsRedraw2();
                    }
                }
            }
        }

        // Auto-generate Origin_global once per export when the world's
        // configured global-origin coord-sys is empty or the legacy
        // "Automatically Generate" sentinel. Returns the resolved name
        // (always non-empty on success). Top-level bodies inheriting the
        // global default get this same name in CreateRobotFromTreeView.
        private string ResolveAndGenerateGlobalOrigin(WorldNode worldNode)
        {
            string globalName = worldNode?.GlobalOriginCoordinateSystemName ?? "";
            if (string.IsNullOrEmpty(globalName) ||
                globalName == "Automatically Generate")
            {
                CreateBaseRefOrigin(true);
                globalName = "Origin_global";
                if (worldNode != null)
                {
                    worldNode.GlobalOriginCoordinateSystemName = globalName;
                }
            }
            return globalName;
        }

        // Build a top-level body link from its components. The world's
        // global-origin auto-generation has already happened (in
        // CreateRobotFromTreeView), and the body's Link.Joint.CoordinateSystemName
        // has already been populated with the world's resolved name when it
        // was empty. This now just delegates to CreateLinkFromComponents.
        private Link CreateTopLevelLinkFromComponents(LinkNode node)
        {
            Link link = CreateLinkFromComponents(null, node);
            if (link != null)
            {
                link.Joint.CoordinateSystemName = node.Link.Joint.CoordinateSystemName;
            }
            return link;
        }

        //Method which builds an entire link and iterates through.
        // Treats both legacy base-node nodes (IsBaseNode == true) and the
        // new WorldNode-rooted top-level bodies (IsTopLevelBody == true) as
        // "URDF base link" equivalents, since the legacy Robot graph carries
        // exactly one BaseLink and either kind of node fills that role.
        private Link CreateLink(LinkNode node, int count)
        {
            progressBar.UpdateTitle("Building link: " + node.Name);
            progressBar.UpdateProgress(count);
            Link link;
            bool treatAsBase = node.IsBaseNode || node.IsTopLevelBody;
            if (treatAsBase)
            {
                link = CreateTopLevelLinkFromComponents(node);
                URDFRobot.SetBaseLink(link);
            }
            else
            {
                LinkNode parentNode = (LinkNode)node.Parent;
                link = CreateLinkFromComponents(parentNode.Link, node);
            }
            node.Link = link;
            if (!string.IsNullOrWhiteSpace(ExportErrorWhy))
            {
                return null;
            }

            // Reset list of children, don't worry the links that were saved are still attached to the child nodes
            link.Children.Clear();
            foreach (LinkNode child in node.Nodes)
            {
                Link childLink = CreateLink(child, count + 1);

                if (!string.IsNullOrWhiteSpace(ExportErrorWhy))
                {
                    return null;
                }
                else
                {
                    link.Children.Add(childLink);
                }
            }
            return link;
        }

        /// <summary>
        /// Gets the Moment of Inertia of specific component bodies with respect to the coordinate system.
        /// This reuses some code with other methods because creating the mass property has to happen every time
        /// </summary>
        /// <param name="bodies">Component Bodies with which to get the MOI</param>
        /// <param name="coordinateSystemTransform">The coordinate system to take the MOI with respect to</param>
        /// <returns>Moment of Inertia array</returns>
        private double[] GetComponentsMomentOfInertia(List<Body2> bodies, MathTransform coordinateSystemTransform)
        {
            MassProperty swMass = ActiveSWModel.Extension.CreateMassProperty();
            swMass.SetCoordinateSystem(coordinateSystemTransform);
            bool bRet = swMass.AddBodies(bodies.ToArray());
            if (!bRet)
            {
                throw new Exception("Failed to add bodies to swMass");
            }

            return (double[])swMass.GetMomentOfInertia(
            (int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass);
        }

        /// <summary>
        /// Gets the components mass. This reuses some code with other methods because creating the
        /// mass property has to happen every time
        /// </summary>
        /// <param name="bodies">Component Bodies with which to get the mass</param>
        /// <returns>Mass value of component bodies</returns>
        private double GetCompomentsMass(List<Body2> bodies)
        {
            MassProperty swMass = ActiveSWModel.Extension.CreateMassProperty();
            bool bRet = swMass.AddBodies(bodies.ToArray());
            if (!bRet)
            {
                throw new Exception("Failed to add bodies to swMass");
            }
            return swMass.Mass;
        }

        /// <summary>
        /// Gets the Center of Mass with respect to the coordinate system. This reuses some code
        /// with other similar methods because creating the mass property has to happen every time.
        /// </summary>
        /// <param name="bodies">Component bodies with which to get the mass</param>
        /// <param name="coordinateSystemTransform">Coordinate system take get the centor of mess with respect to</param>
        /// <returns>3D double array of center of mass</returns>
        private double[] GetCompomentsCenterOfMass(List<Body2> bodies, MathTransform coordinateSystemTransform)
        {
            MassProperty swMass = ActiveSWModel.Extension.CreateMassProperty();
            swMass.SetCoordinateSystem(coordinateSystemTransform);
            bool bRet = swMass.AddBodies(bodies.ToArray());
            if (!bRet)
            {
                throw new Exception("Failed to add bodies to swMass");
            }
            return swMass.CenterOfMass;
        }

        private void ComputeInertialProperties(Link link)
        {
            // Get the SolidWorks MathTransform that corresponds to the child coordinate system
            MathTransform jointTransform = GetCoordinateSystemTransform(link.Joint.CoordinateSystemName);

            // Pick the components based on the per-link InertialSource choice. Falls
            // back to visual components when the user requested Collision/Custom but
            // forgot to populate the corresponding box.
            List<Component2> inertialComponents = link.GetInertialComponents(out bool isFallback);
            if (isFallback)
            {
                logger.Warn("Link " + link.Name + " requested " + link.InertialSource +
                    " inertial components but none were configured. Falling back to visual components.");
            }
            List<Body2> bodies = GetBodies(inertialComponents);
            if (bodies.Count == 0)
            {
                logger.Warn("Link " + link.Name + " has no bodies to compute inertia from; " +
                    "skipping inertial computation.");
                return;
            }

            double[] moment = GetComponentsMomentOfInertia(bodies, jointTransform);
            link.Inertial.Inertia.SetMomentMatrix(moment);

            link.Inertial.Mass.Value = GetCompomentsMass(bodies);

            double[] centerOfMass = GetCompomentsCenterOfMass(bodies, jointTransform);
            link.Inertial.Origin.SetXYZ(centerOfMass);
            link.Inertial.Origin.SetRPY(new double[3] { 0, 0, 0 });
        }

        private static void ComputeVisualCollisionProperties(Link link)
        {
            link.Visual.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            link.Visual.Origin.SetRPY(new double[3] { 0, 0, 0 });
            link.Collision.Origin.SetXYZ(new double[3] { 0, 0, 0 });
            link.Collision.Origin.SetRPY(new double[3] { 0, 0, 0 });

            if (link.SWComponents.Count == 0)
            {
                return;
            }

            ModelDoc2 mainCompdoc = link.SWComponents[0].GetModelDoc2();

            // [ R, G, B, Ambient, Diffuse, Specular, Shininess, Transparency, Emission ]
            double[] values = mainCompdoc.MaterialPropertyValues;
            link.Visual.Material.Color.Red = values[0];
            link.Visual.Material.Color.Green = values[1];
            link.Visual.Material.Color.Blue = values[2];
            link.Visual.Material.Color.Alpha = 1.0 - values[7];
        }

        //Method which builds a single link
        private Link CreateLinkFromComponents(Link parent, LinkNode node)
        {
            if (node.Link.SWComponents.Count > 0)
            {
                List<Component2> components = node.Link.SWComponents;
                node.Link.SWMainComponent = components[0];
            }

            if (parent != null && ComputeJointKinematics)
            {
                logger.Info("Creating joint " + node.Link.Name);
                // CreateJoint returns TRUE on success / FALSE on error (it
                // returns false only from its two ExportErrorWhy guards). The
                // warning must therefore fire on !success - the old code warned
                // on the success return, so every healthy export logged
                // "Creating joint ... failed" for every joint.
                bool success = CreateJoint(parent, node.Link);
                if (!success)
                {
                    logger.Warn(
                        string.Format("Creating joint from parent {0} to child {1} failed", 
                            parent.Name, node.Link.Name));
                }
            }

            if (ComputeInertialValues)
            {
                ComputeInertialProperties(node.Link);
            }

            if (ComputeVisualCollision)
            {
                ComputeVisualCollisionProperties(node.Link);
            }

            return node.Link;
        }

        private List<Body2> GetBodies(List<Component2> components)
        {
            List<Body2> bodies = new List<Body2>();
            foreach (Component2 comp in components)
            {
                // Retrieving the Body2 bodies of the component. Also need to recur through the assembly tree
                object[] componentBodies =
                    (object[])comp.GetBodies3((int)swBodyType_e.swSolidBody, out _);
                if (componentBodies != null)
                {
                    foreach (Body2 obj in componentBodies)
                    {
                        bodies.Add(obj);
                    }
                }
                object[] children = comp.GetChildren();
                if (children != null)
                {
                    List<Component2> childComponents = new List<Component2>();
                    foreach (Component2 child in children)
                    {
                        childComponents.Add(child);
                    }
                    bodies.AddRange(GetBodies(childComponents));
                }
            }
            return bodies;
        }

        #endregion SW to Robot and link methods

        #region Joint methods

        //Base method for constructing a joint from a parent link and child link.
        private bool CreateJoint(Link parent, Link child)
        {
            CheckRefGeometryExists(child);

            string coordSysName = child.Joint.CoordinateSystemName;
            string axisName = child.Joint.AxisName;
            string jointType = child.Joint.Type;

            child.Joint.Parent.Name = parent.Name;
            child.Joint.Child.Name = child.Name;
            if (child.isFixedFrame)
            {
                axisName = "";
                jointType = "fixed";
                child.Joint.Type = jointType;
            }
            else if (!child.Joint.AutoDeriveAxis &&
                !child.Joint.UsesCoordinateSystemAxis &&
                string.IsNullOrEmpty(axisName) &&
                jointType != "fixed")
            {
                ExportErrorWhy = string.Format(
                    "The joint axis is empty for joint {0} from link {1} to {2}. " +
                    "Pick a reference axis, a coordinate-system axis, or enable " +
                    "auto-derive axis from kinematic chain.",
                    child.Joint.Name, child.Name, parent.Name);
                return false;
            }
            else if (string.IsNullOrEmpty(coordSysName) ||
                coordSysName == "Automatically Generate" ||
                child.Joint.AutoDeriveAxis ||
                axisName == "Automatically Generate" ||
                jointType == "Automatically Detect")
            {
                // We have to estimate the joint if the user specifies automatic for either the
                // reference coordinate system, the reference axis or the joint type.
                EstimateGlobalJointFromComponents(parent, child);
                bool autoGenerateError = (
                    child.Joint.Origin.X == 0.0 && child.Joint.Origin.Y == 0.0 && child.Joint.Origin.Z == 0.0 &&
                    child.Joint.Origin.Roll == 0.0 && child.Joint.Origin.Pitch == 0.0 && child.Joint.Origin.Yaw == 0.0);

                if (autoGenerateError)
                {
                    ExportErrorWhy = string.Format("Inferring the joint geometry failed for the joint {0} " +
                        "from link {1} to {2} failed. Check that the mates have not fully defined the " +
                        "components in link {1} and that there is exactly one degree of freedom.",
                        child.Joint.Name, child.Name, parent.Name);
                    return false;
                }
            }

            if (string.IsNullOrEmpty(coordSysName) || coordSysName == "Automatically Generate")
            {
                child.Joint.CoordinateSystemName = "Origin_" + child.Joint.Name;
                ActiveSWModel.ClearSelection2(true);
                int i = 2;
                while (ActiveSWModel.Extension.SelectByID2(
                    child.Joint.CoordinateSystemName, "COORDSYS", 0, 0, 0, false, 0, null, 0))
                {
                    ActiveSWModel.ClearSelection2(true);
                    child.Joint.CoordinateSystemName =
                        "Origin_" + child.Joint.Name + i.ToString();
                    i++;
                }

                CreateRefOrigin(child.Joint);
            }

            if (child.Joint.AutoDeriveAxis || axisName == "Automatically Generate")
            {
                child.Joint.AxisName = "Axis_" + child.Joint.Name;
                ActiveSWModel.ClearSelection2(true);
                int i = 2;
                while (ActiveSWModel.Extension.SelectByID2(
                    child.Joint.AxisName, "AXIS", 0, 0, 0, false, 0, null, 0))
                {
                    ActiveSWModel.ClearSelection2(true);
                    child.Joint.AxisName = "Axis_" + child.Joint.Name + i.ToString();
                    i++;
                }
                if (child.Joint.Type != "fixed")
                {
                    CreateRefAxis(child.Joint);
                }
            }

            EstimateGlobalJointFromRefGeometry(child);

            coordSysName = parent.Joint.CoordinateSystemName;

            LocalizeJoint(child.Joint, coordSysName);
            return true;
        }

        // Creates a Reference Coordinate System in the SolidWorks Model to symbolize the joint location
        private void CreateRefOrigin(Joint Joint)
        {
            CreateRefOrigin(Joint.Origin, Joint.CoordinateSystemName);
        }

        // Creates a Reference Coordinate System in the SolidWorks Model to symbolize the joint location
        private void CreateRefOrigin(Origin Origin, string CoordinateSystemName)
        {
            // Adds the sketch segments and point to the 3D sketch. The sketchEnties are the actual
            // items created (and their locations)
            object[] sketchEntities = AddSketchGeometry(Origin);

            SketchPoint OriginPoint = (SketchPoint)sketchEntities[0];
            SketchSegment xaxis = (SketchSegment)sketchEntities[1];
            SketchSegment yaxis = (SketchSegment)sketchEntities[2];

            double originX = (double)sketchEntities[3]; //OriginPoint X
            double originY = (double)sketchEntities[4];
            double originZ = (double)sketchEntities[5];

            double xAxisX = (double)sketchEntities[6];
            double xAxisY = (double)sketchEntities[7];
            double xAxisZ = (double)sketchEntities[8];

            double yAxisX = (double)sketchEntities[9];
            double yAxisY = (double)sketchEntities[10];
            double yAxisZ = (double)sketchEntities[11];

            ActiveSWModel.ClearSelection2(true);
            SelectionMgr selectionManager = ActiveSWModel.SelectionManager;
            SelectData data = selectionManager.CreateSelectData();

            // First select the origin
            bool SelectedOrigin = false;
            bool SelectedXAxis = false;
            bool SelectedYAxis = false;
            if (OriginPoint != null)
            {
                data.Mark = 1;
                SelectedOrigin = OriginPoint.Select4(true, data);
            }
            if (!SelectedOrigin)
            {
                ActiveSWModel.Extension.SelectByID2(
                    "", "EXTSKETCHPOINT", originX, originY, originZ, true, 1, null, 0);
            }

            // Second, select the xaxis
            if (xaxis != null)
            {
                data.Mark = 2;
                SelectedXAxis = xaxis.Select4(true, data);
            }
            if (!SelectedXAxis)
            {
               ActiveSWModel.Extension.SelectByID2
                 ("", "EXTSKETCHPOINT", xAxisX, xAxisY, xAxisZ, true, 2, null, 0);
            }

            // Third, select the yaxis
            if (yaxis != null)
            {
                data.Mark = 4;
                SelectedYAxis = yaxis.Select4(true, data);
            }
            if (!SelectedYAxis)
            {
                ActiveSWModel.Extension.SelectByID2(
                    "", "EXTSKETCHPOINT", yAxisX, yAxisY, yAxisZ, true, 4, null, 0);
            }

            //From the selected items, insert a coordinate system.
            Feature coordinates =
                ActiveSWModel.FeatureManager.InsertCoordinateSystem(false, false, false);
            if (coordinates != null)
            {
                coordinates.Name = CoordinateSystemName;
            }
        }

        //Creates the Origin_global coordinate system
        private void CreateBaseRefOrigin(bool zIsUp)
        {
            if (!ActiveSWModel.Extension.SelectByID2(
                    "Origin_global", "COORDSYS", 0, 0, 0, false, 0, null, 0))
            {
                Joint Joint = new Joint();
                if (zIsUp)
                {
                    Joint.Origin.SetRPY(new double[] { -Math.PI / 2, 0, 0 });
                }
                else
                {
                    Joint.Origin.SetRPY(new double[] { 0, 0, 0 });
                }
                Joint.Origin.SetXYZ(new double[] { 0, 0, 0 });
                Joint.CoordinateSystemName = "Origin_global";
                EnsureReferenceSketch();
                CreateRefOrigin(Joint);
            }
        }

        // Names of all temporary export-only coord systems we create are prefixed
        // with this so SweepOrphanedExportFrames can find and reap them after a
        // crashed export. Picked to be visually distinct from anything a user
        // would name in SolidWorks.
        private const string TempExportFramePrefix = "__sw_export_";

        // Materializes a unique top-level coord system in the assembly equivalent
        // to the link's joint frame and returns the name to feed
        // swFileSaveAsCoordinateSystem. When the link's stored coord-system name
        // is already at the assembly level (no "<component>" suffix) the existing
        // name is returned and createdTemp is false -- we leave SolidWorks's STL
        // export path untouched in that case.
        //
        // Why this exists: SaveAs's swFileSaveAsCoordinateSystem resolves names
        // against the active document (the assembly). When the user picks a
        // coord system that lives inside a sub-component (e.g.
        // "Coordinate System1 <LINK-5>"), SetLinkSpecificSTLPreferences only
        // sees the bare "Coordinate System1" -- and if the same name exists in
        // multiple sub-component instances (or at none of them at the assembly
        // level), SW silently picks the wrong frame, producing STLs whose
        // geometry sits at a constant wrong offset from the link's body frame.
        // This prevents duplicate sub-component coord-system names from
        // resolving to the wrong assembly-level export frame.
        private string EnsureUniqueAssemblyExportFrame(Link link, out bool createdTemp)
        {
            createdTemp = false;
            string coordsysName = link?.Joint?.CoordinateSystemName;
            if (string.IsNullOrEmpty(coordsysName))
            {
                return coordsysName;
            }

            // No "<component>" suffix means the coord system already lives at
            // the assembly level, where swFileSaveAsCoordinateSystem can resolve
            // it unambiguously. Nothing to do.
            if (!(coordsysName.Contains("<") && coordsysName.Contains(">")))
            {
                return coordsysName;
            }

            MathTransform globalTransform;
            try
            {
                globalTransform = GetCoordinateSystemTransform(coordsysName);
            }
            catch (Exception e)
            {
                logger.Warn("Failed to resolve global transform for " + coordsysName +
                    " on link " + link.Name + "; falling back to bare name (mesh may be misplaced).", e);
                return coordsysName;
            }

            if (globalTransform == null)
            {
                logger.Warn("Resolved global transform for " + coordsysName + " on link " +
                    link.Name + " was null; falling back to bare name (mesh may be misplaced).");
                return coordsysName;
            }

            Origin tempOrigin = new Origin(false);
            tempOrigin.SetXYZ(MathOps.GetXYZ(globalTransform));
            tempOrigin.SetRPY(MathOps.GetRPY(globalTransform));

            string sanitisedLinkName = SanitiseForFeatureName(link.Name);
            string uniqueSuffix = Guid.NewGuid().ToString("N").Substring(0, 8);
            string tempName = TempExportFramePrefix + sanitisedLinkName + "_" + uniqueSuffix;

            EnsureReferenceSketch();

            try
            {
                CreateRefOrigin(tempOrigin, tempName);
            }
            catch (Exception e)
            {
                logger.Warn("Failed to create temporary export coord system " + tempName +
                    " for link " + link.Name + "; falling back to bare name (mesh may be misplaced).", e);
                return coordsysName;
            }

            // Sanity check: confirm the feature actually got created with the
            // requested name. SW silently no-ops InsertCoordinateSystem in some
            // failure modes (e.g. invalid sketch entity selection).
            if (!ActiveSWModel.Extension.SelectByID2(
                    tempName, "COORDSYS", 0, 0, 0, false, 0, null, 0))
            {
                logger.Warn("Temporary export coord system " + tempName +
                    " was not found after creation for link " + link.Name +
                    "; falling back to bare name (mesh may be misplaced).");
                return coordsysName;
            }
            ActiveSWModel.ClearSelection2(true);

            createdTemp = true;
            return tempName;
        }

        // Removes a temporary export coord system created by
        // EnsureUniqueAssemblyExportFrame. Safe to call with any name; logs and
        // swallows failures (the orphan sweep on the next export is the safety
        // net).
        private void DeleteTempExportFrame(string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            // Defensive guard: only ever delete features we own. Avoids
            // catastrophic data loss if a caller somehow passed a user-owned
            // coord system name.
            if (!name.StartsWith(TempExportFramePrefix))
            {
                logger.Warn("Refusing to delete coord system '" + name + "' -- name does not " +
                    "start with the temporary-export prefix '" + TempExportFramePrefix + "'.");
                return;
            }

            try
            {
                ActiveSWModel.ClearSelection2(true);
                bool selected = ActiveSWModel.Extension.SelectByID2(
                    name, "COORDSYS", 0, 0, 0, false, 0, null, 0);
                if (!selected)
                {
                    logger.Warn("Could not select temporary export coord system '" + name +
                        "' for cleanup; orphan sweep will reap it next export.");
                    return;
                }
                ActiveSWModel.EditDelete();
                ActiveSWModel.ClearSelection2(true);
            }
            catch (Exception e)
            {
                logger.Warn("Exception while deleting temporary export coord system '" + name +
                    "'; orphan sweep will reap it next export.", e);
            }
        }

        // Removes any leftover __sw_export_* coord systems at the assembly top
        // level. Called once at the start of each export so a crashed prior
        // export does not pollute the assembly indefinitely.
        public void SweepOrphanedExportFrames()
        {
            // Only meaningful for assemblies; the part exporter does not create
            // these temporaries.
            if (ActiveSWModel == null ||
                ActiveSWModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return;
            }

            List<string> orphans = new List<string>();
            try
            {
                // topLevelOnly = true: temporaries are always created at the
                // assembly root, never inside sub-components.
                Dictionary<string, List<Feature>> features =
                    GetFeaturesOfType("CoordSys", true);
                if (features != null)
                {
                    foreach (KeyValuePair<string, List<Feature>> kvp in features)
                    {
                        if (kvp.Value == null) continue;
                        foreach (Feature feat in kvp.Value)
                        {
                            if (feat == null || feat.Name == null) continue;
                            if (feat.Name.StartsWith(TempExportFramePrefix))
                            {
                                orphans.Add(feat.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logger.Warn("Failed to enumerate top-level coord systems while sweeping orphans; " +
                    "skipping cleanup this export.", e);
                return;
            }

            if (orphans.Count == 0)
            {
                return;
            }

            logger.Info("Sweeping " + orphans.Count + " orphaned export coord system(s) from prior runs: " +
                string.Join(", ", orphans));
            foreach (string name in orphans)
            {
                DeleteTempExportFrame(name);
            }
        }

        // Restricted-charset sanitiser for SolidWorks feature names. We only
        // accept letters, digits, '-' and '_'; everything else collapses to
        // '_'. Length is capped so feature names stay legible in the SW tree.
        private static string SanitiseForFeatureName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "link";
            }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in raw.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            string result = sb.ToString();
            if (result.Length > 40)
            {
                result = result.Substring(0, 40);
            }
            return result;
        }

        // Creates a Reference Axis to be used to calculate the joint axis
        private void CreateRefAxis(Joint Joint)
        {
            //Adds sketch segment
            SketchSegment rotaxis = AddSketchGeometry(Joint.Axis, Joint.Origin, Joint.CoordinateSystemName);
            if (rotaxis != null)
            {
                //Use special method to create the axis
                Feature featAxis = InsertAxis(rotaxis);
                if (featAxis != null)
                {
                    featAxis.Name = Joint.AxisName;
                }
            }
        }

        // Takes a links joint and calculates the local transform from the global transforms of
        // the parent and child. It also converts the axis to local values
        private void LocalizeJoint(Joint Joint, string parentCoordsysName)
        {
            MathTransform parentTransform = GetCoordinateSystemTransform(parentCoordsysName);
            
            Matrix<double> ParentJointGlobalTransform =
                MathOps.GetTransformation(parentTransform);
            MathTransform coordsysTransform =
                GetCoordinateSystemTransform(Joint.CoordinateSystemName);
           
            //Transform from global origin to child joint
            Matrix<double> ChildJointGlobalTransform =
                MathOps.GetTransformation(coordsysTransform);
            Matrix<double> ChildJointOrigin =
                ParentJointGlobalTransform.Inverse() * ChildJointGlobalTransform;
            
            //Localize the axis to the Link's coordinate system.
            Joint.Axis.SetXYZ(LocalizeAxis(Joint.Axis.GetXYZ(), Joint.CoordinateSystemName));

            // Get the array values and threshold them so small values are set to 0.
            Joint.Origin.SetXYZ(MathOps.GetXYZ(ChildJointOrigin));
            Joint.Origin.SetXYZ(MathOps.Threshold(Joint.Origin.GetXYZ(), 0.00001));
            Joint.Origin.SetRPY(MathOps.GetRPY(ChildJointOrigin));
            Joint.Origin.SetRPY(MathOps.Threshold(Joint.Origin.GetRPY(), 0.00001));
        }

        // Funny method I created that inserts a RefAxis and then finds the reference to it.
        private Feature InsertAxis(SketchSegment axis)
        {
            //First select the axis
            SelectData data = ActiveSWModel.SelectionManager.CreateSelectData();
            axis.Select4(false, data);

            //Get the features before the axis is created
            object[] featuresBefore, featuresAfter;
            featuresBefore = ActiveSWModel.FeatureManager.GetFeatures(true);
            
            //Create the axis
            ActiveSWModel.InsertAxis2(true);

            //Get the features after the axis is created
            featuresAfter = ActiveSWModel.FeatureManager.GetFeatures(true);
            
            // If it was created, try to find it
            if (featuresBefore.Length < featuresAfter.Length)
            {
                //It was probably added at the end (hence .Reverse())
                foreach (Feature feat in featuresAfter.Cast<Feature>().Reverse())
                {
                    //If the feature in featuresAfter is not in features before, its gotta be the
                    // axis we inserted
                    if (!featuresBefore.Contains(feat))
                    {
                        return feat;
                    }
                }
            }
            return null;
        }

        // Inserts a sketch into the main assembly and name it
        private string Setup3DSketch()
        {
            bool sketchExists =
                ActiveSWModel.Extension.SelectByID2(
                    "Robot Description Reference", "SKETCH", 0, 0, 0, false, 0, null, 0);
            ActiveSWModel.SketchManager.Insert3DSketch(true);
            ActiveSWModel.SketchManager.CreatePoint(0, 0, 0);
            IFeature sketch = (IFeature)ActiveSWModel.SketchManager.ActiveSketch;
            ActiveSWModel.SketchManager.Insert3DSketch(true);
            if (!sketchExists)
            {
                sketch.Name = "Robot Description Reference";
            }
            return sketch.Name;
        }

        // Guarantees the internal "Robot Description Reference" 3D sketch that
        // every auto-generated reference origin / axis is drawn into exists.
        // Must be called before any AddSketchGeometry path. referenceSketchName
        // is only initialized lazily, and the base-link initializer
        // (CreateBaseRefOrigin) is skipped whenever the user supplies an
        // explicit global origin coord system - so a child joint left on
        // "Automatically Generate" would otherwise hit AddSketchGeometry with a
        // null referenceSketchName and throw "Reference sketch  does not exist".
        private void EnsureReferenceSketch()
        {
            if (referenceSketchName == null)
            {
                referenceSketchName = Setup3DSketch();
            }
        }

        // Adds lines and a point to create the entities for a reference coordinates
        private object[] AddSketchGeometry(Origin Origin)
        {
            EnsureReferenceSketch();
            //Find if the sketch exists first
            if (ActiveSWModel.SketchManager.ActiveSketch == null)
            {
                bool sketchExists =
                    ActiveSWModel.Extension.SelectByID2(
                        referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
                if (!sketchExists)
                {
                    // EnsureReferenceSketch() above guarantees referenceSketchName
                    // is set, so reaching here means the helper sketch was deleted
                    // mid-export. Recreate it rather than aborting the whole export.
                    logger.Warn("Reference sketch '" + (referenceSketchName ?? "<null>") +
                        "' was missing when building an auto-generated joint frame; recreating it.");
                    referenceSketchName = Setup3DSketch();
                    if (!ActiveSWModel.Extension.SelectByID2(
                            referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0))
                    {
                        throw new Exception(
                            "Could not create the internal reference sketch needed to auto-generate a " +
                            "joint coordinate system. This happens when a joint is left on " +
                            "\"Automatically Generate\" for its reference coordinate system and the " +
                            "exporter cannot insert a 3D sketch into the active assembly. Assign an " +
                            "explicit reference coordinate system to the affected joint, or ensure the " +
                            "active document is an editable assembly, and try again.");
                    }
                }
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }

            //Calculate the lines that need to be drawn
            Matrix<double> transform = MathOps.GetRotation(Origin.GetRPY());
            Matrix<double> Axes = 0.01 * DenseMatrix.CreateIdentity(4);
            Matrix<double> tA = transform * Axes;

            // origin at X, Y, Z
            SketchPoint OriginPoint = ActiveSWModel.SketchManager.CreatePoint(Origin.X,
                                                                      Origin.Y,
                                                                      Origin.Z);

            // xAxis is a 1cm line from the origin in the direction of the xaxis of the coordinate system
            SketchSegment XAxis = ActiveSWModel.SketchManager.CreateLine(Origin.X,
                                                                         Origin.Y,
                                                                         Origin.Z,
                                                                         Origin.X + tA[0, 0],
                                                                         Origin.Y + tA[1, 0],
                                                                         Origin.Z + tA[2, 0]);
            XAxis.ConstructionGeometry = true;

            //yAxis is a 1cm line from the origin in the direction of the yaxis of the coordinate system
            SketchSegment YAxis = ActiveSWModel.SketchManager.CreateLine(Origin.X,
                                                                         Origin.Y,
                                                                         Origin.Z,
                                                                         Origin.X + tA[0, 1],
                                                                         Origin.Y + tA[1, 1],
                                                                         Origin.Z + tA[2, 1]);
            YAxis.ConstructionGeometry = true;

            //Close the sketch
            if (ActiveSWModel.SketchManager.ActiveSketch != null)
            {
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }
            // Return an array of objects representing the sketch items that were just inserted,
            // as well as the actual locations of those objecs (aids selection).
            return new object[] { OriginPoint, XAxis, YAxis,
                Origin.X, Origin.Y, Origin.Z,
                Origin.X + tA[0, 0], Origin.Y + tA[1, 0], Origin.Z + tA[2, 0],
                Origin.X + tA[0, 1], Origin.Y + tA[1, 1], Origin.Z + tA[2, 1] };
        }

        //Inserts a sketch segment for use when creating a Reference Axis
        private SketchSegment AddSketchGeometry(Axis axis, Origin origin, string coordSysName)
        {
            EnsureReferenceSketch();
            if (ActiveSWModel.SketchManager.ActiveSketch == null)
            {
                ActiveSWModel.Extension.SelectByID2(
                    referenceSketchName, "SKETCH", 0, 0, 0, false, 0, null, 0);
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }

            bool flip = CheckReverseAxis(axis, coordSysName);
            double sign = (flip) ? -1.0 : 1.0;

            //Insert sketch segment 0.1m long centered on the origin.
            SketchSegment rotAxis = ActiveSWModel.SketchManager.CreateLine(
                origin.X + sign * 0.05 * axis.X,
                origin.Y + sign * 0.05 * axis.Y,
                origin.Z + sign * 0.05 * axis.Z,
                origin.X - sign * 0.05 * axis.X,
                origin.Y - sign * 0.05 * axis.Y,
                origin.Z - sign * 0.05 * axis.Z);
            if (rotAxis == null)
            {
                return null;
            }
            rotAxis.ConstructionGeometry = true;
            rotAxis.Width = 2;

            //Close sketch
            if (ActiveSWModel.SketchManager.ActiveSketch != null)
            {
                ActiveSWModel.SketchManager.Insert3DSketch(true);
            }
            return rotAxis;
        }

        // Checks if an axis to be created should be flipped, so as to favor positive directions of rotation
        // This prefers that the first non-zero value be positive
        private bool CheckReverseAxis(Axis axis, string coordSysName)
        {
            //axis is a double[] {x, y, z}
            double[] transformedAxis = LocalizeAxis(axis.GetXYZ(), coordSysName);

            // If x is negative, flip
            if (transformedAxis[0] < 0)
            {
                return true;
            }
            // Else if x is 0 and y is negative, flip
            else if (Math.Abs(transformedAxis[0]) < 0.00001 && transformedAxis[1] < 0)
            {
                return true;
            }
            // Else if x and y are 0 and z is negative, flip
            else if (Math.Abs(transformedAxis[0]) < 0.00001 &&
                     Math.Abs(transformedAxis[1]) < 0.00001 &&
                     transformedAxis[2] < 0)
            {
                return true;
            }
            return false;
        }

        //Calculates the free degree of freedom (if exists), and then determines the location of the joint,
        // the axis of rotation/translation, and the type of joint
        public Boolean EstimateGlobalJointFromComponents(Link parent, Link child)
        {
            //Create the ref objects
            int degreesOfFreedom;

            // Fix parent components so that only the actual degree of freedom
            // can be detected. Tier B2: FixComponents records what it fixed in
            // sessionFixedComponents and skips ancestors already fixed earlier
            // this pass; the bulk unfix happens once in CreateRobotFromTreeView's
            // finally (UnfixSessionFixedComponents), not per joint here.
            FixComponents(parent);

            // Surpress Limit Mates to properly find degrees of freedom. They don't work with the API call
            List<Mate2> limitMates = SuppressLimitMates(child.SWMainComponent);
            Boolean success = false;
            if (child.SWMainComponent != null)
            {
                // The wonderful undocumented API call I found to get the degrees of freedom in a joint.
                // https://forum.solidworks.com/thread/57414
                int remainingDOFs =
                    child.SWMainComponent.GetRemainingDOFs(
                        out int R1Status, out MathPoint RPoint1, out int R1DirStatus, out MathVector RDir1,
                        out int R2Status, out MathPoint RPoint2, out int R2DirStatus, out MathVector RDir2,
                        out int L1Status, out MathVector LDir1,
                        out int L2Status, out MathVector LDir2);
                if (RPoint1 != null)
                {
                    logger.Info("R1: " + R1Status + ", " + RPoint1 + ", " + R1DirStatus + ", " + RDir1.ArrayData);
                }
                else
                {
                    logger.Info("R1: " + R1Status + ", " + R1DirStatus);
                }

                if (RPoint2 != null)
                {
                    logger.Info("R2: " + R2Status + ", " + RPoint2 + ", " + R2DirStatus + ", " + RDir2.ArrayData);
                }
                else
                {
                    logger.Info("R2: " + R2Status + ", " + R2DirStatus);
                }
                if (LDir1 != null)
                {
                    logger.Info("L1: " + L1Status + ", " + LDir1.ArrayData);
                }
                else
                {
                    logger.Info("L1: " + L1Status);
                }
                if (LDir2 != null)
                {
                    logger.Info("L2: " + ", " + LDir2.ArrayData);
                }
                else
                {
                    logger.Info("L2: " + L2Status);
                }

                degreesOfFreedom = remainingDOFs;

                // Convert the gotten degrees of freedom to a joint type, origin and axis.
                // Use the root-relative total transform (reflects flexible /
                // nested sub-assembly repositioning), NOT Transform2 -- same
                // invariant as ResolveFeatureReference. Resolve once to avoid
                // repeated COM round trips; fall back to Transform2 only if SW
                // cannot compute the total transform.
                MathTransform mainCompTransform =
                    child.SWMainComponent.GetTotalTransform(true) ?? child.SWMainComponent.Transform2;
                child.Joint.Type = "fixed";
                child.Joint.Origin.SetXYZ(MathOps.GetXYZ(mainCompTransform));
                child.Joint.Origin.SetRPY(MathOps.GetRPY(mainCompTransform));

                if (degreesOfFreedom == 0 && (R1Status + L1Status > 0))
                {
                    success = true;
                    if (R1Status == 1)
                    {
                        child.Joint.Type = "continuous";
                        child.Joint.Axis.SetXYZ(RDir1.ArrayData);
                        child.Joint.Origin.SetXYZ(RPoint1.ArrayData);
                        child.Joint.Origin.SetRPY(MathOps.GetRPY(mainCompTransform));
                        MoveOrigin(parent, child);
                    }
                    else if (L1Status == 1)
                    {
                        child.Joint.Type = "prismatic";
                        child.Joint.Axis.SetXYZ(LDir1.ArrayData);
                        child.Joint.Origin.SetXYZ(MathOps.GetXYZ(mainCompTransform));
                        child.Joint.Origin.SetRPY(MathOps.GetRPY(mainCompTransform));
                        MoveOrigin(parent, child);
                    }
                }
                child.Joint.Origin.SetXYZ(MathOps.Threshold(child.Joint.Origin.GetXYZ(), 0.00001));
                child.Joint.Origin.SetRPY(MathOps.Threshold(child.Joint.Origin.GetRPY(), 0.00001));
                UnsuppressLimitMates(limitMates);
                // Per-joint AutoComputeLimits gates the SW-mate derivation
                // for this specific joint. The global ComputeJointLimits
                // flag is now test-only (TestExportHelper exercises the
                // false path); production gates exclusively via the
                // per-joint toggle.
                if (limitMates.Count > 0 && ComputeJointLimits && child.Joint.AutoComputeLimits)
                {
                    AddLimits(child.Joint, limitMates, parent.SWMainComponent, child.SWMainComponent);
                }
            }

            return success;
        }

        //This now needs to be able to get the component, and it's associated coordinate system name.
        //Then it needs to transform to the top level assembly (sounds like fun).
        private void EstimateGlobalJointFromRefGeometry(Link child)
        {
            MathTransform GlobalCoordsysTransform =
                GetCoordinateSystemTransform(child.Joint.CoordinateSystemName);
            if (GlobalCoordsysTransform == null)
            {
                logger.Warn(
                    string.Format("Joint transform for coordinate system {0} could not be computed for joint {1}", 
                        child.Joint.CoordinateSystemName, child.Joint.Name));
                return;
            }
            child.Joint.Origin.SetXYZ(MathOps.GetXYZ(GlobalCoordsysTransform));
            child.Joint.Origin.SetRPY(MathOps.GetRPY(GlobalCoordsysTransform));
            if (child.Joint.Type != "fixed")
            {
                EstimateAxis(child.Joint);
            }
        }

        // Bundle returned by ResolveFeatureReference: enough information to query
        // a coord system / axis feature in the right document AND configuration
        // and then map the result back to assembly-global space.
        private struct ResolvedFeatureReference
        {
            // Document inside which the feature lives. Equals ActiveSWModel for
            // top-level (assembly-scope) features; the part doc for sub-component
            // features.
            public ModelDoc2 OwningDoc;
            // Bare feature name with the "<...>" suffix stripped.
            public string FeatureName;
            // null for top-level features; comp.GetTotalTransform(true) for
            // sub-component features so callers can multiply local -> assembly
            // ROOT global. Must be the total (root) transform, NOT Transform2:
            // Transform2 does not reflect flexible sub-assembly repositioning or
            // multi-level nesting, so the feature would resolve at the
            // sub-assembly's stored layout instead of its moved top-level pose.
            public MathTransform ComponentTransform;
            // null for top-level features; comp.ReferencedConfiguration for
            // sub-component features so callers can switch the part doc to the
            // right config before reading.
            public string ConfigurationName;
            // null for top-level features; the owning Component2 for
            // sub-component features. Used by GetRefAxis / GetReferencePoint to
            // convert a model-context feature pointer into the ASSEMBLY context
            // via IComponent2.GetCorresponding, which yields IN-CONTEXT (flexible
            // sub-assembly-aware) reference-axis params / reference-point
            // positions in root-global coordinates.
            public Component2 OwningComponent;
        }

        // Single source of truth for "Coordinate System 1 <Comp-Name>"-style
        // references. Parses the suffix, locates the matching Component2 in
        // the assembly, and returns the doc / bare name / component transform
        // / referenced config bundled together. Used by
        // GetCoordinateSystemTransform and GetRefAxis -- both used to inline
        // the same parse + lookup loop, with the latent gotcha that
        // comp.ReferencedConfiguration was never captured (so config-dependent
        // features like coord systems anchored to length-driven dimensions
        // were always read in the part doc's currently-active configuration,
        // typically Default, regardless of which configuration the assembly
        // instance referenced).
        //
        // ComponentTransform is captured via comp.GetTotalTransform(true)
        // (component-local -> root-assembly), the SAME primitive the mesh
        // tessellation path uses (SaveSTLViaTessellation). This is the
        // root-relative total transform: it composes every parent
        // sub-assembly transform AND reflects flexible sub-assembly internal
        // repositioning driven by top-level mates, so a coord system / axis /
        // reference point that lives inside a moved (flexible or deeply
        // nested) sub-assembly resolves at its actual top-level pose. Do NOT
        // revert to comp.Transform2 here: Transform2 is the stored/immediate
        // transform and ignores flexible repositioning, which silently exports
        // joints at the original sub-assembly layout while the meshes (which
        // use GetTotalTransform) land at the moved pose.
        //
        // KNOWN LIMITATION: per SOLIDWORKS forum guidance,
        // IComponent2.ReferencedConfiguration is unreliable for components
        // nested below the top-level assembly. assy.GetComponents(false) does
        // recurse into sub-assemblies, so a deep match is possible here, but
        // the resulting ConfigurationName may not reflect what the user
        // expects. If a deep-nested-config use case shows up later, switch to
        // IAssemblyDoc.CompConfigProperties4.
        private ResolvedFeatureReference ResolveFeatureReference(string nameWithSuffix)
        {
            ResolvedFeatureReference r = new ResolvedFeatureReference
            {
                OwningDoc = ActiveSWModel,
                FeatureName = nameWithSuffix,
                ComponentTransform = null,
                ConfigurationName = null,
            };
            if (string.IsNullOrEmpty(nameWithSuffix)) return r;
            if (!(nameWithSuffix.Contains("<") && nameWithSuffix.Contains(">"))) return r;

            int indexFirst = nameWithSuffix.IndexOf('<');
            int indexLast = nameWithSuffix.IndexOf('>', indexFirst);
            if (indexLast <= indexFirst) return r;

            string componentStr = nameWithSuffix.Substring(indexFirst + 1, indexLast - indexFirst - 1);
            r.FeatureName = nameWithSuffix.Substring(0, indexFirst).Trim();

            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
            object[] components = assy.GetComponents(false);
            foreach (Component2 comp in components)
            {
                if (comp.Name2 == componentStr)
                {
                    r.OwningDoc = comp.GetModelDoc2();
                    r.OwningComponent = comp;
                    // Root-relative total transform (reflects flexible / nested
                    // sub-assembly repositioning); fall back to the immediate
                    // Transform2 only if SW cannot compute the total transform.
                    r.ComponentTransform = comp.GetTotalTransform(true) ?? comp.Transform2;
                    r.ConfigurationName = comp.ReferencedConfiguration;
                    break;
                }
            }
            return r;
        }

        // Switch/restore wrapper: ensures `partDoc` is in the named
        // configuration while `action` runs, then restores the prior
        // configuration in a finally block. No-ops cleanly when there is
        // nothing to switch (partDoc is null/the assembly, configName is
        // empty, or it already matches). The query SOLIDWORKS APIs we care
        // about (GetCoordinateSystemTransformByName, SelectByID2 for AXIS)
        // implicitly read from the doc's currently-active configuration --
        // there is no overload that accepts a config parameter. Hence the
        // switch.
        private T WithComponentConfiguration<T>(ModelDoc2 partDoc, string configName, Func<T> action)
        {
            if (partDoc == null || partDoc == ActiveSWModel) return action();

            // Live preview path: never mutate the part document. Switching
            // configuration via ShowConfiguration2 while the PropertyManager
            // page is open closes/crashes the page, and a preview only needs
            // a best-effort transform in whatever configuration is currently
            // active. Export resolution leaves the flag false.
            if (suppressConfigSwitchForFeatureLookup) return action();

            string savedConfig = null;
            bool switched = false;
            try
            {
                savedConfig = partDoc.ConfigurationManager?.ActiveConfiguration?.Name;
                if (!string.IsNullOrEmpty(configName) &&
                    !string.Equals(configName, savedConfig, StringComparison.Ordinal))
                {
                    if (partDoc.ShowConfiguration2(configName))
                    {
                        switched = true;
                        logger.Info("Switched " + partDoc.GetTitle() + " from config '" +
                            savedConfig + "' to '" + configName + "' for feature lookup");
                    }
                    else
                    {
                        logger.Warn("ShowConfiguration2('" + configName + "') failed on " +
                            partDoc.GetTitle() + "; querying in current config '" + savedConfig + "' instead.");
                    }
                }
                return action();
            }
            finally
            {
                if (switched && !string.IsNullOrEmpty(savedConfig))
                {
                    try { partDoc.ShowConfiguration2(savedConfig); }
                    catch (Exception e)
                    {
                        logger.Warn("Failed to restore active configuration '" + savedConfig +
                            "' on " + partDoc.GetTitle(), e);
                    }
                }
            }
        }

        private IDisposable BeginFeatureLookupCache()
        {
            if (featureLookupCacheDepth == 0)
            {
                coordinateSystemTransformCache.Clear();
                referenceAxisCache.Clear();
            }

            featureLookupCacheDepth++;
            return new FeatureLookupCacheScope(this);
        }

        private bool IsFeatureLookupCacheEnabled()
        {
            return featureLookupCacheDepth > 0;
        }

        private void EndFeatureLookupCache()
        {
            featureLookupCacheDepth--;
            if (featureLookupCacheDepth <= 0)
            {
                featureLookupCacheDepth = 0;
                coordinateSystemTransformCache.Clear();
                referenceAxisCache.Clear();
            }
        }

        private static string FeatureLookupCacheKey(string role, ResolvedFeatureReference r)
        {
            string docKey = "";
            if (r.OwningDoc != null)
            {
                docKey = r.OwningDoc.GetPathName();
                if (string.IsNullOrEmpty(docKey))
                {
                    docKey = r.OwningDoc.GetTitle();
                }
            }

            return role + "|" + docKey + "|" + (r.ConfigurationName ?? "") + "|" + (r.FeatureName ?? "");
        }

        private sealed class FeatureLookupCacheScope : IDisposable
        {
            private ExportHelper owner;

            public FeatureLookupCacheScope(ExportHelper owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                if (owner != null)
                {
                    owner.EndFeatureLookupCache();
                    owner = null;
                }
            }
        }

        // Method to get the SolidWorks MathTransform from a coordinate system. This method can account for
        // coordinate systems that are embedded in subcomponents, and apply the correct transformation to return
        // it to a global transform. It assumes that the coordinate system name is formatted like:
        // "Coordinate System 1 <assy/subassy/comp>" where the full Component2.Name2 is between the <>
        private MathTransform GetCoordinateSystemTransform(string CoordinateSystemName)
        {
            if (CoordinateSystemName == null)
            {
                throw new Exception("Coordinate system string is null");
            }

            ResolvedFeatureReference r = ResolveFeatureReference(CoordinateSystemName);

            // PRIMARY PATH for sub-component coordinate systems: resolve the
            // coord-sys feature IN THE ASSEMBLY CONTEXT (see ResolveInContextFeature
            // - FeatureByName / GetCorresponding) and read its transform via
            // ICoordinateSystemFeatureData.Transform. The assembly-context
            // definition reflects flexible sub-assembly repositioning (the parts'
            // moved top-level pose). NOTE: GetCoordinateSystemTransformByName
            // CANNOT be used for this - per SW docs it always returns the
            // transform in the DEFINING DOCUMENT's frame (standalone), even with
            // a component-qualified name, which is exactly the original bug. The
            // assembly-context transform is already root-global, so there is NO
            // component-transform multiply. Falls through to the standalone
            // compose path when the in-context resolution is unavailable.
            if (r.OwningComponent != null)
            {
                MathTransform inContext = TryGetInContextCoordSysTransform(r);
                if (inContext != null)
                {
                    return inContext;
                }
            }

            string cacheKey = FeatureLookupCacheKey("coord", r);
            MathTransform local;
            if (IsFeatureLookupCacheEnabled() &&
                coordinateSystemTransformCache.TryGetValue(cacheKey, out local))
            {
                logger.Info("Coordinate system lookup cache hit for " + r.FeatureName +
                    " in config '" + (r.ConfigurationName ?? "") + "'");
            }
            else
            {
                local = WithComponentConfiguration(
                    r.OwningDoc, r.ConfigurationName,
                    () => r.OwningDoc.Extension.GetCoordinateSystemTransformByName(r.FeatureName));
                if (IsFeatureLookupCacheEnabled() && local != null)
                {
                    coordinateSystemTransformCache[cacheKey] = local;
                }
            }

            MathTransform composed =
                r.ComponentTransform == null ? local : local.Multiply(r.ComponentTransform);

            return composed;
        }

        // In-context coordinate-system transform for a coord system that lives
        // inside a (possibly flexible) sub-assembly.
        //
        // Unlike reference axes / points (whose GetRefAxisParams / GetRefPoint
        // EVALUATE the geometry, so an assembly-context feature pointer yields
        // the flexed pose directly), a coordinate system exposes only its
        // DEFINITION (ICoordinateSystemFeatureData.Transform / the by-name
        // getter), which is always in the defining document's frame (standalone,
        // un-flexed) regardless of how the feature pointer was obtained. SW has
        // NO evaluated coord-sys transform getter (verified by reflection).
        //
        // So we RECONSTRUCT the flexed pose: a coordinate system is rigidly
        // attached to the part its origin references, and that leaf part's
        // GetTotalTransform(true) DOES reflect flexible repositioning. We compute
        // the part's flex delta (flexed-global * unflexed-global^-1) and apply it
        // to the standalone-composed coord-sys transform:
        //   coordsysFlexed = coordsysUnflexed expressed in anchor frame, then
        //                    lifted by the anchor's flexed-global pose.
        // Returns null (caller falls back to the standalone compose path, which
        // is correct when nothing moved) if the anchor cannot be resolved.
        //
        // LIMITATION: single-anchor. A coord system whose origin and axes
        // reference DIFFERENT moving parts is only approximated (anchored to the
        // origin part). Pure-axis joints avoid this entirely via the evaluated
        // reference-axis path; document a top-of-assembly coord system if exact
        // multi-part flex behaviour is required.
        private MathTransform TryGetInContextCoordSysTransform(ResolvedFeatureReference r)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                MathTransform subAsmGlobal = r.ComponentTransform;

                MathTransform localStd = WithComponentConfiguration(
                    r.OwningDoc, r.ConfigurationName,
                    () => r.OwningDoc.Extension.GetCoordinateSystemTransformByName(r.FeatureName));
                if (localStd == null)
                {
                    return null;
                }
                MathTransform coordsysUnflexedGlobal =
                    subAsmGlobal == null ? localStd : (MathTransform)localStd.Multiply(subAsmGlobal);

                Feature coordSysFeat = FindNamedFeature(r.OwningDoc, "CoordSys", r.FeatureName);

                // FAST PATH (no AccessSelections / dependency-tree walk): derive the
                // origin from the coord system's defining reference POINT evaluated
                // in-context, and the basis from its defining reference AXES evaluated
                // in-context. Both reads are cheap (GetRefPoint / GetRefAxisParams on
                // an in-context-resolved feature, then lifted by ComponentTransform),
                // whereas the anchor carry below calls AccessSelections on every
                // parent feature, which puts each into edit mode and costs tens of
                // seconds on a flexible sub-assembly. When the coord system supplies
                // both a reference point (origin) and reference axes (basis) - the
                // common case for joint/site coord systems - we skip the anchor walk
                // entirely.
                double[] originGlobal = TryGetInContextCoordSysOriginGlobal(r, coordSysFeat);
                double[][] basisCols = RefineCoordSysBasisColumns(r, coordSysFeat, coordsysUnflexedGlobal);
                if (originGlobal != null && basisCols != null)
                {
                    // FAST path: no AccessSelections anchor walk. If a future
                    // coord-sys is unexpectedly slow, look for "anchor-carry
                    // fallback" below instead - that path runs AccessSelections.
                    logger.Info("In-context coord-sys '" + r.FeatureName +
                        "' resolved via fast reference-point+axis path in " +
                        sw.ElapsedMilliseconds + " ms.");
                    return ComposeCoordSysTransform(basisCols, originGlobal);
                }

                // SLOW FALLBACK: anchor-part flex carry. Needed only when the coord
                // system lacks a defining reference point (origin) or defining axes
                // (basis) we can evaluate in-context. Fills in whichever component
                // (origin / basis) the fast path could not supply.
                logger.Info("In-context coord-sys '" + r.FeatureName +
                    "' missing fast " + (originGlobal == null ? "origin (no reference point)" : "")
                    + (originGlobal == null && basisCols == null ? " and " : "")
                    + (basisCols == null ? "basis (no evaluable axis)" : "")
                    + "; using anchor-carry fallback (runs AccessSelections, may be slow).");
                // Surface the slow path on whatever busy indicator is active (the
                // export bar during export, or the live-preview scope during a
                // PMP coord-sys pick); no-op if none.
                SwProgress.SetTitle("Resolving coordinate system in flexible subassembly...");
                MathTransform carriedFlexed = AnchorCarryCoordSysTransform(r, coordsysUnflexedGlobal);
                if (carriedFlexed == null)
                {
                    // No anchor either; return whatever the fast path did produce so
                    // a partial in-context result still beats the standalone read.
                    if (originGlobal != null && basisCols != null)
                    {
                        return ComposeCoordSysTransform(basisCols, originGlobal);
                    }
                    return null;
                }

                double[] cf = carriedFlexed.ArrayData as double[];
                double[] finalOrigin = originGlobal
                    ?? (cf != null ? new[] { cf[9], cf[10], cf[11] } : new[] { 0.0, 0.0, 0.0 });
                double[][] finalBasis = basisCols ?? (cf != null
                    ? new[]
                    {
                        new[] { cf[0], cf[1], cf[2] },
                        new[] { cf[3], cf[4], cf[5] },
                        new[] { cf[6], cf[7], cf[8] },
                    }
                    : null);
                if (finalBasis == null)
                {
                    return carriedFlexed;
                }
                return ComposeCoordSysTransform(finalBasis, finalOrigin);
            }
            catch (Exception ex)
            {
                logger.Warn("In-context coord-sys resolution for '" + r.FeatureName +
                    "' failed (" + ex.Message + "); falling back to standalone read.");
                return null;
            }
        }

        // In-context coord-system ORIGIN: read the coord system's defining reference
        // POINT in-context (reflecting the flexible solve) and lift it to root-global.
        // This is the cheap replacement for the anchor dependency-tree walk on the
        // origin (translation) axis - a reference point evaluates in-context in ~2 ms
        // via GetRefPoint, vs tens of seconds for the AccessSelections-based anchor
        // carry. Returns null when the coord system has no direct-parent reference
        // point (caller falls back to the anchor carry for the origin).
        private double[] TryGetInContextCoordSysOriginGlobal(
            ResolvedFeatureReference r, Feature coordSysFeat)
        {
            if (coordSysFeat == null)
            {
                return null;
            }
            var pointNames = DirectParentFeatureNames(coordSysFeat, "RefPoint");
            foreach (string pointName in pointNames)
            {
                ResolvedFeatureReference pr = r;
                pr.FeatureName = pointName;
                double[] g = TryGetInContextPointGlobal(pr);
                if (g != null)
                {
                    return g;
                }
            }
            return null;
        }

        // Composes a coord-system MathTransform from three basis columns (X/Y/Z) and
        // a global origin, in SW's 16-element column-major layout.
        private MathTransform ComposeCoordSysTransform(double[][] basisCols, double[] origin)
        {
            if (basisCols == null || basisCols.Length < 3 || origin == null)
            {
                return null;
            }
            double[] data =
            {
                basisCols[0][0], basisCols[0][1], basisCols[0][2],
                basisCols[1][0], basisCols[1][1], basisCols[1][2],
                basisCols[2][0], basisCols[2][1], basisCols[2][2],
                origin[0], origin[1], origin[2],
                1.0, 0.0, 0.0, 0.0,
            };
            return swMath?.CreateTransform(data) as MathTransform;
        }

        // Anchor-part flex carry for a coord system: rigidly carries the standalone
        // coord-sys frame by the motion of the ORIGIN part (the leaf component its
        // origin references). Correct for the origin (translation) and for a
        // single-part coord system, but NOT for a basis whose axes are RELATIVE
        // directions spanning two moving parts. EXPENSIVE - GetCoordSysAnchorComponent
        // calls AccessSelections on the dependency tree (each puts a feature into edit
        // mode); only used as the fallback when the fast reference-point / reference-
        // axis path cannot supply the origin or basis. Returns null when the anchor
        // cannot be resolved (caller falls back to the standalone read).
        private MathTransform AnchorCarryCoordSysTransform(
            ResolvedFeatureReference r, MathTransform coordsysUnflexedGlobal)
        {
            MathTransform subAsmGlobal = r.ComponentTransform;

            Component2 anchorStandalone = GetCoordSysAnchorComponent(r);
            if (anchorStandalone == null)
            {
                logger.Info("In-context coord system '" + r.FeatureName +
                    "' anchor component unresolved; falling back to standalone read.");
                return null;
            }

            MathTransform anchorSubAsmLocal = anchorStandalone.Transform2;
            if (anchorSubAsmLocal == null)
            {
                return null;
            }
            MathTransform anchorUnflexedGlobal = subAsmGlobal == null
                ? anchorSubAsmLocal : (MathTransform)anchorSubAsmLocal.Multiply(subAsmGlobal);

            // Same anchor part, but its in-context instance under the root
            // assembly (whose total transform reflects the flexible solve).
            string targetName = r.OwningComponent.Name2 + "/" + anchorStandalone.Name2;
            Component2 anchorInContext = FindComponentByName2InAssembly(targetName);
            if (anchorInContext == null)
            {
                logger.Info("In-context coord system '" + r.FeatureName +
                    "' anchor '" + targetName + "' not found in assembly; falling back.");
                return null;
            }
            MathTransform anchorFlexedGlobal =
                anchorInContext.GetTotalTransform(true) ?? anchorInContext.Transform2;
            if (anchorFlexedGlobal == null)
            {
                return null;
            }

            MathTransform anchorUnflexedInv = (MathTransform)anchorUnflexedGlobal.Inverse();
            MathTransform coordsysInAnchor =
                (MathTransform)coordsysUnflexedGlobal.Multiply(anchorUnflexedInv);
            return (MathTransform)coordsysInAnchor.Multiply(anchorFlexedGlobal);
        }

        // Rebuilds the coordinate system's BASIS from its own defining reference
        // axes evaluated IN-CONTEXT, returning the three basis columns (X/Y/Z) as
        // global unit vectors. A coord-sys basis is frequently a RELATIVE direction
        // (e.g. a two-point reference axis spanning two moving parts); the
        // single-anchor flex carry pins it to one part and gets such a basis wrong
        // whenever the OTHER part moves. Reference axes, by contrast, evaluate
        // correctly in-context (GetRefAxisParams), so we:
        //   1. find the coord system's direct-parent RefAxis features,
        //   2. match each (by standalone direction) to the X/Y/Z column it defines,
        //   3. read its in-context direction,
        //   4. compute the rotation delta mapping the standalone specified
        //      column(s) to their in-context direction(s), and apply it to the
        //      WHOLE standalone basis (so auto-derived columns are carried
        //      rigidly - the best continuation without replicating SW's private
        //      axis-completion rule).
        // Returns null when no defining axis is evaluable (caller keeps the carried
        // basis). The basis is independent of the origin (the caller supplies the
        // origin separately - from the in-context reference point or the anchor carry).
        private double[][] RefineCoordSysBasisColumns(
            ResolvedFeatureReference r, Feature feat, MathTransform unflexedGlobal)
        {
            double[] s = unflexedGlobal.ArrayData as double[];
            if (s == null)
            {
                return null;
            }
            double[][] stdCols =
            {
                new[] { s[0], s[1], s[2] },
                new[] { s[3], s[4], s[5] },
                new[] { s[6], s[7], s[8] },
            };

            var axisNames = DirectParentFeatureNames(feat, "RefAxis");
            if (axisNames.Count == 0)
            {
                return null;
            }

            var stdMatched = new System.Collections.Generic.List<double[]>();
            var ctxMatched = new System.Collections.Generic.List<double[]>();
            var usedColumns = new System.Collections.Generic.HashSet<int>();
            foreach (string axisName in axisNames)
            {
                double[] rawStd = StandaloneAxisDirGlobal(r, axisName);
                if (rawStd == null)
                {
                    continue;
                }
                int best = -1;
                double bestAbs = 0.0;
                for (int i = 0; i < 3; i++)
                {
                    double d = Math.Abs(Vec.Dot(rawStd, stdCols[i]));
                    if (d > bestAbs) { bestAbs = d; best = i; }
                }
                if (best < 0 || bestAbs < 0.9 || usedColumns.Contains(best))
                {
                    continue;
                }
                ResolvedFeatureReference ar = r;
                ar.FeatureName = axisName;
                double[] rawCtx = TryGetInContextAxisDirection(ar);
                if (rawCtx == null)
                {
                    continue;
                }
                double sign = Vec.Dot(rawStd, stdCols[best]) < 0 ? -1.0 : 1.0;
                usedColumns.Add(best);
                stdMatched.Add(Vec.Normalize(stdCols[best]));
                ctxMatched.Add(Vec.Normalize(Vec.Scale(rawCtx, sign)));
            }

            if (stdMatched.Count == 0)
            {
                return null;
            }

            double[][] rDelta = stdMatched.Count == 1
                ? Vec.RotationAligning(stdMatched[0], ctxMatched[0])
                : Vec.RotationFromTwoCorrespondences(
                    stdMatched[0], ctxMatched[0], stdMatched[1], ctxMatched[1]);
            if (rDelta == null)
            {
                return null;
            }

            return new[]
            {
                Vec.MatVec(rDelta, stdCols[0]),
                Vec.MatVec(rDelta, stdCols[1]),
                Vec.MatVec(rDelta, stdCols[2]),
            };
        }

        // Names of a feature's DIRECT parent features of the given SW type
        // (e.g. "RefAxis"). Used to find a coordinate system's axis-defining
        // reference features without descending into their own dependencies.
        private System.Collections.Generic.List<string> DirectParentFeatureNames(
            Feature feat, string swTypeName)
        {
            var names = new System.Collections.Generic.List<string>();
            if (feat == null)
            {
                return names;
            }
            object parentsObj = null;
            try { parentsObj = feat.GetParents(); } catch { }
            if (parentsObj is object[] parents)
            {
                foreach (object po in parents)
                {
                    if (!(po is Feature pf))
                    {
                        continue;
                    }
                    string tn = null;
                    try { tn = pf.GetTypeName2(); } catch { }
                    if (tn == swTypeName)
                    {
                        names.Add(pf.Name);
                    }
                }
            }
            return names;
        }

        // Standalone (un-flexed) root-global direction of a named reference axis
        // owned by the coord system's sub-assembly. Read-only FeatureManager walk
        // (no SelectionMgr), lifted into root-global by the sub-assembly transform.
        private double[] StandaloneAxisDirGlobal(ResolvedFeatureReference r, string axisName)
        {
            Feature af = FindNamedFeature(r.OwningDoc, "RefAxis", axisName);
            RefAxis ax = af?.GetSpecificFeature2() as RefAxis;
            double[] p = ax?.GetRefAxisParams();
            if (p == null || p.Length < 6)
            {
                return null;
            }
            double[] v = MathOps.PNorm(new[] { p[0] - p[3], p[1] - p[4], p[2] - p[5] }, 2);
            if (Math.Abs(v[0]) < 1e-12 && Math.Abs(v[1]) < 1e-12 && Math.Abs(v[2]) < 1e-12)
            {
                return null;
            }
            return GlobalAxis(v, r.ComponentTransform);
        }

        // Minimal 3-vector / 3x3-rotation helpers for in-context coord-sys basis
        // reconstruction. Rotations are row-major jagged matrices (R[row][col]);
        // MatVec computes R*v. Frame vectors are global-frame unit basis columns.
        private static class Vec
        {
            public static double Dot(double[] a, double[] b)
                => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];

            public static double[] Cross(double[] a, double[] b) => new[]
            {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0],
            };

            public static double[] Scale(double[] a, double s) => new[] { a[0] * s, a[1] * s, a[2] * s };

            public static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };

            public static double Norm(double[] a) => Math.Sqrt(Dot(a, a));

            public static double[] Normalize(double[] a)
            {
                double n = Norm(a);
                return n < 1e-12 ? new[] { a[0], a[1], a[2] } : Scale(a, 1.0 / n);
            }

            public static double[] MatVec(double[][] m, double[] v) => new[]
            {
                m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
                m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
                m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2],
            };

            public static double[][] Identity() => new[]
            {
                new[] { 1.0, 0.0, 0.0 },
                new[] { 0.0, 1.0, 0.0 },
                new[] { 0.0, 0.0, 1.0 },
            };

            private static double[][] MatMul(double[][] a, double[][] b)
            {
                var r = new double[3][];
                for (int i = 0; i < 3; i++)
                {
                    r[i] = new double[3];
                    for (int j = 0; j < 3; j++)
                    {
                        r[i][j] = a[i][0] * b[0][j] + a[i][1] * b[1][j] + a[i][2] * b[2][j];
                    }
                }
                return r;
            }

            private static double[][] Transpose(double[][] a) => new[]
            {
                new[] { a[0][0], a[1][0], a[2][0] },
                new[] { a[0][1], a[1][1], a[2][1] },
                new[] { a[0][2], a[1][2], a[2][2] },
            };

            // Matrix whose COLUMNS are c1, c2, c3 (row-major storage).
            private static double[][] FrameColumns(double[] c1, double[] c2, double[] c3) => new[]
            {
                new[] { c1[0], c2[0], c3[0] },
                new[] { c1[1], c2[1], c3[1] },
                new[] { c1[2], c2[2], c3[2] },
            };

            private static double[] AnyPerp(double[] a)
            {
                double[] t = Math.Abs(a[0]) < 0.9 ? new[] { 1.0, 0.0, 0.0 } : new[] { 0.0, 1.0, 0.0 };
                return Normalize(Cross(a, t));
            }

            // Rodrigues rotation about unit axis k by angle (radians).
            private static double[][] AxisAngle(double[] k, double angle)
            {
                double c = Math.Cos(angle), s = Math.Sin(angle), t = 1.0 - c;
                double x = k[0], y = k[1], z = k[2];
                return new[]
                {
                    new[] { t * x * x + c,     t * x * y - s * z, t * x * z + s * y },
                    new[] { t * x * y + s * z, t * y * y + c,     t * y * z - s * x },
                    new[] { t * x * z - s * y, t * y * z + s * x, t * z * z + c     },
                };
            }

            // Minimal rotation mapping unit vector a onto unit vector b.
            public static double[][] RotationAligning(double[] a, double[] b)
            {
                double[] ua = Normalize(a), ub = Normalize(b);
                double c = Dot(ua, ub);
                double[] v = Cross(ua, ub);
                double sLen = Norm(v);
                if (sLen < 1e-9)
                {
                    return c >= 0 ? Identity() : AxisAngle(AnyPerp(ua), Math.PI);
                }
                return AxisAngle(Scale(v, 1.0 / sLen), Math.Atan2(sLen, c));
            }

            // Rotation mapping the source frame built from (a1, a2) onto the target
            // frame built from (b1, b2): R = F * Eᵀ where E/F have the orthonormal
            // frame vectors as columns. Maps a1->b1 exactly and the a2/b2 plane
            // consistently. Falls back to single-axis alignment if a2/b2 are
            // degenerate relative to a1/b1.
            public static double[][] RotationFromTwoCorrespondences(
                double[] a1, double[] b1, double[] a2, double[] b2)
            {
                double[] e1 = Normalize(a1);
                double[] e2raw = Sub(a2, Scale(e1, Dot(a2, e1)));
                double[] f1 = Normalize(b1);
                double[] f2raw = Sub(b2, Scale(f1, Dot(b2, f1)));
                if (Norm(e2raw) < 1e-6 || Norm(f2raw) < 1e-6)
                {
                    return RotationAligning(a1, b1);
                }
                double[] e2 = Normalize(e2raw);
                double[] e3 = Cross(e1, e2);
                double[] f2 = Normalize(f2raw);
                double[] f3 = Cross(f1, f2);
                return MatMul(FrameColumns(f1, f2, f3), Transpose(FrameColumns(e1, e2, e3)));
            }
        }

        // Returns the (standalone sub-assembly-doc) component the coord system's
        // ORIGIN is built on - the part it rigidly translates with. Used to
        // compute the origin's flex delta for in-context coord-sys reconstruction.
        // The origin is usually defined via an intermediate reference point / axis
        // rather than part geometry directly, so this descends the coord system's
        // dependency tree (FindAnchorComponentRecursive) to reach the moving part.
        // Returns null when the origin is sub-assembly-root geometry (no moving
        // part) or cannot be resolved (caller falls back to the standalone read).
        private Component2 GetCoordSysAnchorComponent(ResolvedFeatureReference r)
        {
            Feature feat = FindNamedFeature(r.OwningDoc, "CoordSys", r.FeatureName);
            if (feat == null)
            {
                return null;
            }
            return FindAnchorComponentRecursive(feat, r.OwningDoc, 0);
        }

        // Walks a feature's reference geometry (and, failing that, its parent
        // features) looking for the first sub-component the feature is built on.
        // A coordinate system is frequently anchored to a part only indirectly -
        // its origin / axes are themselves reference points / axes defined at the
        // sub-assembly level, whose OWN selections point at the moving part. So a
        // direct GetComponent() on the coord-sys entities returns null and we have
        // to descend one (or more) reference levels via GetParents(). Depth-capped
        // to avoid cycles.
        private Component2 FindAnchorComponentRecursive(Feature feat, ModelDoc2 doc, int depth)
        {
            if (feat == null || depth > 5)
            {
                return null;
            }
            // A MateGroup lists every mated component and gives no anchor signal -
            // descending into it would pick an arbitrary (likely wrong) part.
            string tname = null;
            try { tname = feat.GetTypeName2(); } catch { }
            if (tname == "MateGroup")
            {
                return null;
            }
            Component2 own = ComponentFromFeatureSelections(feat, doc);
            if (own != null)
            {
                return own;
            }
            object parentsObj = null;
            try { parentsObj = feat.GetParents(); } catch { }
            if (parentsObj is object[] parents)
            {
                foreach (object po in parents)
                {
                    Component2 c = FindAnchorComponentRecursive(po as Feature, doc, depth + 1);
                    if (c != null)
                    {
                        return c;
                    }
                }
            }
            return null;
        }

        // Reads the reference entities backing a feature (coord system, reference
        // point, reference axis) and returns the first owning sub-component.
        // Returns null for features whose references are all owning-doc-level
        // geometry.
        private Component2 ComponentFromFeatureSelections(Feature feat, ModelDoc2 doc)
        {
            string tn = null;
            try { tn = feat.GetTypeName2(); } catch { }

            // A component instance appears in the FeatureManager / dependency tree
            // as a feature of type "Reference"; GetSpecificFeature2 yields its
            // Component2. This is how the moving part is reached when the coord
            // system's origin / axes are defined via intermediate reference
            // geometry (reference points / axes) rather than part faces directly.
            if (tn == "Reference")
            {
                Component2 comp = null;
                try { comp = feat.GetSpecificFeature2() as Component2; } catch { }
                if (comp == null)
                {
                    // The dependency-tree "Reference" proxy doesn't always expose
                    // the Component2 via GetSpecificFeature2; resolve the real
                    // component instance in the owning doc by name instead.
                    comp = FindComponentInDocByName(doc, feat.Name);
                }
                return comp;
            }

            object def = null;
            try { def = feat.GetDefinition(); } catch { }

            Component2 result = null;
            if (def is ICoordinateSystemFeatureData cs)
            {
                bool acc = false;
                try { acc = cs.AccessSelections(doc, null); } catch { }
                try
                {
                    result = EntityObjToComponent(SafeGet(() => cs.OriginEntity))
                          ?? EntityObjToComponent(SafeGet(() => cs.XAxisEntities))
                          ?? EntityObjToComponent(SafeGet(() => cs.YAxisEntities))
                          ?? EntityObjToComponent(SafeGet(() => cs.ZAxisEntities));
                }
                finally { if (acc) { try { cs.ReleaseSelectionAccess(); } catch { } } }
            }
            else if (def is IRefPointFeatureData rp)
            {
                bool acc = false;
                try { acc = rp.AccessSelections(doc, null); } catch { }
                try { result = EntityObjToComponent(SafeGet(() => rp.Selections)); }
                finally { if (acc) { try { rp.ReleaseSelectionAccess(); } catch { } } }
            }
            else if (def is IRefAxisFeatureData ra)
            {
                bool acc = false;
                try { acc = ra.AccessSelections(doc, null); } catch { }
                try { result = EntityObjToComponent(ra.GetSelections(out object _)); } catch { }
                finally { if (acc) { try { ra.ReleaseSelectionAccess(); } catch { } } }
            }

            return result;
        }

        private object SafeGet(Func<object> getter)
        {
            try { return getter(); } catch { return null; }
        }

        // Extracts the first owning Component2 from a reference returned by a
        // feature definition (a single IEntity or an object[] of entities).
        // Returns null if none resolve to a component (owning-doc-level geometry).
        private Component2 EntityObjToComponent(object entityObj)
        {
            if (entityObj == null)
            {
                return null;
            }
            if (entityObj is object[] arr)
            {
                foreach (object e in arr)
                {
                    Component2 c = (e as IEntity)?.GetComponent() as Component2;
                    if (c != null)
                    {
                        return c;
                    }
                }
                return null;
            }
            return (entityObj as IEntity)?.GetComponent() as Component2;
        }

        // Finds a component in a SPECIFIC assembly document by name. The
        // dependency-tree "Reference" feature carries the component name (e.g.
        // "LINK-1") but does not reliably yield the Component2 via
        // GetSpecificFeature2; this resolves the real instance in the owning doc.
        // Matches Name2 exactly first, then by the leaf path segment (handles the
        // bare "LINK-1" feature name vs a "LINK-1-1" instance Name2). Logs the
        // available names once when nothing matches so the case is diagnosable.
        private Component2 FindComponentInDocByName(ModelDoc2 doc, string name)
        {
            if (doc == null || string.IsNullOrEmpty(name) || !(doc is AssemblyDoc asm))
            {
                return null;
            }
            try
            {
                object[] comps = asm.GetComponents(false);
                if (comps == null)
                {
                    return null;
                }
                foreach (object o in comps)
                {
                    if (o is Component2 c && c.Name2 == name)
                    {
                        return c;
                    }
                }
                string Leaf(string n)
                {
                    int slash = n.LastIndexOf('/');
                    return slash >= 0 ? n.Substring(slash + 1) : n;
                }
                foreach (object o in comps)
                {
                    if (o is Component2 c)
                    {
                        string leaf = Leaf(c.Name2);
                        if (leaf == name || leaf.StartsWith(name + "-") || name.StartsWith(leaf))
                        {
                            return c;
                        }
                    }
                }
                var names = new System.Collections.Generic.List<string>();
                foreach (object o in comps)
                {
                    if (o is Component2 c) { names.Add(c.Name2); }
                }
                logger.Warn("Coord-sys anchor component '" + name +
                    "' not found in sub-assembly doc; in-context basis refinement may " +
                    "fall back to the carried frame. Available components: [" +
                    string.Join(", ", names) + "]");
            }
            catch (Exception ex)
            {
                logger.Warn("FindComponentInDocByName('" + name + "') failed: " + ex.Message);
            }
            return null;
        }

        // Finds the first component (at any sub-assembly depth) whose Name2
        // matches, walking the active assembly's full component list. Mirrors the
        // lookup in ResolveFeatureReference; used by the in-context coord-sys
        // reconstruction to locate the anchor part's in-context instance.
        private Component2 FindComponentByName2InAssembly(string name2)
        {
            if (string.IsNullOrEmpty(name2) || !(ActiveSWModel is AssemblyDoc assy))
            {
                return null;
            }
            try
            {
                object[] components = assy.GetComponents(false);
                if (components == null)
                {
                    return null;
                }
                foreach (Component2 comp in components)
                {
                    if (comp != null && comp.Name2 == name2)
                    {
                        return comp;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Warn("FindComponentByName2InAssembly('" + name2 + "') failed: " + ex.Message);
            }
            return null;
        }

        // Returns the global-frame unit basis vector (X=0, Y=1, Z=2) of the
        // named coordinate system. The rotation columns of the coord-sys
        // transform ARE its basis vectors expressed in assembly-global
        // coordinates, so this is the coord-sys-axis analog of GetRefAxis:
        // feed it through the same AxisFlipped negation + LocalizeJoint path
        // and a basis axis collapses to a clean unit vector in the joint frame.
        private double[] GetCoordinateSystemBasisAxis(string coordinateSystemName, int basisIndex)
        {
            MathTransform transform = GetCoordinateSystemTransform(coordinateSystemName);
            if (transform == null)
            {
                throw new Exception(
                    "Could not resolve coordinate system '" + (coordinateSystemName ?? "") +
                    "' for its basis axis.");
            }

            double[] data = transform.ArrayData;
            int offset = 3 * basisIndex;
            double[] axis = new double[]
            {
                data[offset],
                data[offset + 1],
                data[offset + 2],
            };
            return MathOps.PNorm(axis, 2);
        }

        private void MoveOrigin(Link parent, Link nonLocalizedChild)
        {
            double xMax = Double.MinValue;
            double yMax = Double.MinValue;
            double zMax = Double.MinValue;
            double xMin = Double.MaxValue;
            double yMin = Double.MaxValue;
            double zMin = Double.MaxValue;
            double[] points;

            foreach (Component2 comp in nonLocalizedChild.SWComponents)
            {
                // Returns box as [ XCorner1, YCorner1, ZCorner1, XCorner2, YCorner2, ZCorner2 ]
                points = comp.GetBox(false, false);
                xMax = MathOps.Max(points[0], points[3], xMax);
                yMax = MathOps.Max(points[1], points[4], yMax);
                zMax = MathOps.Max(points[2], points[5], zMax);
                xMin = MathOps.Min(points[0], points[3], xMin);
                yMin = MathOps.Min(points[1], points[4], yMin);
                zMin = MathOps.Min(points[2], points[5], zMin);
            }
            string coordsys = parent.Joint.CoordinateSystemName;
            MathTransform parentTransform = GetCoordinateSystemTransform(coordsys);

            double[] xyzParent = MathOps.GetXYZ(parentTransform);
            double[] xyzJointAxis = nonLocalizedChild.Joint.Axis.GetXYZ();
            double[] xyzOrigin = nonLocalizedChild.Joint.Origin.GetXYZ();
            double[] idealOrigin =
                MathOps.ClosestPointOnLineToPoint(xyzParent, xyzJointAxis, xyzOrigin);

            nonLocalizedChild.Joint.Origin.SetXYZ(
                MathOps.ClosestPointOnLineWithinBox(xMin, xMax, yMin, yMax, zMin, zMax,
                    nonLocalizedChild.Joint.Axis.GetXYZ(), idealOrigin));
        }

        // Calculates the axis from a Reference Axis in the model. Honors the
        // per-joint AxisFlipped intent set in the PropertyManager so that the
        // user's "Reverse Direction" choice survives every export
        // (otherwise the freshly-read SW vector would silently re-pick its
        // own sign on each export).
        private void EstimateAxis(Joint Joint)
        {
            double[] axisXYZ = Joint.UsesCoordinateSystemAxis
                ? GetCoordinateSystemBasisAxis(Joint.CoordinateSystemName, BasisIndexFor(Joint.AxisSource))
                : EstimateAxis(Joint.AxisName);
            if (Joint.AxisFlipped)
            {
                axisXYZ[0] = -axisXYZ[0];
                axisXYZ[1] = -axisXYZ[1];
                axisXYZ[2] = -axisXYZ[2];
            }
            Joint.Axis.SetXYZ(axisXYZ);
        }

        // Maps a coordinate-system basis JointAxisSource to its 0/1/2 column
        // index. Defaults to 0 (X) for non-basis sources; callers must gate
        // on Joint.UsesCoordinateSystemAxis before relying on the result.
        private static int BasisIndexFor(JointAxisSource source)
        {
            switch (source)
            {
                case JointAxisSource.CoordinateSystemY:
                    return 1;
                case JointAxisSource.CoordinateSystemZ:
                    return 2;
                default:
                    return 0;
            }
        }

        // Result of PreviewAxisDirection: enough information for the
        // PropertyManager to render an overlay arrow at the joint origin in
        // the assembly viewport without mutating any Joint state.
        // Both vectors are in assembly-global coordinates.
        public struct AxisPreview
        {
            public bool IsValid;
            public double[] OriginGlobal;
            public double[] AxisGlobal;
        }

        // Resolves the given coord-sys + axis names to a global-frame origin
        // and (possibly flipped) axis direction. Pure: does NOT mutate any
        // Joint or write to the model. Used by the PM live preview hook to
        // (re)draw the overlay arrow whenever the user changes the axis,
        // coord system, or flip toggle. Returns IsValid=false when the
        // selections are missing, are placeholder ("Automatically Generate" /
        // "None"), or cannot be resolved.
        public AxisPreview PreviewAxisDirection(string coordsysName, string axisName, bool flipped)
        {
            return PreviewAxisDirection(coordsysName, axisName, flipped, JointAxisSource.ReferenceAxis);
        }

        public AxisPreview PreviewAxisDirection(
            string coordsysName, string axisName, bool flipped, JointAxisSource axisSource)
        {
            AxisPreview empty = new AxisPreview { IsValid = false };

            bool usesBasisAxis =
                axisSource == JointAxisSource.CoordinateSystemX ||
                axisSource == JointAxisSource.CoordinateSystemY ||
                axisSource == JointAxisSource.CoordinateSystemZ;

            logger.Info("PreviewAxisDirection: enter coordsys='" + (coordsysName ?? "") +
                        "' axis='" + (axisName ?? "") + "' flipped=" + flipped +
                        " source=" + axisSource);

            // A coord-sys basis axis needs only a resolvable coordinate system;
            // the reference-axis name is irrelevant (and normally empty) in
            // that mode. Auto-derive has no previewable geometry until export.
            bool placeholderCoordsys =
                string.IsNullOrWhiteSpace(coordsysName) ||
                coordsysName == "Automatically Generate";
            bool placeholderAxis =
                string.IsNullOrWhiteSpace(axisName) ||
                axisName == "Automatically Generate" ||
                axisName == "None";

            if (placeholderCoordsys || (!usesBasisAxis && placeholderAxis))
            {
                // Empty / placeholder picks: nothing to preview. With
                // the SelectionBox-only UI an empty AxisName combined
                // with auto-derive is the "auto" state (no overlay until
                // the kinematic chain has been resolved at export time).
                logger.Info("PreviewAxisDirection: placeholder/empty inputs -> IsValid=false");
                return empty;
            }

            // Memoize the resolved UNFLIPPED preview per (coordsys, axis, source).
            // The flip button and re-selecting the same feature otherwise re-run
            // the multi-second in-context reconstruction; the flip is a pure sign
            // change. Cache the unflipped result so only the FIRST resolve of each
            // distinct selection pays the cost (cleared on PMP close).
            string previewKey =
                (coordsysName ?? "") + "||" + (axisName ?? "") + "||" + (int)axisSource;
            AxisPreview cached;
            if (!axisPreviewCache.TryGetValue(previewKey, out cached))
            {
                cached = ResolveAxisPreviewUnflipped(coordsysName, axisName, axisSource, usesBasisAxis);
                axisPreviewCache[previewKey] = cached;
            }
            else
            {
                logger.Info("PreviewAxisDirection: cache hit for '" + previewKey + "'");
            }

            if (!cached.IsValid)
            {
                return empty;
            }

            // Return a fresh copy so the caller-applied flip never mutates the
            // cached (unflipped) array.
            double[] axisOut = new double[]
            {
                cached.AxisGlobal[0], cached.AxisGlobal[1], cached.AxisGlobal[2]
            };
            if (flipped)
            {
                axisOut[0] = -axisOut[0];
                axisOut[1] = -axisOut[1];
                axisOut[2] = -axisOut[2];
            }
            return new AxisPreview
            {
                IsValid = true,
                OriginGlobal = new double[]
                {
                    cached.OriginGlobal[0], cached.OriginGlobal[1], cached.OriginGlobal[2]
                },
                AxisGlobal = axisOut,
            };
        }

        // Resolves the UNFLIPPED preview origin + axis for a selection. Factored
        // out of PreviewAxisDirection so the result can be memoized. Computes the
        // coordinate-system transform ONCE and derives both the origin and (for a
        // coord-sys basis axis) the basis column from it, instead of resolving the
        // transform twice (origin via GetCoordinateSystemTransform + axis via
        // GetCoordinateSystemBasisAxis, which re-resolved internally).
        private AxisPreview ResolveAxisPreviewUnflipped(
            string coordsysName, string axisName, JointAxisSource axisSource, bool usesBasisAxis)
        {
            AxisPreview empty = new AxisPreview { IsValid = false };

            // PreviewAxisDirection is contractually side-effect-free and runs
            // while the PropertyManager page is open. Suppress the
            // ShowConfiguration2 document mutation in WithComponentConfiguration
            // for the duration; a preview overlay in the part's current
            // configuration is acceptable and avoids closing the PM page.
            bool priorSuppress = suppressConfigSwitchForFeatureLookup;
            suppressConfigSwitchForFeatureLookup = true;
            try
            {
                MathTransform coordsysTransform = GetCoordinateSystemTransform(coordsysName);
                if (coordsysTransform == null)
                {
                    return empty;
                }
                double[] origin = MathOps.GetXYZ(coordsysTransform);

                double[] axis;
                try
                {
                    if (usesBasisAxis)
                    {
                        // Pull the basis column straight off the already-resolved
                        // transform - no second GetCoordinateSystemTransform.
                        int offset = 3 * BasisIndexFor(axisSource);
                        double[] data = coordsysTransform.ArrayData;
                        axis = MathOps.PNorm(new double[]
                        {
                            data[offset], data[offset + 1], data[offset + 2]
                        }, 2);
                    }
                    else
                    {
                        axis = EstimateAxis(axisName);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("PreviewAxisDirection: axis resolution failed: " + ex.Message);
                    return empty;
                }

                if (axis == null ||
                    (Math.Abs(axis[0]) < 1e-12 && Math.Abs(axis[1]) < 1e-12 && Math.Abs(axis[2]) < 1e-12))
                {
                    return empty;
                }

                return new AxisPreview
                {
                    IsValid = true,
                    OriginGlobal = origin,
                    AxisGlobal = axis,
                };
            }
            finally
            {
                suppressConfigSwitchForFeatureLookup = priorSuppress;
            }
        }

        // Drops the live-preview memo. Called on export PMP close so a later
        // session re-resolves against possibly-changed geometry.
        public void ClearAxisPreviewCache()
        {
            axisPreviewCache.Clear();
        }

        // Resolves a SW reference axis Feature.Name to its global-frame
        // unit direction vector. Read-only with respect to SelectionMgr:
        // GetRefAxis walks FeatureManager.GetFeatures directly to find
        // the named RefAxis (see FindNamedFeature) instead of going
        // through Extension.SelectByID2 + GetSelectedObject6, so the
        // live-preview path never perturbs the assembly's selection
        // state. The historical clobber via SelectByID2(Append=false,
        // mark=0) was the root cause of the "SelectionBox renders
        // empty after coord-sys / axis pick" symptom - every PM
        // preview emptied every marked SelectionBox until the user
        // re-clicked the tab. With FindNamedFeature, the preview
        // path is side-effect-free.
        public double[] EstimateAxis(string axisName)
        {
            return GetRefAxis(axisName);
        }

        private double[] GetRefAxis(string axisStr)
        {
            ResolvedFeatureReference r = ResolveFeatureReference(axisStr);

            // PRIMARY PATH for sub-component reference axes: convert the
            // model-context RefAxis pointer into the ASSEMBLY context via
            // IComponent2.GetCorresponding, then read GetRefAxisParams. That
            // returns the axis IN-CONTEXT (reflecting flexible sub-assembly
            // repositioning) but in the owning sub-assembly's LOCAL frame;
            // TryGetInContextAxisDirection lifts it to root-global. Read-only
            // (no SelectionMgr / doc mutation), so safe on the live-preview path.
            // Falls through to the standalone sub-assembly-document read when
            // unavailable.
            if (r.OwningComponent != null)
            {
                double[] inContextDir = TryGetInContextAxisDirection(r);
                if (inContextDir != null)
                {
                    return inContextDir;
                }
            }

            // The SelectByID2 -> SelectionManager -> GetRefAxisParams chain
            // implicitly reads from the part doc's currently-active
            // configuration, so it has to live inside the config-switched
            // block. Returning null signals "no axis found"; the array
            // returned otherwise is already PNorm-normalised but still in
            // the part doc's local frame -- we apply the component transform
            // outside the block since it does not depend on active config.
            string cacheKey = FeatureLookupCacheKey("axis", r);
            double[] axisVector;
            if (IsFeatureLookupCacheEnabled() && referenceAxisCache.TryGetValue(cacheKey, out axisVector))
            {
                logger.Info("Reference axis lookup cache hit for " + r.FeatureName +
                    " in config '" + (r.ConfigurationName ?? "") + "'");
                axisVector = (double[])axisVector.Clone();
            }
            else
            {
                axisVector = WithComponentConfiguration(r.OwningDoc, r.ConfigurationName, () =>
                {
                    // Walk r.OwningDoc.FeatureManager directly to resolve
                    // the named RefAxis without touching SelectionMgr.
                    // FeatureManager.GetFeatures is read-only, so live axis
                    // preview cannot clear or contaminate PropertyManager
                    // SelectionBox marks while the user is picking features.
                    Feature feat = FindNamedFeature(r.OwningDoc, "RefAxis", r.FeatureName);
                    if (feat == null)
                    {
                        return null;
                    }
                    RefAxis axis = feat.GetSpecificFeature2() as RefAxis;
                    if (axis == null)
                    {
                        return null;
                    }

                    // GetRefAxisParams returns {startX, startY, startZ, endX, endY, endZ}
                    double[] axisParams = axis.GetRefAxisParams();
                    double[] v = new double[3];
                    v[0] = axisParams[0] - axisParams[3];
                    v[1] = axisParams[1] - axisParams[4];
                    v[2] = axisParams[2] - axisParams[5];

                    return MathOps.PNorm(v, 2);
                });
                if (IsFeatureLookupCacheEnabled() && axisVector != null)
                {
                    referenceAxisCache[cacheKey] = (double[])axisVector.Clone();
                }
            }

            if (axisVector == null)
            {
                return new double[3];
            }

            return GlobalAxis(axisVector, r.ComponentTransform);
        }

        // Resolves the named feature in the owning component's ASSEMBLY context
        // (reflecting flexible sub-assembly repositioning) rather than the
        // component's standalone underlying-model document. Two mechanisms, both
        // documented as assembly-context-aware:
        //   1. IComponent2.FeatureByName - returns the feature in the context of
        //      the assembly (codestack assembly-context overview).
        //   2. IComponent2.GetCorresponding(Feature) - converts a model-context
        //      Feature pointer into the assembly context. NOTE: must be passed
        //      the Feature, NOT the specific feature (RefAxis / IRefPoint) -
        //      passing the specific feature returns null (the bug in the first
        //      cut of this fix).
        // Returns null for top-level features (no owning component) or when
        // neither mechanism resolves the feature.
        private Feature ResolveInContextFeature(ResolvedFeatureReference r, string swTypeName)
        {
            if (r.OwningComponent == null)
            {
                return null;
            }
            try
            {
                Feature byName = r.OwningComponent.FeatureByName(r.FeatureName);
                if (byName != null)
                {
                    return byName;
                }
            }
            catch (Exception ex)
            {
                logger.Warn("In-context FeatureByName('" + r.FeatureName + "') failed: " + ex.Message);
            }
            try
            {
                Feature modelFeat = FindNamedFeature(r.OwningDoc, swTypeName, r.FeatureName);
                if (modelFeat != null)
                {
                    Feature corr = r.OwningComponent.GetCorresponding(modelFeat) as Feature;
                    if (corr != null)
                    {
                        return corr;
                    }
                    logger.Info("In-context GetCorresponding(Feature) for '" + r.FeatureName +
                        "' returned null.");
                }
            }
            catch (Exception ex)
            {
                logger.Warn("In-context GetCorresponding('" + r.FeatureName + "') failed: " + ex.Message);
            }
            return null;
        }

        // In-context reference-axis direction for a feature that lives inside a
        // (possibly flexible) sub-assembly. Resolves the RefAxis in the assembly
        // context (see ResolveInContextFeature) so GetRefAxisParams reflects
        // flexible repositioning. The evaluated direction comes back in the OWNING
        // sub-assembly's LOCAL document frame (NOT root-global), so it IS lifted by
        // r.ComponentTransform's rotation here. Returns a PNorm-normalised global
        // direction, or null if unavailable (caller falls back to standalone).
        private double[] TryGetInContextAxisDirection(ResolvedFeatureReference r)
        {
            try
            {
                Feature feat = ResolveInContextFeature(r, "RefAxis");
                RefAxis axis = feat?.GetSpecificFeature2() as RefAxis;
                if (axis == null)
                {
                    logger.Info("In-context axis '" + r.FeatureName +
                        "' unresolved; falling back to standalone read.");
                    return null;
                }

                double[] p = axis.GetRefAxisParams();
                if (p == null || p.Length < 6)
                {
                    return null;
                }

                double[] v = new double[]
                {
                    p[0] - p[3], p[1] - p[4], p[2] - p[5]
                };
                if (Math.Abs(v[0]) < 1e-12 && Math.Abs(v[1]) < 1e-12 && Math.Abs(v[2]) < 1e-12)
                {
                    return null;
                }

                // GetRefAxisParams on an assembly-context feature obtained from a
                // NESTED sub-assembly component returns the direction in the
                // OWNING sub-assembly's LOCAL document frame (it reflects flexible
                // repositioning, but is NOT root-global). Lift it to root-global by
                // the owning component's transform rotation -- the SAME convention
                // StandaloneAxisDirGlobal uses. (A one-level sub-assembly placed at
                // identity hides this because local == root-global there.)
                double[] localDir = MathOps.PNorm(v, 2);
                return MathOps.PNorm(GlobalAxis(localDir, r.ComponentTransform), 2);
            }
            catch (Exception ex)
            {
                logger.Warn("In-context axis resolution for '" + r.FeatureName +
                    "' failed (" + ex.Message + "); falling back to standalone read.");
                return null;
            }
        }

        //This is called whenever the pull down menu is changed and the axis needs to be
        // recalculated in reference to the coordinate system
        public double[] LocalizeAxis(double[] Axis, string coordsys)
        {
            MathTransform coordsysTransform = GetCoordinateSystemTransform(coordsys);
            return LocalizeAxis(Axis, coordsysTransform);
        }

        // This is called by the above method and the getRefAxis method
        private static double[] LocalizeAxis(double[] Axis, MathTransform coordsysTransform)
        {
            if (coordsysTransform != null)
            {
                Vector<double> vec = new DenseVector(new double[] { Axis[0], Axis[1], Axis[2], 0 });
                Matrix<double> transform = MathOps.GetTransformation(coordsysTransform);
                vec = transform.Inverse() * vec;
                Axis[0] = vec[0]; Axis[1] = vec[1]; Axis[2] = vec[2];
            }
            return MathOps.Threshold(Axis, 0.00001);
        }

        private static double[] GlobalAxis(double[] axis, Matrix<double> transform)
        {
            double[] transformedAxis = new double[axis.Length];
            if (transform != null)
            {
                Vector<double> transformedVector = new DenseVector(new double[] { axis[0], axis[1], axis[2], 0 });
                transformedVector = transform * transformedVector;
                transformedAxis[0] = transformedVector[0];
                transformedAxis[1] = transformedVector[1];
                transformedAxis[2] = transformedVector[2];
            }
            return MathOps.Threshold(transformedAxis, 0.00001);
        }

        private static double[] GlobalAxis(double[] axis, MathTransform coordsysTransform)
        {
            if (coordsysTransform != null)
            {
                Matrix<double> transform = MathOps.GetTransformation(coordsysTransform);
                return GlobalAxis(axis, transform);
            }
            return axis;
        }

        // Returns the assembly-global position (meters) of the named SolidWorks
        // reference point, or null if it cannot be resolved. Mirrors GetRefAxis:
        // walks FeatureManager directly via FindNamedFeature so the live PM
        // preview / export path never perturbs SelectionMgr, and lifts a
        // part-local point into assembly-global coordinates via the
        // sub-component transform. Reference points carry only a location (no
        // basis vectors), so callers supply the site's rotation themselves
        // (identity in the parent body frame, by design).
        private double[] GetReferencePointGlobalPosition(string pointName)
        {
            if (string.IsNullOrEmpty(pointName))
            {
                return null;
            }

            ResolvedFeatureReference r = ResolveFeatureReference(pointName);

            // PRIMARY PATH for sub-component reference points (e.g. a site point
            // in a flexible sub-assembly): convert the model-context RefPoint
            // pointer into the ASSEMBLY context via IComponent2.GetCorresponding,
            // then read GetRefPoint. That returns the point IN-CONTEXT (reflecting
            // flexible repositioning) but in the owning sub-assembly's LOCAL frame;
            // TryGetInContextPointGlobal lifts it to root-global. Read-only, safe on
            // any path. Falls through to the standalone sub-assembly-document read
            // when unavailable.
            if (r.OwningComponent != null)
            {
                double[] inContextPoint = TryGetInContextPointGlobal(r);
                if (inContextPoint != null)
                {
                    return inContextPoint;
                }
            }

            // GetSpecificFeature2/GetRefPoint read the part doc's currently-active
            // configuration, so resolve inside the config-switched block; the
            // returned point is in the owning doc's model frame and the component
            // transform (applied outside) does not depend on the active config.
            double[] localPoint = WithComponentConfiguration(r.OwningDoc, r.ConfigurationName, () =>
            {
                Feature feat = FindNamedFeature(r.OwningDoc, "RefPoint", r.FeatureName);
                if (feat == null)
                {
                    return null;
                }
                IRefPoint refPoint = feat.GetSpecificFeature2() as IRefPoint;
                if (refPoint == null)
                {
                    return null;
                }
                MathPoint mathPoint = refPoint.GetRefPoint();
                double[] data = mathPoint?.ArrayData as double[];
                if (data == null || data.Length < 3)
                {
                    return null;
                }
                return new double[] { data[0], data[1], data[2] };
            });

            if (localPoint == null)
            {
                return null;
            }

            return GlobalPoint(localPoint, r.ComponentTransform);
        }

        // In-context reference-point position for a feature that lives inside a
        // (possibly flexible) sub-assembly. Resolves the RefPoint in the assembly
        // context (see ResolveInContextFeature) so GetRefPoint reflects flexible
        // repositioning. The evaluated position comes back in the OWNING
        // sub-assembly's LOCAL document frame (NOT root-global), so it IS lifted by
        // r.ComponentTransform (GlobalPoint) here. Returns the global position, or
        // null if unavailable (caller falls back to the standalone read).
        private double[] TryGetInContextPointGlobal(ResolvedFeatureReference r)
        {
            try
            {
                Feature feat = ResolveInContextFeature(r, "RefPoint");
                IRefPoint refPoint = feat?.GetSpecificFeature2() as IRefPoint;
                if (refPoint == null)
                {
                    logger.Info("In-context point '" + r.FeatureName +
                        "' unresolved; falling back to standalone read.");
                    return null;
                }

                double[] data = refPoint.GetRefPoint()?.ArrayData as double[];
                if (data == null || data.Length < 3)
                {
                    return null;
                }

                // GetRefPoint on an assembly-context feature obtained from a NESTED
                // sub-assembly component returns the position in the OWNING
                // sub-assembly's LOCAL document frame (it reflects flexible
                // repositioning, but is NOT root-global). Lift it to root-global by
                // the owning component's full transform -- the SAME GlobalPoint
                // compose the standalone fallback path uses. (A one-level
                // sub-assembly placed at identity hides this because local ==
                // root-global there.)
                double[] local = new double[] { data[0], data[1], data[2] };
                return GlobalPoint(local, r.ComponentTransform);
            }
            catch (Exception ex)
            {
                logger.Warn("In-context point resolution for '" + r.FeatureName +
                    "' failed (" + ex.Message + "); falling back to standalone read.");
                return null;
            }
        }

        // Lifts a part-local point into assembly-global coordinates. Identical
        // to GlobalAxis except the homogeneous w-coordinate is 1 (a position is
        // affected by the transform's translation; a direction is not).
        private static double[] GlobalPoint(double[] point, MathTransform componentTransform)
        {
            double[] result = new double[] { point[0], point[1], point[2] };
            if (componentTransform != null)
            {
                Matrix<double> transform = MathOps.GetTransformation(componentTransform);
                Vector<double> v = new DenseVector(new double[] { point[0], point[1], point[2], 1 });
                v = transform * v;
                result[0] = v[0];
                result[1] = v[1];
                result[2] = v[2];
            }
            return MathOps.Threshold(result, 0.00001);
        }

        // Finds a single named Feature of the requested type-name
        // (e.g. "RefAxis", "CoordSys") by walking the document's
        // FeatureManager. Read-only - does NOT touch SelectionMgr,
        // unlike Extension.SelectByID2 which has a global side effect
        // even on success. Used by GetRefAxis (live PM preview path
        // and export path); could be reused for any future feature
        // resolution that needs to avoid SelectionMgr clobber.
        private static Feature FindNamedFeature(ModelDoc2 modelDoc, string typeName, string featureName)
        {
            if (modelDoc == null || string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(featureName))
            {
                return null;
            }
            object[] featureObjects = modelDoc.FeatureManager.GetFeatures(false);
            if (featureObjects == null)
            {
                return null;
            }
            foreach (object obj in featureObjects)
            {
                Feature candidate = obj as Feature;
                if (candidate == null) continue;
                if (candidate.GetTypeName2() != typeName) continue;
                if (candidate.Name == featureName)
                {
                    return candidate;
                }
            }
            return null;
        }

        // Read-only existence check for a named reference feature (coord system
        // / axis) used by the configuration-load validation to surface a saved
        // reference whose feature no longer exists in the assembly (renamed or
        // deleted). Uses the same side-effect-free FeatureManager walk as
        // GetRefAxis - it does NOT perturb SelectionMgr.
        //
        // Component-scoped names (carrying an "@component" or " <component>"
        // suffix) cannot be validated by a top-level FeatureManager walk and
        // would require an expensive per-component configuration switch, so they
        // are conservatively reported as present to avoid false "missing"
        // warnings. Only plain top-level names are actually verified.
        public bool ReferenceFeatureExists(string typeName, string featureName)
        {
            if (string.IsNullOrEmpty(featureName))
            {
                return false;
            }
            if (featureName.IndexOf('@') >= 0 || featureName.IndexOf('<') >= 0)
            {
                return true;
            }
            return FindNamedFeature(ActiveSWModel, typeName, featureName) != null;
        }

        // Creates a list of all the features of this type.
        private Dictionary<string, List<Feature>> GetFeaturesOfType(string featureName, bool topLevelOnly)
        {
            Dictionary<string, List<Feature>> features = new Dictionary<string, List<Feature>>();
            GetFeaturesOfType(ActiveSWModel, featureName, topLevelOnly, "", features);
            return features;
        }

        private void GetFeaturesOfType(ModelDoc2 modelDoc, string featureName,
            bool topLevelOnly, string keyName, Dictionary<string, List<Feature>> features)
        {
            string fileName = (string.IsNullOrWhiteSpace(keyName)) ? modelDoc.GetTitle() : keyName;
            logger.Info("Retrieving features of type [" + featureName + "] from " + fileName);

            features[keyName] = new List<Feature>();

            object[] featureObjects = modelDoc.FeatureManager.GetFeatures(false);
            if (featureObjects == null)
            {
                logger.Info("No features found in " + modelDoc.GetTitle());
                return;
            }

            logger.Info("Found " + featureObjects.Length + " in " + fileName);
            foreach (Feature feat in featureObjects)
            {
                if (feat.GetTypeName2() == featureName)
                {
                    features[keyName].Add(feat);
                }
            }

            logger.Info("Found " + features[keyName].Count + " features of type [" + featureName + "] in " + fileName);
            if (!topLevelOnly && modelDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                logger.Info("Proceeding through assembly components");
                AssemblyDoc assyDoc = (AssemblyDoc)modelDoc;

                // Get top level components in an assembly. If the user wants to use a reference
                // coordinate system or axis not located in the top level assembly, then it will
                // need to be in a top level component. This will probably be ok because most
                // users keep their reference geometry in the top level assembly as it is.
                object[] components = assyDoc.GetComponents(true);

                // If there are no components in an assembly, this object will be null.
                if (components != null)
                {
                    logger.Info(components.Length + " components to check");
                    foreach (Component2 comp in components)
                    {
                        ModelDoc2 doc = comp.GetModelDoc2();
                        if (doc != null)
                        {
                            //We already have all the components in an assembly, we don't want
                            // to recur as we go through them. (topLevelOnly = true)
                            GetFeaturesOfType(doc, featureName, true, comp.Name2, features);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, string> GetComponentRefGeoNames(string StringToParse)
        {
            string RefGeoName = StringToParse;
            string ComponentName = "";
            if (StringToParse.Contains("<") && StringToParse.Contains(">"))
            {
                int indexFirst = StringToParse.IndexOf('<');
                int indexLast = StringToParse.IndexOf('>', indexFirst);
                if (indexLast > indexFirst)
                {
                    ComponentName = StringToParse.Substring(indexFirst + 1, indexLast - indexFirst - 1);
                    string RefGeoNameUnTrimmed = StringToParse.Substring(0, indexFirst);
                    RefGeoName = RefGeoNameUnTrimmed.Trim();
                }
            }

            Dictionary<string, string> dict = new Dictionary<string, string>
            {
                ["geo"] = RefGeoName,
                ["component"] = ComponentName
            };
            return dict;
        }

        private List<string> FindRefGeoNames(string FeatureName)
        {
            Dictionary<string, List<Feature>> features = GetFeaturesOfType(FeatureName, false);
            List<string> featureNames = new List<string>();
            foreach (string key in features.Keys)
            {
                foreach (Feature feat in features[key])
                {
                    if (String.IsNullOrWhiteSpace(key))
                    {
                        featureNames.Add(feat.Name);
                    }
                    else
                    {
                        featureNames.Add(feat.Name + " <" + key + ">");
                    }
                }
            }
            return featureNames;
        }

        public void UpdateReferenceGeometries()
        {
            List<string> coordinateSystemNames = FindRefGeoNames("CoordSys");
            List<string> axesNames = FindRefGeoNames("RefAxis");

            ReferenceCoordinateSystemNames.Clear();
            ReferenceCoordinateSystemNames.AddRange(coordinateSystemNames);

            ReferenceAxesNames.Clear();
            ReferenceAxesNames.AddRange(axesNames);
        }

        public List<string> GetRefCoordinateSystems()
        {
            return new List<string>(ReferenceCoordinateSystemNames);
        }

        public List<string> GetRefAxes()
        {
            return new List<string>(ReferenceAxesNames);
        }

        // ----- Joint axis direction overlay (PropertyManager preview) -----
        //
        // Renders a SolidWorks-native DragArrowManipulator on the picked
        // joint axis so the user can see which way "positive" points. We
        // use an IDragArrowManipulator (the same gizmo SW's coord-system
        // and mate PMs use for their flip arrows) instead of raw
        // IBody2.Display3 temp bodies because manipulators render ON
        // TOP of geometry by design - they ignore the depth buffer, so an
        // axis hidden inside a tube or behind a link is still visible.
        // Display3 bodies, by contrast, are subject to normal depth test
        // and disappear behind opaque geometry. The manipulator API gives
        // the overlay the required "visible through other bodies" behavior
        // natively.
        //
        // The handler argument to CreateManipulator is REQUIRED. Passing
        // null causes SW to silently refuse to create the manipulator
        // (we did not test what passing null actually does - the canonical
        // SW C# example unambiguously constructs a handler, so we mirror
        // that). For our display-only use we provide a no-op handler
        // (AxisOverlayManipulatorHandler) that returns true / does
        // nothing for every callback. If we later want click-to-flip
        // behavior on the arrow itself (mirroring SW's coord-system PM),
        // wire OnDirectionFlipped to a callback that updates
        // currentAxisFlipped + Joint.AxisFlipped. AllowFlip is currently
        // false because the bitmap button is the documented flip control;
        // exposing two flip mechanisms doubles the surface area.

        // Fraction of the assembly's bounding-box diagonal used for the
        // axis overlay arrow length. 15% is large enough to see clearly
        // against any link's geometry but small enough not to dominate
        // the viewport. Bracketed by AxisOverlayLengthMin / Max so
        // pathological assemblies (a single tiny screw, a 100m skyscraper
        // import) still produce a sensible arrow.
        private const double AxisOverlayLengthFraction = 0.15;
        private const double AxisOverlayLengthMin = 0.02;
        private const double AxisOverlayLengthMax = 5.0;
        private const double AxisOverlayLengthFallback = 0.05;

        // Cached no-op handler for the DragArrowManipulator - lazily
        // built on first DrawAxisOverlay and reused thereafter. The
        // handler has no per-call state so a single instance shared
        // across manipulators is safe; allocating a new one per draw
        // would create unnecessary COM-callable wrappers.
        private AxisOverlayManipulatorHandler axisManipulatorHandler;

        // (Re)draws the joint axis direction overlay arrow at the given
        // world-space origin pointing in the given world-space direction.
        // Both inputs are expressed in assembly-global coordinates. Safe
        // to call repeatedly; the previous manipulator is removed before
        // a new one is created so only the most recent arrow is visible.
        // All failures are logged and swallowed - a viewport problem
        // must not break the PropertyManager.
        public void DrawAxisOverlay(double[] originGlobal, double[] axisGlobal)
        {
            int seq = ++axisOverlayLogSeq;
            logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: enter");

            if (originGlobal == null || originGlobal.Length < 3 ||
                axisGlobal == null || axisGlobal.Length < 3)
            {
                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: bad inputs; clearing");
                ClearAxisOverlay();
                return;
            }

            // Defensive normalize so the arrow length is independent of
            // the caller's vector magnitude.
            double mag = Math.Sqrt(
                axisGlobal[0] * axisGlobal[0] +
                axisGlobal[1] * axisGlobal[1] +
                axisGlobal[2] * axisGlobal[2]);
            if (mag < 1e-12)
            {
                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: zero-magnitude axis; clearing");
                ClearAxisOverlay();
                return;
            }
            double ax = axisGlobal[0] / mag;
            double ay = axisGlobal[1] / mag;
            double az = axisGlobal[2] / mag;

            logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: pre-existing overlay clear");
            ClearAxisOverlay();

            try
            {
                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: querying ModelViewManager");
                ModelViewManager mvm = ActiveSWModel?.ModelViewManager;
                if (mvm == null)
                {
                    logger.Warn("[AxisOverlay #" + seq + "] DrawAxisOverlay: ModelViewManager null; skipping overlay");
                    return;
                }

                MathUtility mathUtil = iSwApp.GetMathUtility() as MathUtility;
                if (mathUtil == null)
                {
                    logger.Warn("[AxisOverlay #" + seq + "] DrawAxisOverlay: GetMathUtility returned null; skipping overlay");
                    return;
                }

                if (axisManipulatorHandler == null)
                {
                    // Use the no-op parameterless ctor: the bitmap-button
                    // "Reverse Direction" control is the sole flip path.
                    // Wiring OnDirectionFlipped together with AllowFlip=true
                    // can deadlock inside SW's manipulator update while
                    // DrawAxisOverlay is refreshing the overlay.
                    axisManipulatorHandler = new AxisOverlayManipulatorHandler();
                }

                // CreateManipulator REQUIRES a non-null handler; passing
                // null is an undocumented case the canonical SW C# example
                // pointedly avoids. Our handler is a no-op stub that
                // returns true / does nothing for every callback.
                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: CreateManipulator(swDragArrowManipulator)");
                Manipulator mgr = mvm.CreateManipulator(
                    (int)swManipulatorType_e.swDragArrowManipulator,
                    axisManipulatorHandler);
                if (mgr == null)
                {
                    logger.Warn("[AxisOverlay #" + seq + "] DrawAxisOverlay: CreateManipulator returned null; skipping overlay");
                    return;
                }

                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: GetSpecificManipulator");
                DragArrowManipulator drag = mgr.GetSpecificManipulator() as DragArrowManipulator;
                if (drag == null)
                {
                    logger.Warn("[AxisOverlay #" + seq + "] DrawAxisOverlay: GetSpecificManipulator did not return a DragArrowManipulator; removing");
                    try { mgr.Remove(); } catch { /* swallowed */ }
                    return;
                }

                double overlayLength = ComputeAxisOverlayLength();

                // Order matters: per the canonical SW example, set all
                // properties first, THEN Show, THEN Update. Update
                // commits the property changes to the rendered gizmo.
                //
                // AllowFlip MUST stay false: with AllowFlip = true SW's
                // manipulator update invokes OnDirectionFlipped on its
                // own render path, our callback re-enters
                // RefreshAxisDirectionPreview which calls Manipulator.Remove
                // / CreateManipulator on the still-updating manipulator,
                // and SW deadlocks on its own internal lock. Symptom: the
                // bitmap "Reverse Direction" button hangs SW with the
                // "not responding" warning and cycles indefinitely. The
                // bitmap button is the documented sole flip control.
                drag.AllowFlip = false;
                drag.ShowRuler = false;
                drag.ShowOppositeDirection = false;
                drag.FixedLength = true;
                drag.Length = overlayLength;
                drag.Direction = (MathVector)mathUtil.CreateVector(new double[] { ax, ay, az });
                drag.Origin = (MathPoint)mathUtil.CreatePoint(new double[] { originGlobal[0], originGlobal[1], originGlobal[2] });

                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: Manipulator.Show ENTER");
                mgr.Show(ActiveSWModel);
                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: Manipulator.Show RETURNED");

                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: DragArrowManipulator.Update ENTER");
                drag.Update();
                logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: DragArrowManipulator.Update RETURNED");

                axisManipulator = mgr;

                // No success-path logger.Info here on purpose. DrawAxisOverlay
                // runs on every coord-sys / axis pick, every flip, every node
                // switch, and every deferred-refresh dequeue - i.e. it is on
                // a UI hot path. The Logger ConversionPattern includes
                // %filename:%line, which forces log4net to call
                // System.Diagnostics.StackTrace.CaptureStackTrace per Info
                // call. Under a debugger with PDBs loaded that single walk
                // takes hundreds of ms; multiplied by the (now-fixed)
                // Manipulator.Show -> OnSelectionboxListChanged ->
                // DeferRefreshAxisPreview loop it produced an apparent
                // SW hang whose call stack always landed in
                // FileNamePatternConverter.Convert. We added a
                // re-entrancy guard upstream
                // (axisPreviewRefreshPending) but kept the success log
                // off as a belt-and-suspenders mitigation: even if a
                // future code path re-queues refreshes faster than
                // expected, the hot loop will not be amplified by the
                // slow log layout. If the success log is ever needed
                // for debugging, switch the Logger pattern to a
                // location-free format ("%date %-5level - %message")
                // first, or wrap this call in a Debug-only conditional
                // (logger.IsDebugEnabled is cheap).
            }
            catch (Exception ex)
            {
                // Failure path keeps logger.Warn deliberately - a single
                // walk per failure is acceptable, and we DO want this in
                // the log when debugging "the arrow does not appear".
                logger.Warn("[AxisOverlay #" + seq + "] DrawAxisOverlay failed: " + ex.Message);
            }

            logger.Info("[AxisOverlay #" + seq + "] DrawAxisOverlay: exit");
        }

        // Returns a sensible arrow length in meters for the active model:
        // a fraction of the assembly's bounding-box diagonal, clamped to
        // [Min, Max] so pathological assemblies still produce a usable
        // arrow. Falls back to a fixed 5cm size if the bounding-box query
        // fails or the doc isn't an assembly.
        private double ComputeAxisOverlayLength()
        {
            try
            {
                AssemblyDoc asmDoc = ActiveSWModel as AssemblyDoc;
                if (asmDoc == null)
                {
                    return AxisOverlayLengthFallback;
                }
                object boxObj = asmDoc.GetBox(0);
                if (!(boxObj is double[] box) || box.Length < 6)
                {
                    return AxisOverlayLengthFallback;
                }
                double dx = box[3] - box[0];
                double dy = box[4] - box[1];
                double dz = box[5] - box[2];
                double diag = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (diag < 1e-9)
                {
                    return AxisOverlayLengthFallback;
                }
                double length = diag * AxisOverlayLengthFraction;
                if (length < AxisOverlayLengthMin) length = AxisOverlayLengthMin;
                if (length > AxisOverlayLengthMax) length = AxisOverlayLengthMax;
                return length;
            }
            catch (Exception ex)
            {
                logger.Warn("ComputeAxisOverlayLength: " + ex.Message);
                return AxisOverlayLengthFallback;
            }
        }

        // Removes the active overlay manipulator, if any. Idempotent -
        // safe to call when no overlay is currently shown. Called on
        // every overlay refresh, on PM node-switch into a base node
        // (which has no joint axis), and on PM OnClose. Manipulators
        // hold a Show() reference on the active ModelDoc until Remove()
        // is called, so dropping the field without Remove() leaks the
        // arrow into the user's viewport across exports.
        public void ClearAxisOverlay()
        {
            if (axisManipulator == null)
            {
                logger.Info("[AxisOverlay] ClearAxisOverlay: no overlay to clear");
                return;
            }
            try
            {
                logger.Info("[AxisOverlay] ClearAxisOverlay: Manipulator.Remove ENTER");
                axisManipulator.Remove();
                logger.Info("[AxisOverlay] ClearAxisOverlay: Manipulator.Remove RETURNED");
            }
            catch (Exception ex)
            {
                logger.Warn("[AxisOverlay] ClearAxisOverlay: Remove failed: " + ex.Message);
            }
            axisManipulator = null;
        }

        //This method adds in the limits from a limit mate, to make a joint a revolute joint.
        // It really needs to checked for correctness.
        private static void AddLimits(Joint Joint, List<Mate2> limitMates,
            Component2 parentComponent, Component2 childComponent)
        {
            logger.Info("Parent SW Component: " + parentComponent.Name2);
            logger.Info("Child SW Component: " + childComponent.Name2);
            // The number of limit Mates should only be one. But for completeness, I cycle through
            // every found limit mate.
            foreach (Mate2 swMate in limitMates)
            {
                logger.Info("Determining limit mate eligibility ");
                List<Component2> entities = new List<Component2>();
                for (int i = 0; i < swMate.GetMateEntityCount(); i++)
                {
                    MateEntity2 entity = swMate.MateEntity(i);
                    
                    // Check if entity.ReferenceComponent is null and skip if so
                    if (entity.ReferenceComponent == null)
                    {
                        logger.Warn("Mate entity has no reference component");
                        continue;
                    }
                    
                    entities.Add(entity.ReferenceComponent);
                    logger.Info("Adding component entity: " + entity.ReferenceComponent.Name2);

                    Component2 parent = entity.ReferenceComponent.GetParent();
                    while (parent != null)
                    {
                        logger.Info("Adding component entity: " + parent.Name2);
                        entities.Add(parent);
                        parent = parent.GetParent();
                    }
                }

                if (entities.Contains(parentComponent) && entities.Contains(childComponent))
                {
                    // [TODO] This assumes the limit mate limits the right degree of freedom,
                    // it really should check that assumption
                    if (((Joint.Type == "revolute" || Joint.Type == "continuous") && swMate.Type ==
                            (int)swMateType_e.swMateANGLE) ||
                        (Joint.Type == "prismatic" && swMate.Type ==
                            (int)swMateType_e.swMateDISTANCE))
                    {
                        bool angularLimit = swMate.Type == (int)swMateType_e.swMateANGLE;
                        double minimumVariation = angularLimit
                            ? SW2RD.Input.Joint.RadiansToDegrees(swMate.MinimumVariation)
                            : swMate.MinimumVariation;
                        double maximumVariation = angularLimit
                            ? SW2RD.Input.Joint.RadiansToDegrees(swMate.MaximumVariation)
                            : swMate.MaximumVariation;

                        // Unclear if flipped is the right thing we want to be checking here.
                        // From a sample size of 1, in SolidWorks it appears that an aligned and
                        // anti-aligned mates are NOT flipped...
                        if (!swMate.Flipped)
                        {
                            // Reverse mate directions, for some reason
                            Joint.Limit.Upper = -minimumVariation;
                            Joint.Limit.Lower = -maximumVariation;
                        }
                        else
                        {
                            Joint.Limit.Upper = maximumVariation;
                            Joint.Limit.Lower = minimumVariation;
                        }
                        if (Joint.Type == "continuous")
                        {
                            Joint.Type = "revolute";
                        }
                    }
                }
            }
        }

        // Suppresses limit mates to make it easier to find the free degree of freedom in a joint
        private static List<Mate2> SuppressLimitMates(IComponent2 component)
        {
            List<Mate2> limitMates = new List<Mate2>();

            object[] objs = component.GetMates();

            //limit mates aren't always present
            if (objs != null)
            {
                foreach (object obj in objs)
                {
                    if (obj is Mate2 swMate)
                    {
                        if (swMate.MinimumVariation != swMate.MaximumVariation)
                        {
                            limitMates.Add(swMate);
                        }
                    }
                }
            }

            foreach (Mate2 swMate in limitMates)
            {
                Feature feat = (Feature)swMate;
                feat.Select(false);
                feat.SetSuppression2((int)swFeatureSuppressionAction_e.swSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
            }

            return limitMates;
        }

        // Unsuppresses limit mates that were suppressed before
        private static void UnsuppressLimitMates(List<Mate2> limitMates)
        {
            foreach (Mate2 swMate in limitMates)
            {
                Feature feat = (Feature)swMate;
                feat.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature,
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
            }
        }

        //Unfixes components that were fixed to find the free degree of freedom
        private void UnFixComponents(List<Component2> components)
        {
            foreach (Component2 comp in components)
            {
                logger.Info("Unfixing component " + comp.GetID());
            }

            CommonSwOperations.SelectComponents(ActiveSWModel, components, true);
            AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
            assy.UnfixComponent();
        }

        //Verifies that the reference geometry still exists. This can happen if the reference
        // geometry was deleted but the configuration was kept. The new
        // SelectionBox-only UI represents "auto" as AutoDeriveAxis=true
        // + empty AxisName; the legacy "Automatically Generate" sentinel
        // is also still accepted by CreateJoint for back-compat with old
        // saved configs that haven't been touched in the new PMPage yet.
        private void CheckRefGeometryExists(Link link)
        {
            if (!string.IsNullOrEmpty(link.Joint.CoordinateSystemName) &&
                link.Joint.CoordinateSystemName != "Automatically Generate" &&
                !IsSubComponentReference(link.Joint.CoordinateSystemName) &&
                !CheckRefCoordsysExists(link.Joint.CoordinateSystemName))
            {
                link.Joint.CoordinateSystemName = "";
            }
            if (!link.Joint.AutoDeriveAxis &&
                !string.IsNullOrEmpty(link.Joint.AxisName) &&
                link.Joint.AxisName != "Automatically Generate" &&
                !IsSubComponentReference(link.Joint.AxisName) &&
                !CheckRefAxisExists(link.Joint.AxisName))
            {
                link.Joint.AxisName = "";
            }
        }

        // A reference name carrying a "<component path>" suffix points at
        // geometry that lives INSIDE a sub-component (e.g.
        // "SWING_JOINT_CS <100-01080-1/.../830-02157-33>"). The reference lists
        // (ReferenceCoordinateSystemNames / ReferenceAxesNames) are built by
        // FindRefGeoNames -> GetFeaturesOfType, which only enumerates the
        // assembly plus its TOP-LEVEL components (GetComponents(true) + no
        // recursion). Deeply-nested sub-component geometry therefore never
        // appears in those lists, so a Contains() check produces a FALSE
        // NEGATIVE and CheckRefGeometryExists would silently wipe a perfectly
        // valid user-picked coordinate system / axis - which then collapses the
        // joint into the auto-generate path. We cannot reliably validate
        // sub-component references against the shallow list, so we leave them
        // intact here; if such a reference is genuinely missing it surfaces
        // later (and loudly) when GetCoordinateSystemTransform / GetRefAxis
        // fails to resolve it, rather than being destroyed up front.
        private static bool IsSubComponentReference(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                name.Contains("<") && name.Contains(">");
        }

        private bool CheckRefCoordsysExists(string OriginName)
        {
            return ReferenceCoordinateSystemNames.Contains(OriginName);
        }

        private bool CheckRefAxisExists(string AxisName)
        {
            return ReferenceAxesNames.Contains(AxisName);
        }

        private List<Component2> GetParentAncestorComponents(Link node)
        {
            List<Component2> components = new List<Component2>(node.SWComponents);
            if (node.Parent != null)
            {
                components.AddRange(GetParentAncestorComponents(node.Parent));
            }
            return components;
        }

        // Tier B2: components WE fixed during the current CreateRobotFromTreeView
        // joint-creation pass, keyed by Component2.Name2 (stable, unique path
        // name - unlike GetID which is not unique here) so a shared ancestor
        // chain is fixed at most ONCE per pass. Unfixed in bulk by
        // UnfixSessionFixedComponents from the CreateRobotFromTreeView finally.
        // Components the user had pre-fixed are deliberately NOT recorded here,
        // so we never unfix them and the user's pre-export state is preserved.
        private Dictionary<string, Component2> sessionFixedComponents;

        //Used to fix components to estimate the degree of freedom.
        //
        // Tier B2 correctness: a node's own degree of freedom is always
        // measured (as a child, in EstimateGlobalJointFromComponents) BEFORE it
        // is ever fixed (as an ancestor of its own children) because CreateLink
        // creates a node's joint before recursing into that node's children.
        // So fixing an ancestor and leaving it fixed for the rest of the pass
        // cannot corrupt a later DOF measurement - every later measurement is
        // of a descendant, for which that ancestor MUST be fixed anyway.
        private void FixComponents(Link parent)
        {
            logger.Info("Fixing components for " + parent.Name);
            List<Component2> componentsToFix = GetParentAncestorComponents(parent);
            List<Component2> newlyFixed = new List<Component2>();
            foreach (Component2 comp in componentsToFix)
            {
                if (comp == null)
                {
                    continue;
                }
                string key = comp.Name2;
                if (key != null && sessionFixedComponents != null &&
                    sessionFixedComponents.ContainsKey(key))
                {
                    // Already fixed earlier this pass by us - leave it fixed.
                    continue;
                }
                if (comp.IsFixed())
                {
                    // Pre-fixed by the user; never touch it (don't fix, don't
                    // record, don't unfix later).
                    continue;
                }
                newlyFixed.Add(comp);
                if (key != null && sessionFixedComponents != null)
                {
                    sessionFixedComponents[key] = comp;
                }
            }
            if (newlyFixed.Count > 0)
            {
                CommonSwOperations.SelectComponents(ActiveSWModel, newlyFixed, true);
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;
                assy.FixComponent();
            }
        }

        // Unfix every component WE fixed during the joint-creation pass in a
        // single bulk select + UnfixComponent round trip, then clear the
        // session set. No-op when nothing was fixed.
        private void UnfixSessionFixedComponents()
        {
            if (sessionFixedComponents == null || sessionFixedComponents.Count == 0)
            {
                sessionFixedComponents = null;
                return;
            }
            List<Component2> toUnfix = new List<Component2>(sessionFixedComponents.Values);
            sessionFixedComponents = null;
            UnFixComponents(toUnfix);
        }

        #endregion Joint methods
    }

    public enum MeshExportFormat
    {
        STL,
        THREEDXML
    }

    // Handler passed to ModelViewManager.CreateManipulator for the joint axis
    // direction overlay. SW requires a non-null handler when creating a
    // DragArrowManipulator (the canonical SW C# example explicitly constructs
    // one and we mirror that). The manipulator is fixed-length and non-draggable;
    // the only user interaction we consume is the built-in flip action, which
    // converges on the same AxisFlipped state as the bitmap button.
    //
    // [ComVisible(true)] is required because SW invokes these via COM
    // late-binding, NOT through the .NET interface dispatch table. If
    // you remove the attribute the manipulator will appear to create
    // successfully but SW will see no callable methods and silently
    // drop the manipulator on first display.
    //
    [System.Runtime.InteropServices.ComVisible(true)]
    public class AxisOverlayManipulatorHandler : ISwManipulatorHandler2
    {
        private readonly Action directionFlipped;

        public AxisOverlayManipulatorHandler()
        {
        }

        public AxisOverlayManipulatorHandler(Action directionFlipped)
        {
            this.directionFlipped = directionFlipped;
        }

        public bool OnDelete(object pManipulator) { return true; }
        public bool OnHandleLmbSelected(object pManipulator) { return true; }
        public bool OnDoubleValueChanged(object pManipulator, int handleIndex, ref double Value) { return true; }
        public bool OnStringValueChanged(object pManipulator, int handleIndex, ref string Value) { return true; }
        public void OnDirectionFlipped(object pManipulator)
        {
            directionFlipped?.Invoke();
        }
        public void OnEndDrag(object pManipulator, int handleIndex) { }
        public void OnEndNoDrag(object pManipulator, int handleIndex) { }
        public void OnHandleRmbSelected(object pManipulator, int handleIndex) { }
        public void OnHandleSelected(object pManipulator, int handleIndex) { }
        public void OnItemSetFocus(object pManipulator, int handleIndex) { }
        public void OnUpdateDrag(object pManipulator, int handleIndex, object newPosMathPt) { }
    }
}