using SolidWorks.Interop.sldworks;
using SW2RD.Input;
using SW2RD.UI;
using SW2RD.URDF;
using System;

namespace SW2RD.Test
{
    /// <summary>
    /// TestFixture which gets passed to each Test Class. For now it just provides 
    /// the reference to the SolidWorks app.
    /// </summary>
    public class SWTestFixture : IDisposable
    {
        public static bool Initialized = false;
        public static SldWorks SwApp;

        public static void Initialize()
        {
            if (!Initialized)
            {
                SwApp = (SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
                SwApp.Visible = true;

                // Run the export/config code paths headless. Under the unattended
                // TestRunner a modal MessageBox has no one to dismiss it and
                // deadlocks the test thread while SOLIDWORKS keeps pumping
                // messages - the "Responding = True but hung" failure mode. These
                // three lines collapse every interactive prompt to a deterministic,
                // non-blocking default:
                //  - UserNotifier gates our own export/config WinForms popups.
                //  - SilentMessageBox no-ops URDFPackage's "Creating URDF Package"
                //    box that fires on every URDF export.
                //  - UserControl=false stops SOLIDWORKS itself from raising modal
                //    prompts (rebuild/save/etc.) during automated operations.
                UserNotifier.SuppressInteractivePrompts = true;
                URDFPackage.MessageBox = new SilentMessageBox();
                SwApp.UserControl = false;

                Initialized = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {

        }
    }
}
