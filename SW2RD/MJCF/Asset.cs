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
using System.Xml;

namespace SW2RD.MJCF
{
    // A reference to a mesh STL file. MuJoCo expects unique names per mesh.
    internal class MeshAsset
    {
        public string Name { get; set; }
        public string File { get; set; }

        public MeshAsset(string name, string file)
        {
            Name = name;
            File = file;
        }
    }

    // A 2D surface texture (PNG / JPG / etc.) referenced from a <material>. The file
    // path is resolved relative to <compiler texturedir="..."> (set by MJCFBuilder)
    // so we only need the filename's basename here. MuJoCo requires <texture>
    // declarations to appear before any <material> that references them — Asset
    // emits in that order regardless of insertion order.
    internal class TextureAsset
    {
        public string Name { get; set; }
        public string File { get; set; }

        public TextureAsset(string name, string file)
        {
            Name = name;
            File = file;
        }
    }

    // <material name="..." rgba="r g b a" texture="..."/> emitted under <asset>.
    // Texture is optional; a null/empty value omits the attribute. Future fields
    // (specular, shininess, emission, reflectance) plug in here without touching
    // geom emission — visual <geom>s reference materials by name.
    internal class MaterialAsset
    {
        public string Name { get; set; }
        public double[] Rgba { get; set; }
        public string Texture { get; set; }

        public MaterialAsset(string name, double[] rgba)
        {
            Name = name;
            Rgba = rgba;
        }
    }

    // Emits the <asset> block. Emits in the MuJoCo-required order:
    //   <texture>...</texture>     (referenced by materials)
    //   <material>...</material>   (referenced by visual geoms)
    //   <mesh>...</mesh>           (referenced by all geoms)
    // The block is omitted entirely if there is nothing to declare.
    internal class Asset
    {
        public List<MeshAsset> Meshes { get; }
        public List<TextureAsset> Textures { get; }
        public List<MaterialAsset> Materials { get; }

        public Asset()
        {
            Meshes = new List<MeshAsset>();
            Textures = new List<TextureAsset>();
            Materials = new List<MaterialAsset>();
        }

        public void Add(MeshAsset mesh)
        {
            // Avoid emitting duplicate <mesh name=...> entries when a body re-uses the
            // same STL file across visual and collision (or across links).
            foreach (MeshAsset existing in Meshes)
            {
                if (existing.Name == mesh.Name)
                {
                    return;
                }
            }
            Meshes.Add(mesh);
        }

        // Returns true if added, false if a texture with the same name already exists.
        // Caller can use the return value to detect duplicate-name collisions and
        // log a warning if the underlying file differs.
        public bool Add(TextureAsset texture)
        {
            foreach (TextureAsset existing in Textures)
            {
                if (existing.Name == texture.Name)
                {
                    return false;
                }
            }
            Textures.Add(texture);
            return true;
        }

        // Returns true if added, false if a material with the same name already exists.
        // Material names must be globally unique within <asset> in MJCF, so a same-name
        // collision means the second link gets the FIRST link's color/texture; the
        // builder logs a warning when it detects this case.
        public bool Add(MaterialAsset material)
        {
            foreach (MaterialAsset existing in Materials)
            {
                if (existing.Name == material.Name)
                {
                    return false;
                }
            }
            Materials.Add(material);
            return true;
        }

        // Lookup helper used by the builder to decide whether a same-named material
        // about to be added is a true duplicate (same color/texture) or a conflict.
        public MaterialAsset FindMaterial(string name)
        {
            foreach (MaterialAsset existing in Materials)
            {
                if (existing.Name == name)
                {
                    return existing;
                }
            }
            return null;
        }

        public void WriteMJCF(XmlWriter writer)
        {
            if (Textures.Count == 0 && Materials.Count == 0 && Meshes.Count == 0)
            {
                return;
            }
            writer.WriteStartElement("asset");

            foreach (TextureAsset tex in Textures)
            {
                writer.WriteStartElement("texture");
                writer.WriteAttributeString("name", tex.Name);
                writer.WriteAttributeString("type", "2d");
                writer.WriteAttributeString("file", tex.File);
                writer.WriteEndElement();
            }

            foreach (MaterialAsset mat in Materials)
            {
                writer.WriteStartElement("material");
                writer.WriteAttributeString("name", mat.Name);
                if (mat.Rgba != null && mat.Rgba.Length == 4)
                {
                    writer.WriteAttributeString(
                        "rgba",
                        MJCFFormat.FormatDouble(mat.Rgba[0]) + " " +
                        MJCFFormat.FormatDouble(mat.Rgba[1]) + " " +
                        MJCFFormat.FormatDouble(mat.Rgba[2]) + " " +
                        MJCFFormat.FormatDouble(mat.Rgba[3]));
                }
                if (!string.IsNullOrEmpty(mat.Texture))
                {
                    writer.WriteAttributeString("texture", mat.Texture);
                }
                writer.WriteEndElement();
            }

            foreach (MeshAsset mesh in Meshes)
            {
                writer.WriteStartElement("mesh");
                writer.WriteAttributeString("name", mesh.Name);
                writer.WriteAttributeString("file", mesh.File);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }
    }
}
