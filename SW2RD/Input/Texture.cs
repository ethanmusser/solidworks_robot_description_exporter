namespace SW2RD.Input
{
    // The <texture> element of <material>. Filename is the in-package path
    // emitted to URDF/MJCF; wFilename is the source file on disk used by the
    // export copy step.
    public class Texture
    {
        public string Filename;

        public string wFilename;

        public Texture()
        {
            wFilename = "";
            Filename = null;
        }

        public Texture Clone()
        {
            return new Texture { Filename = Filename, wFilename = wFilename };
        }
    }
}
