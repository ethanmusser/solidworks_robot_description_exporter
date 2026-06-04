using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <material> element of <visual>.
    public class Material
    {
        public Color Color;

        public Texture Texture;

        public string Name;

        public Material()
        {
            Color = new Color();
            Texture = new Texture();
            Name = "";
        }

        public Material Clone()
        {
            return new Material
            {
                Color = Color.Clone(),
                Texture = Texture.Clone(),
                Name = Name,
            };
        }

        public void FillBoxes(ComboBox box)
        {
            box.Text = Name;
        }
    }
}
