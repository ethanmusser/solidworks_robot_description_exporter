namespace SW2RD.Input
{
    // The root of the SolidWorks input/edit model: a single robot with a base
    // link. Serialized to / from Config JSON via the KinematicTree adapter;
    // URDF/MJCF output is produced by the records-native writers.
    public class Robot
    {
        public Link BaseLink { get; private set; }

        public string Name { get; set; }

        public Robot()
        {
            BaseLink = new Link(null);
            Name = "";
        }

        public void SetBaseLink(Link link)
        {
            BaseLink = link;
        }

        internal string[] GetJointNames(bool includeFixed)
        {
            return BaseLink.GetJointNames(includeFixed);
        }
    }
}
