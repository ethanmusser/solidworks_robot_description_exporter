namespace SW2RD.Input
{
    // The <collision> "template" element of a link; per-group mesh filenames
    // live in Link.CollisionGroups.
    public class Collision
    {
        public Origin Origin;

        public Geometry Geometry;

        public Collision()
        {
            Origin = new Origin(false);
            Geometry = new Geometry();
        }

        public Collision Clone()
        {
            return new Collision
            {
                Origin = Origin.Clone(),
                Geometry = Geometry.Clone(),
            };
        }
    }
}
