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
        private static readonly log4net.ILog logger = Logger.GetLogger();

        // Path written into <compiler texturedir="..."> for MJCF packages. Mirrors
        // the meshdir convention: relative to the model XML which lives in mjcf/.
        public const string DefaultTextureDir = "../textures/";

        // Site transform information used only when constructing the MJCF model. The
        // SolidWorks-side machinery is responsible for computing the body-local
        // transform for each site; the builder simply consumes the result.
        public class SiteTransform
        {
            public string Name;
            public double[] Position;
            public double[] Quaternion;
        }

        // A single mesh reference: a logical name that becomes the <mesh> asset's
        // `name` attribute and the file path (relative to the model's meshdir)
        // that lands in the asset's `file` attribute. ExportHelper assembles one
        // MeshAssetRef per visual / collision group on a link.
        public class MeshAssetRef
        {
            public string Name;
            public string File;
        }

        // Per-link auxiliary data that the export helper assembles. The builder
        // does not know about SolidWorks, so the caller supplies the pieces that
        // depend on SW state (mesh filenames, site transforms).
        public class LinkAuxiliary
        {
            // One entry per visual group on the link. Empty list -> no <geom
            // class="visual"> emitted on this body. Each entry produces one
            // <mesh> in <asset> and one <geom> in the body.
            public List<MeshAssetRef> VisualMeshes = new List<MeshAssetRef>();

            // One entry per collision group on the link. Empty list -> no
            // <geom class="collision"> emitted unless the legacy single-mesh
            // fallback below is used.
            public List<MeshAssetRef> CollisionMeshes = new List<MeshAssetRef>();

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
            model.Compiler.TextureDir = DefaultTextureDir;
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

            // Visual/collision geoms — one mesh asset and one geom per group.
            // A link with two visual groups gets two <mesh> entries in <asset>
            // and two <geom class="visual"> children of the body. The same
            // applies to collision; this is what lets MuJoCo represent a
            // concave shape as a union of convex hulls (one hull per group).
            //
            // Geom names disambiguate role and group index so a body with
            //   * one visual + one collision      -> "<link>_visual" / "<link>_collision"
            //   * N visuals (N > 1)               -> "<link>_visual_1..N"
            // remain unique even when collision reuses a visual mesh asset.
            if (aux != null)
            {
                int visualCount = (aux.VisualMeshes != null) ? aux.VisualMeshes.Count : 0;
                int visualIndex = 0;
                string materialName = null;
                if (aux.VisualMeshes != null && visualCount > 0)
                {
                    // One <material> per link with at least one visual mesh. Multi-group
                    // visual links share this material on every visual <geom>.
                    materialName = EnsureLinkMaterial(asset, link);
                }
                if (aux.VisualMeshes != null)
                {
                    foreach (MeshAssetRef meshRef in aux.VisualMeshes)
                    {
                        if (meshRef == null || string.IsNullOrEmpty(meshRef.Name))
                        {
                            continue;
                        }
                        asset.Add(new MeshAsset(meshRef.Name, meshRef.File));
                        string geomName = (visualCount == 1)
                            ? link.Name + "_visual"
                            : link.Name + "_visual_" + (visualIndex + 1);
                        body.Geoms.Add(new Geom(geomName, meshRef.Name, GeomRole.Visual)
                        {
                            Material = materialName,
                        });
                        visualIndex++;
                    }
                }

                int collisionCount = (aux.CollisionMeshes != null) ? aux.CollisionMeshes.Count : 0;
                int collisionIndex = 0;
                if (aux.CollisionMeshes != null)
                {
                    foreach (MeshAssetRef meshRef in aux.CollisionMeshes)
                    {
                        if (meshRef == null || string.IsNullOrEmpty(meshRef.Name))
                        {
                            continue;
                        }
                        asset.Add(new MeshAsset(meshRef.Name, meshRef.File));
                        string geomName = (collisionCount == 1)
                            ? link.Name + "_collision"
                            : link.Name + "_collision_" + (collisionIndex + 1);
                        // Collision geoms intentionally carry neither rgba nor material;
                        // they inherit the rgba from <default class="collision"> so all
                        // collision hulls render at a uniform tint regardless of link.
                        body.Geoms.Add(new Geom(geomName, meshRef.Name, GeomRole.Collision));
                        collisionIndex++;
                    }
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

        // Adds (idempotently) a <material> for the given link to the asset block,
        // plus the corresponding <texture> if the link has a non-empty
        // Texture.wFilename pointing at a file. The Color element is populated
        // either by the user (Configure Link Properties form) or automatically
        // from SolidWorks (ComputeVisualCollisionProperties reads the part's
        // MaterialPropertyValues). Defaults to white-opaque so a link whose
        // color was never populated still emits syntactically valid rgba; URDF
        // Color() initializes to {1,1,1,1} for the same reason.
        //
        // Returns the material's <name> so the caller can stamp it on the geom.
        // Material names must be unique within <asset> in MJCF: if the chosen
        // name is already taken (because two links share a custom material name
        // set in the form), Asset.Add returns false and we log a warning. The
        // second link's geoms still reference the existing material, which
        // means they render with the first link's color/texture. Acceptable
        // degradation; the user can fix it by giving the second link a distinct
        // name.
        private static string EnsureLinkMaterial(Asset asset, Link link)
        {
            string materialName = ChooseMaterialName(link);

            string textureName = null;
            string textureWFilename = link?.Visual?.Material?.Texture?.wFilename;
            if (!string.IsNullOrWhiteSpace(textureWFilename))
            {
                textureName = "texture_" + ((link != null && !string.IsNullOrWhiteSpace(link.Name))
                    ? link.Name
                    : "link");
                string textureFile = Path.GetFileName(textureWFilename);
                if (!asset.Add(new TextureAsset(textureName, textureFile)))
                {
                    // Same name already present -- not an error per se, but worth a
                    // breadcrumb if the underlying file path differs.
                    logger.Info("MJCF texture '" + textureName +
                        "' already declared in <asset>; reusing existing entry.");
                }
            }

            double[] rgba = (link != null
                && link.Visual != null
                && link.Visual.Material != null
                && link.Visual.Material.Color != null)
                ? link.Visual.Material.Color.GetColor()
                : new double[] { 1, 1, 1, 1 };

            MaterialAsset newMaterial = new MaterialAsset(materialName, rgba)
            {
                Texture = textureName,
            };
            if (!asset.Add(newMaterial))
            {
                MaterialAsset existing = asset.FindMaterial(materialName);
                if (existing != null && !MaterialMatches(existing, newMaterial))
                {
                    logger.Warn("MJCF material name '" + materialName +
                        "' is reused by link '" + (link?.Name ?? "<null>") +
                        "' with different rgba/texture. The first link's material " +
                        "definition wins; this link's geoms will render with that " +
                        "color/texture instead. Give the link a distinct material " +
                        "name in the Configure Link Properties form to fix.");
                }
            }
            return materialName;
        }

        // Picks the material name for a link. Honours a user-supplied
        // Link.Visual.Material.Name when set (typically by the form's
        // comboBoxMaterials or by ComputeVisualCollisionProperties which writes
        // "material_<linkname>"); falls back to "material_<linkname>" otherwise.
        // Always returns a non-empty string.
        private static string ChooseMaterialName(Link link)
        {
            string explicitName = link?.Visual?.Material?.Name;
            if (!string.IsNullOrWhiteSpace(explicitName))
            {
                return explicitName;
            }
            string baseName = (link != null && !string.IsNullOrWhiteSpace(link.Name))
                ? link.Name
                : "link";
            return "material_" + baseName;
        }

        private static bool MaterialMatches(MaterialAsset a, MaterialAsset b)
        {
            if (a == null || b == null)
            {
                return false;
            }
            if (!string.Equals(a.Texture ?? "", b.Texture ?? "", StringComparison.Ordinal))
            {
                return false;
            }
            if (a.Rgba == null || b.Rgba == null
                || a.Rgba.Length != 4 || b.Rgba.Length != 4)
            {
                return a.Rgba == b.Rgba;
            }
            for (int i = 0; i < 4; i++)
            {
                if (a.Rgba[i] != b.Rgba[i])
                {
                    return false;
                }
            }
            return true;
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
