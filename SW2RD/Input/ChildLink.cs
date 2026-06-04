using System.Windows.Forms;

namespace SW2RD.Input
{
    // The <child link=...> element of a joint.
    public class ChildLink
    {
        public string Name;

        public ChildLink()
        {
            Name = "";
        }

        public ChildLink Clone()
        {
            return new ChildLink { Name = Name };
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
