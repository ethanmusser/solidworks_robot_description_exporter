namespace SW2RD.Export
{
    // Output format for the assembly export pipeline. URDF retains the existing
    // ROS-package layout with launch/config files; MJCF emits a smaller folder
    // with just the .xml model and the meshes directory.
    public enum ExportFormat
    {
        URDF,
        MJCF,
    }
}
