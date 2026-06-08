using System.Windows;

namespace SW2RD.UI
{
    /// <summary>
    /// No-op <see cref="IMessageBox"/> used to run the URDF package creation path
    /// headless. <see cref="SW2RD.URDF.URDFPackage.CreateDirectories"/> shows an
    /// informational "Creating URDF Package ..." box on every URDF export; under
    /// the unattended SW-attached test suite that modal box deadlocks the test
    /// thread. The fixture injects this implementation so the export proceeds
    /// without a dialog. Returns <see cref="MessageBoxResult.OK"/> so any future
    /// caller that inspects the result takes the affirmative branch.
    /// </summary>
    public class SilentMessageBox : IMessageBox
    {
        public MessageBoxResult Show(string message)
        {
            return MessageBoxResult.OK;
        }

        public MessageBoxResult Show(string message, string caption, MessageBoxButton buttons)
        {
            return MessageBoxResult.OK;
        }
    }
}
