using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace SW2URDF.MJCF
{
    // Bridge between the SW2URDF Link tree (already populated by ExportHelper with
    // localized joint origins / axes / inertials) and the MJCF data model. The builder
    // does not interact with SolidWorks directly: all transforms and components must
    // already be resolved in the Link tree.
    //
    // The mapping rules are:
    //   - URDF <joint><origin xyz=... rpy=.../></joint> for the joint connecting the
    //     parent link to a child link is encoded as `pos`/`quat` on the child <body>.
    //   - The MJCF <joint> child sits at pos=0 0 0 in the body frame and its `axis`
    //     is the URDF Joint.Axis (already localized to the body frame by the
    //     ExportHelper).
    //   - <geom>s are mesh references with no explicit pos/quat — STLs are exported
    //     in body-local coordinates, so the geom origin coincides with the body
    //     origin.
    //   - <site>s have pos/quat computed from a separate body-local transform and
    //     are populated by the caller (which has access to the live SolidWorks
    //     coordinate systems).
    public static class MJCFBuilder
    {
        // Site transform information used only when constructing the MJCF model. The
        // SolidWorks-side machinery is responsible for computing the body-local
        // transform for each site; the builder simply consumes the result.
        public class SiteTransform
        {
            public string Name;
            public double[] Position;
            public double[] Quaternion;
        }

        // Per-link auxiliary data that the export helper assembles. The builder does
        // not know about SolidWorks, so the caller supplies the pieces that depend on
        // SW state (mesh filenames, site transforms).
        public class LinkAuxiliary
        {
            public string VisualMeshName;
            public string CollisionMeshName;
            public string VisualMeshFile;     // file path stored under the meshdir
            public string CollisionMeshFile;
            public List<SiteTransform> Sites = new List<SiteTransform>();
        }

        // Builds an MJCFModel from a populated SW2URDF Robot. `auxByLinkName` carries
        // per-link mesh/site information that depends on the SolidWorks export step.
        public static MJCFModel Build(
            Robot robot,
            string meshDir,
            Dictionary<string, LinkAuxiliary> auxByLinkName)
        {
            if (robot == null)
            {
                throw new ArgumentNullException("robot");
            }
            MJCFModel model = new MJCFModel(robot.Name);
            model.Compiler.MeshDir = string.IsNullOrEmpty(meshDir) ? "meshes/" : meshDir;
            model.RootBody = BuildBody(robot.BaseLink, model.Asset, auxByLinkName, isRoot: true);
            return model;
        }

        private static Body BuildBody(
            Link link, Asset asset, Dictionary<string, LinkAuxiliary> auxByLinkName, bool isRoot)
        {
            Body body = new Body
            {
                Name = link.Name,
                SuppressTransform = isRoot,
            };

            if (!isRoot)
            {
                // For non-root links, the URDF joint origin (xyz/rpy) is the transform
                // from the parent's body frame to the child's body frame; that's the
                // pos/quat we stamp on the child <body>.
                body.Position = link.Joint.Origin.GetXYZ();
                body.Quaternion = MathOps.RPYToQuaternion(link.Joint.Origin.GetRPY());
            }

            body.Inertial = BuildInertial(link);

            // The root link has no incoming joint by convention; for a fixed joint,
            // we omit the joint element so the body is rigidly attached to its parent.
            if (!isRoot && link.Joint != null && link.Joint.Type != "fixed")
            {
                body.Joint = BuildJoint(link.Joint);
            }

            LinkAuxiliary aux = null;
            if (auxByLinkName != null)
            {
                auxByLinkName.TryGetValue(link.Name, out aux);
            }

            // Visual/collision geoms — only emitted if the corresponding STL was
            // actually exported (presence of mesh name implies a mesh asset).
            if (aux != null)
            {
                if (!string.IsNullOrEmpty(aux.VisualMeshName))
                {
                    asset.Add(new MeshAsset(aux.VisualMeshName, aux.VisualMeshFile));
                    body.Geoms.Add(new Geom(
                        link.Name + "_visual", aux.VisualMeshName, GeomRole.Visual));
                }
                if (!string.IsNullOrEmpty(aux.CollisionMeshName))
                {
                    asset.Add(new MeshAsset(aux.CollisionMeshName, aux.CollisionMeshFile));
                    body.Geoms.Add(new Geom(
                        link.Name + "_collision", aux.CollisionMeshName, GeomRole.Collision));
                }
                if (aux.Sites != null)
                {
                    foreach (SiteTransform st in aux.Sites)
                    {
                        body.Sites.Add(new Site
                        {
                            Name = st.Name,
                            Position = st.Position ?? new double[] { 0, 0, 0 },
                            Quaternion = st.Quaternion ?? new double[] { 1, 0, 0, 0 },
                        });
                    }
                }
            }

            foreach (Link childLink in link.Children)
            {
                body.Children.Add(BuildBody(childLink, asset, auxByLinkName, isRoot: false));
            }

            return body;
        }

        private static MJCFInertial BuildInertial(Link link)
        {
            // Skip inertial for fixed-frame "links" (they're not real bodies; they
            // mark a coordinate frame on the parent).
            if (link.isFixedFrame)
            {
                return null;
            }
            // If mass is essentially zero, there's no useful inertial to emit and
            // MuJoCo will fall back to the geom-derived inertia (if any).
            if (link.Inertial == null || link.Inertial.Mass.Value <= 0.0)
            {
                return null;
            }

            MJCFInertial inertial = new MJCFInertial
            {
                Position = link.Inertial.Origin.GetXYZ(),
                Mass = link.Inertial.Mass.Value,
                FullInertia = new double[]
                {
                    link.Inertial.Inertia.Ixx,
                    link.Inertial.Inertia.Iyy,
                    link.Inertial.Inertia.Izz,
                    link.Inertial.Inertia.Ixy,
                    link.Inertial.Inertia.Ixz,
                    link.Inertial.Inertia.Iyz,
                },
                HasInertia = true,
            };
            return inertial;
        }

        private static MJCFJoint BuildJoint(Joint urdfJoint)
        {
            MJCFJoint mjJoint = new MJCFJoint
            {
                Name = urdfJoint.Name,
                Axis = urdfJoint.Axis.GetXYZ(),
                Position = new double[] { 0, 0, 0 },
            };
            if (MJCFJointTypeExtensions.TryFromURDFType(urdfJoint.Type, out MJCFJointType mjType))
            {
                mjJoint.Type = mjType;
            }
            else
            {
                // "fixed" should have been filtered out earlier, but if we get here
                // (e.g. unknown URDF type), default to a hinge so the file is still
                // syntactically valid.
                mjJoint.Type = MJCFJointType.Hinge;
            }

            if (urdfJoint.Type == "revolute" || urdfJoint.Type == "prismatic")
            {
                if (urdfJoint.Limit != null)
                {
                    mjJoint.HasLimits = true;
                    mjJoint.LowerLimit = urdfJoint.Limit.Lower;
                    mjJoint.UpperLimit = urdfJoint.Limit.Upper;
                }
            }
            if (urdfJoint.Dynamics != null)
            {
                // Dynamics fields are always present in the URDF model but may be
                // zero/blank; MuJoCo defaults handle a zero damping just fine.
            }

            return mjJoint;
        }

        // Joins a directory and a filename using the MuJoCo convention of forward
        // slashes (the meshdir is used verbatim and prepended to each <mesh file=...>).
        public static string CombineMeshPath(string dir, string filename)
        {
            if (string.IsNullOrEmpty(dir))
            {
                return filename;
            }
            string trimmed = dir.TrimEnd('/', '\\');
            return trimmed + "/" + Path.GetFileName(filename);
        }
    }
}
