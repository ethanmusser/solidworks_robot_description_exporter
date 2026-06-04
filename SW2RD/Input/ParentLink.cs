using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <parent link=...> element of a joint.
    public class ParentLink
    {
        public string Name;

        public ParentLink()
        {
            Name = "";
        }

        public ParentLink Clone()
        {
            return new ParentLink { Name = Name };
        }

        public void FillBoxes(Label box)
        {
            box.Text = Name;
        }

        public void Update(Label box)
        {
            Name = box.Text;
        }
    }
}
