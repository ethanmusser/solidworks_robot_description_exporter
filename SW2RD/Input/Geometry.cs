namespace SW2RD.Input
{
    // The <geometry> element of <visual> / <collision>.
    public class Geometry
    {
        public Mesh Mesh;

        public Geometry()
        {
            Mesh = new Mesh();
        }

        public Geometry Clone()
        {
            return new Geometry { Mesh = Mesh.Clone() };
        }
    }
}
