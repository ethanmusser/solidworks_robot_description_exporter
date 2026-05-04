using System.Collections.Generic;

namespace SW2URDF.MJCF
{
    // Site transform information used only when constructing the MJCF model. The
    // SolidWorks-side machinery is responsible for computing the body-local
    // transform for each site; the builder simply consumes the result.
    //
    // Top-level (rather than nested in MJCFBuilder) so the analyzer doesn't fire
    // CA1034 and so callers can name it without the MJCFBuilder prefix.
    internal class SiteTransform
    {
        public string Name;
        public double[] Position;
        public double[] Quaternion;
    }

    // A single mesh reference: a logical name that becomes the <mesh> asset's
    // `name` attribute and the file path (relative to the model's meshdir)
    // that lands in the asset's `file` attribute. ExportHelper assembles one
    // MeshAssetRef per visual / collision group on a link.
    internal class MeshAssetRef
    {
        public string Name;
        public string File;
    }

    // Per-link auxiliary data that the export helper assembles. The builder
    // does not know about SolidWorks, so the caller supplies the pieces that
    // depend on SW state (mesh filenames, site transforms).
    internal class LinkAuxiliary
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
}
