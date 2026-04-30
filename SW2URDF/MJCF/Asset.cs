using System.Collections.Generic;
using System.Xml;

namespace SW2URDF.MJCF
{
    // A reference to a mesh STL file. MuJoCo expects unique names per mesh.
    public class MeshAsset
    {
        public string Name { get; set; }
        public string File { get; set; }

        public MeshAsset(string name, string file)
        {
            Name = name;
            File = file;
        }
    }

    // Emits the <asset> block. The block is omitted if there are no meshes to declare.
    public class Asset
    {
        public List<MeshAsset> Meshes { get; }

        public Asset()
        {
            Meshes = new List<MeshAsset>();
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

        public void WriteMJCF(XmlWriter writer)
        {
            if (Meshes.Count == 0)
            {
                return;
            }
            writer.WriteStartElement("asset");
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
