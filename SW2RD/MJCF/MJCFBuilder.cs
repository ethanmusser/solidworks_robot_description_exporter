using SW2RD.Core;
using SW2RD.URDF;
using SW2RD.Export;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.IO;

namespace SW2RD.MJCF
{
    // Phase 2: the MJCF builder consumes the format-neutral KinematicTree
    // (SW2RD.Core) directly. Callers that still hold a legacy Robot
    // tree go through KinematicTreeAdapter.ToCore at the boundary; the
    // builder itself knows nothing about URDFElement / SolidWorks types.
    //
    // Mapping rules:
    //   - URDF <joint><origin xyz=... rpy=.../></joint> for the joint
    //     connecting the parent link to a child link is encoded as
    //     pos/quat on the child <body>.
    //   - The MJCF <joint> child sits at pos=0 0 0 in the body frame and
    //     its `axis` is the joint axis (already localized to the body
    //     frame upstream by ExportHelper).
    //   - <geom>s are mesh references with no explicit pos/quat; STLs are
    //     exported in body-local coordinates so the geom origin coincides
    //     with the body origin.
    //   - <site>s have pos/quat computed from a separate body-local
    //     transform and are populated by the caller (which has access to
    //     the live SolidWorks coordinate systems) via LinkAuxiliary.
    internal static class MJCFBuilder
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        // Path written into <compiler texturedir="..."> for MJCF packages.
        // Mirrors the meshdir convention: relative to the model XML which
        // lives in mjcf/.
        public const string DefaultTextureDir = "../textures/";

        /// <summary>
        /// Synthetic key used in <c>auxByLinkName</c> to carry world-direct
        /// geometry / sites (the worldbody's own meshes, ground planes,
        /// scene fiducials). Distinct from any real link name (LinkModel.Name
        /// cannot legally start with '&lt;').
        /// </summary>
        public const string WorldAuxKey = "<world>";

        // Canonical entry point. Walks the KinematicTree and assembles the
        // MJCFModel. `auxByLinkName` carries per-link mesh/site information
        // that depends on the SolidWorks export step (mesh filenames, site
        // poses) and is keyed by the LinkModel.Name. World-direct geometry
        // is keyed by <see cref="WorldAuxKey"/>.
        public static MJCFModel Build(
            KinematicTree tree,
            string meshDir,
            Dictionary<string, LinkAuxiliary> auxByLinkName)
        {
            if (tree == null)
            {
                throw new ArgumentNullException(nameof(tree));
            }

            MJCFModel model = new MJCFModel(tree.Name ?? "");
            model.Compiler.MeshDir = string.IsNullOrEmpty(meshDir) ? "meshes/" : meshDir;
            model.Compiler.TextureDir = DefaultTextureDir;

            // Reset the default RootBody seed so we can build TopLevelBodies
            // from scratch via the multi-tree walk below.
            model.TopLevelBodies.Clear();
            model.WorldGeoms.Clear();
            model.WorldSites.Clear();

            LinkModel worldBody = tree.WorldBody;
            IReadOnlyList<LinkModel> topLevels =
                worldBody?.Children ?? Array.Empty<LinkModel>();
            foreach (LinkModel topLevel in topLevels)
            {
                if (topLevel == null)
                {
                    continue;
                }
                Body body = BuildBody(topLevel, model.Asset, auxByLinkName, isRoot: true);

                // World->body offset: when the body's reference frame
                // matches the world's global origin coord-sys, the legacy
                // single-tree byte-identical output is preserved
                // (SuppressTransform=true). When they differ, the body
                // carries an explicit pos/quat - which is computed by
                // the export pipeline (ExportHelperExtension's
                // localization step) and stamped on link.Joint.Origin.
                if (!IsWorldOffsetIdentity(tree.GlobalOriginCoordinateSystemName, topLevel))
                {
                    Vector3Model pos = topLevel.Joint?.Origin?.Position ?? new Vector3Model(0, 0, 0);
                    RpyModel rpy = topLevel.Joint?.Origin?.Rotation ?? new RpyModel(0, 0, 0);
                    body.SuppressTransform = false;
                    body.Position = new[] { pos.X, pos.Y, pos.Z };
                    body.Quaternion = MathOps.RPYToQuaternion(new[] { rpy.Roll, rpy.Pitch, rpy.Yaw });
                }
                else
                {
                    body.SuppressTransform = true;
                }

                if (topLevel.WorldAttachment == WorldAttachmentModel.Free)
                {
                    body.HasFreeJoint = true;
                }

                model.TopLevelBodies.Add(body);
            }

            // World-direct geometry / sites. The WorldAuxKey carries the
            // mesh filenames / site transforms that the SW export step
            // pre-computed. Empty world collections produce no <geom> /
            // <site> elements at all, preserving today's single-tree output.
            EmitWorldGeometry(worldBody, model, auxByLinkName);

            return model;
        }

        // True when the top-level body's reference frame coincides with the
        // world's global origin (so MJCF emits SuppressTransform=true and
        // the byte diff against today's single-tree output is zero). The
        // canonical legacy case has both names equal to oldRoot.Joint.
        // CoordinateSystemName, so the string compare suffices; if the
        // names differ but the resolved transforms are equal, the export
        // pipeline must have already stamped Origin to identity, so we
        // also check for that.
        private static bool IsWorldOffsetIdentity(string globalOriginName, LinkModel topLevel)
        {
            string worldName = globalOriginName ?? "";
            string bodyName = topLevel?.Joint?.CoordinateSystemName ?? "";
            if (string.Equals(worldName, bodyName, StringComparison.Ordinal))
            {
                return true;
            }
            // Origin pose all zeros also implies identity. Defensive against
            // a pipeline that resolves the offset down to zero numerically
            // even when the names differ (e.g. two different feature names
            // pointing at the same global frame).
            PoseModel origin = topLevel?.Joint?.Origin;
            if (origin == null)
            {
                return true;
            }
            Vector3Model pos = origin.Position ?? new Vector3Model(0, 0, 0);
            RpyModel rpy = origin.Rotation ?? new RpyModel(0, 0, 0);
            return pos.X == 0.0 && pos.Y == 0.0 && pos.Z == 0.0
                && rpy.Roll == 0.0 && rpy.Pitch == 0.0 && rpy.Yaw == 0.0;
        }

        // Emits world-direct <geom> and <site> elements. Reuses the
        // EmitVisualGeoms / EmitCollisionGeoms / EmitSites helpers via a
        // synthetic Body that we drain into model.WorldGeoms / WorldSites.
        // This keeps the asset-deduplication + material-naming logic
        // identical between body-level and world-level emission.
        private static void EmitWorldGeometry(
            LinkModel world,
            MJCFModel model,
            Dictionary<string, LinkAuxiliary> auxByLinkName)
        {
            if (world == null || auxByLinkName == null)
            {
                return;
            }
            if (!auxByLinkName.TryGetValue(WorldAuxKey, out LinkAuxiliary aux) || aux == null)
            {
                return;
            }

            Body scratch = new Body { Name = "world" };
            EmitVisualGeoms(scratch, model.Asset, world, aux);
            EmitCollisionGeoms(scratch, model.Asset, world, aux);
            EmitSites(scratch, aux);

            model.WorldGeoms.AddRange(scratch.Geoms);
            model.WorldSites.AddRange(scratch.Sites);
        }

        // Convenience overload retained for the export pipeline, which still
        // builds the legacy Robot at the SolidWorks boundary. Forwards
        // through the adapter and into the KinematicTree-native path so
        // there's exactly one BuildBody implementation.
        public static MJCFModel Build(
            Robot robot,
            string meshDir,
            Dictionary<string, LinkAuxiliary> auxByLinkName)
        {
            if (robot == null)
            {
                throw new ArgumentNullException(nameof(robot));
            }
            return Build(KinematicTreeAdapter.ToCore(robot), meshDir, auxByLinkName);
        }

        private static Body BuildBody(
            LinkModel link, Asset asset, Dictionary<string, LinkAuxiliary> auxByLinkName, bool isRoot)
        {
            Body body = new Body
            {
                Name = link.Name ?? "",
                SuppressTransform = isRoot,
            };

            if (!isRoot && link.Joint != null && link.Joint.Origin != null)
            {
                // For non-root links, the URDF joint origin (xyz/rpy) is the
                // transform from the parent's body frame to the child's
                // body frame; that's the pos/quat we stamp on the child
                // <body>.
                Vector3Model pos = link.Joint.Origin.Position ?? new Vector3Model(0, 0, 0);
                RpyModel rpy = link.Joint.Origin.Rotation ?? new RpyModel(0, 0, 0);
                body.Position = new[] { pos.X, pos.Y, pos.Z };
                body.Quaternion = MathOps.RPYToQuaternion(new[] { rpy.Roll, rpy.Pitch, rpy.Yaw });
            }

            body.Inertial = BuildInertial(link);

            // The root link has no incoming joint by convention; for a
            // fixed joint we omit the joint element so the body is rigidly
            // attached to its parent.
            if (!isRoot && link.Joint != null && !string.Equals(link.Joint.Type, "fixed", StringComparison.Ordinal))
            {
                body.Joint = BuildJoint(link.Joint);
            }

            LinkAuxiliary aux = null;
            if (auxByLinkName != null && !string.IsNullOrEmpty(link.Name))
            {
                auxByLinkName.TryGetValue(link.Name, out aux);
            }

            // Visual / collision geoms - one mesh asset and one geom per
            // group. A link with two visual groups gets two <mesh> entries
            // in <asset> and two <geom class="visual"> children of the
            // body. The same applies to collision; this is what lets
            // MuJoCo represent a concave shape as a union of convex hulls
            // (one hull per group).
            //
            // Geom names disambiguate role and group index so a body with
            //   * one visual + one collision -> "<link>_visual" / "<link>_collision"
            //   * N visuals (N > 1)          -> "<link>_visual_1..N"
            // remain unique even when collision reuses a visual mesh asset.
            if (aux != null)
            {
                EmitVisualGeoms(body, asset, link, aux);
                EmitCollisionGeoms(body, asset, link, aux);
                EmitSites(body, aux);
            }

            if (link.Children != null)
            {
                foreach (LinkModel childLink in link.Children)
                {
                    body.Children.Add(BuildBody(childLink, asset, auxByLinkName, isRoot: false));
                }
            }

            return body;
        }

        private static MJCFInertial BuildInertial(LinkModel link)
        {
            // Skip inertial for fixed-frame "links" (they're not real
            // bodies; they mark a coordinate frame on the parent).
            if (link.IsFixedFrame)
            {
                return null;
            }
            // If mass is essentially zero, there's no useful inertial to
            // emit and MuJoCo will fall back to the geom-derived inertia
            // (if any).
            if (link.Inertial == null || link.Inertial.Mass <= 0.0)
            {
                return null;
            }

            Vector3Model pos = link.Inertial.Origin?.Position ?? new Vector3Model(0, 0, 0);
            InertiaTensorModel inertia = link.Inertial.Inertia ?? new InertiaTensorModel(0, 0, 0, 0, 0, 0);
            return new MJCFInertial
            {
                Position = new[] { pos.X, pos.Y, pos.Z },
                Mass = link.Inertial.Mass,
                FullInertia = new[]
                {
                    inertia.Ixx,
                    inertia.Iyy,
                    inertia.Izz,
                    inertia.Ixy,
                    inertia.Ixz,
                    inertia.Iyz,
                },
                HasInertia = true,
            };
        }

        private static MJCFJoint BuildJoint(JointModel urdfJoint)
        {
            Vector3Model axisVec = urdfJoint.Axis ?? new Vector3Model(0, 0, 1);
            MJCFJoint mjJoint = new MJCFJoint
            {
                Name = urdfJoint.Name ?? "",
                Axis = new[] { axisVec.X, axisVec.Y, axisVec.Z },
                Position = new double[] { 0, 0, 0 },
            };
            if (MJCFJointTypeExtensions.TryFromURDFType(urdfJoint.Type ?? "", out MJCFJointType mjType))
            {
                mjJoint.Type = mjType;
            }
            else
            {
                // "fixed" should have been filtered out earlier, but if we
                // get here (e.g. unknown URDF type), default to a hinge so
                // the file is still syntactically valid.
                mjJoint.Type = MJCFJointType.Hinge;
            }

            if (urdfJoint.Type == "revolute" || urdfJoint.Type == "prismatic")
            {
                if (urdfJoint.Limit != null && urdfJoint.Limit.Lower.HasValue && urdfJoint.Limit.Upper.HasValue)
                {
                    mjJoint.HasLimits = true;
                    mjJoint.LowerLimit = urdfJoint.Limit.Lower.Value;
                    mjJoint.UpperLimit = urdfJoint.Limit.Upper.Value;
                }
            }

            // Effort -> MJCF actuatorfrcrange = [-effort, +effort]. Mirrors
            // URDF's single-magnitude effort convention. Free / ball
            // joints don't accept actuatorfrcrange and skip the attribute
            // in the writer.
            if (urdfJoint.Limit != null && urdfJoint.Limit.Effort.HasValue
                && urdfJoint.Limit.Effort.Value > 0.0)
            {
                mjJoint.HasEffort = true;
                mjJoint.Effort = urdfJoint.Limit.Effort.Value;
            }

            // URDF velocity has no MJCF analog. Note once per build for
            // visibility, then proceed without emitting an attribute.
            if (urdfJoint.Limit != null && urdfJoint.Limit.Velocity.HasValue
                && urdfJoint.Limit.Velocity.Value > 0.0)
            {
                logger.Info("MJCF: <joint velocity> has no MJCF equivalent; " +
                    "dropping velocity=" + urdfJoint.Limit.Velocity.Value +
                    " on joint '" + (urdfJoint.Name ?? "") + "'.");
            }

            if (urdfJoint.Damping.HasValue)
            {
                mjJoint.HasDamping = true;
                mjJoint.Damping = urdfJoint.Damping.Value;
            }
            if (urdfJoint.Friction.HasValue)
            {
                mjJoint.HasFriction = true;
                mjJoint.Friction = urdfJoint.Friction.Value;
            }
            if (urdfJoint.Armature.HasValue)
            {
                mjJoint.HasArmature = true;
                mjJoint.Armature = urdfJoint.Armature.Value;
            }
            if (urdfJoint.Reference.HasValue)
            {
                mjJoint.HasRef = true;
                mjJoint.Ref = urdfJoint.Reference.Value;
            }

            return mjJoint;
        }

        private static void EmitVisualGeoms(Body body, Asset asset, LinkModel link, LinkAuxiliary aux)
        {
            if (aux.VisualMeshes == null)
            {
                return;
            }
            int visualCount = aux.VisualMeshes.Count;
            if (visualCount == 0)
            {
                return;
            }

            // One <material> per link with at least one visual mesh.
            // Multi-group visual links share this material on every visual
            // <geom>.
            string materialName = EnsureLinkMaterial(asset, link);

            int visualIndex = 0;
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

        private static void EmitCollisionGeoms(Body body, Asset asset, LinkModel link, LinkAuxiliary aux)
        {
            if (aux.CollisionMeshes == null)
            {
                return;
            }
            int collisionCount = aux.CollisionMeshes.Count;
            int collisionIndex = 0;
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
                // Collision geoms intentionally carry neither rgba nor
                // material; they inherit the rgba from
                // <default class="collision"> so all collision hulls
                // render at a uniform tint regardless of link.
                body.Geoms.Add(new Geom(geomName, meshRef.Name, GeomRole.Collision));
                collisionIndex++;
            }
        }

        private static void EmitSites(Body body, LinkAuxiliary aux)
        {
            if (aux.Sites == null)
            {
                return;
            }
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

        // Adds (idempotently) a <material> for the given link to the asset
        // block, plus the corresponding <texture> if the link has a
        // non-empty Texture filename pointing at a file. The Color element
        // is populated either by the user (Configure Link Properties form)
        // or automatically from SolidWorks
        // (ComputeVisualCollisionProperties reads the part's
        // MaterialPropertyValues). Defaults to white-opaque so a link
        // whose color was never populated still emits syntactically valid
        // rgba.
        //
        // Returns the material's <name> so the caller can stamp it on the
        // geom. Material names must be unique within <asset> in MJCF: if
        // the chosen name is already taken (because two links share a
        // custom material name set in the form), Asset.Add returns false
        // and we log a warning. The second link's geoms still reference
        // the existing material, which means they render with the first
        // link's color/texture. Acceptable degradation; the user can fix
        // it by giving the second link a distinct name.
        private static string EnsureLinkMaterial(Asset asset, LinkModel link)
        {
            string materialName = ChooseMaterialName(link);

            string textureName = null;
            string textureFilename = link?.Material?.TextureFilename;
            if (!string.IsNullOrWhiteSpace(textureFilename))
            {
                textureName = "texture_" + ((link != null && !string.IsNullOrWhiteSpace(link.Name))
                    ? link.Name
                    : "link");
                string textureFile = Path.GetFileName(textureFilename);
                if (!asset.Add(new TextureAsset(textureName, textureFile)))
                {
                    // Same name already present - not an error per se, but
                    // worth a breadcrumb if the underlying file path
                    // differs.
                    logger.Info("MJCF texture '" + textureName +
                        "' already declared in <asset>; reusing existing entry.");
                }
            }

            RgbaModel color = link?.Material?.Color ?? new RgbaModel(1, 1, 1, 1);
            double[] rgba = new[] { color.Red, color.Green, color.Blue, color.Alpha };

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
        // Material.Name when set (typically by the form's
        // comboBoxMaterials or by ComputeVisualCollisionProperties which
        // writes "material_<linkname>"); falls back to "material_<linkname>"
        // otherwise. Always returns a non-empty string.
        private static string ChooseMaterialName(LinkModel link)
        {
            string explicitName = link?.Material?.Name;
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

        // Joins a directory and a filename using the MuJoCo convention of
        // forward slashes (the meshdir is used verbatim and prepended to
        // each <mesh file=...>).
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
