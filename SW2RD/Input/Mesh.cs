namespace SW2RD.Input
{
    // The <mesh> element (filename only) inside <geometry>.
    public class Mesh
    {
        public string Filename;

        public Mesh Clone()
        {
            return new Mesh { Filename = Filename };
        }
    }
}
