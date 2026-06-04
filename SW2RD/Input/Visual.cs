namespace SW2RD.Input
{
    // The <visual> "template" element of a link, carrying the shared origin /
    // material; per-group mesh filenames live in Link.VisualGroups.
    public class Visual
    {
        public Origin Origin;

        public Geometry Geometry;

        public Material Material;

        public Visual()
        {
            Origin = new Origin(false);
            Geometry = new Geometry();
            Material = new Material();
        }

        public Visual Clone()
        {
            return new Visual
            {
                Origin = Origin.Clone(),
                Geometry = Geometry.Clone(),
                Material = Material.Clone(),
            };
        }
    }
}
