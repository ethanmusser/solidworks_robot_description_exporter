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

using System.Collections.Generic;

namespace SW2RD.MJCF
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
