namespace SW2RD.Input
{
    // The <inertial> element of a link.
    public class Inertial
    {
        public Origin Origin;

        public Mass Mass;

        public Inertia Inertia;

        public Inertial()
        {
            Origin = new Origin(false);
            Mass = new Mass();
            Inertia = new Inertia();
        }

        public Inertial Clone()
        {
            return new Inertial
            {
                Origin = Origin.Clone(),
                Mass = Mass.Clone(),
                Inertia = Inertia.Clone(),
            };
        }
    }
}
