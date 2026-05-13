/*
Copyright (c) 2015 Stephen Brawner

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.  IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using SW2RD.UI;
using SW2RD.Export;
using SW2RD.Utilities;
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SW2RD.SW
{
    // Adding a new line
    //
    /// <summary>
    /// Summary description for SW2RD (SolidWorks Robot Description Exporter).
    /// </summary>
    [Guid("CEC5AFBA-0180-4958-9C47-15F14E2BE922"), ComVisible(true)]
    [SwAddin(
        Description = "SolidWorks Robot Description Exporter (URDF and MJCF)",
        Title = "SW2RD",
        LoadAtStartup = true
        )]
    public class SwAddin : ISwAddin
    {
        #region Static Variables

        private static readonly log4net.ILog logger = Logger.GetLogger();

        #endregion Static Variables

        #region Local Variables

        private int add_in_id_ = 0;

        public const int mainCmdGroupID = 5;
        public const int mainItemID1 = 0;
        public const int mainItemID2 = 1;
        public const int mainItemID3 = 2;
        public const int flyoutGroupID = 91;

        // Caption used as the CommandGroup title and as the lookup key
        // for the SOLIDWORKS Add-Ins tab insertion. SW shows this string
        // in the Customize > Toolbars dialog so the user can disable
        // the toolbar without uninstalling the add-in.
        private const string CmdGroupTitle = "Robot Description Exporter";

        #region Event Handler Variables

        private SldWorks SwEventPtr = null;

        #endregion Event Handler Variables

        // Public Properties
        public ISldWorks SwApp { get; private set; } = null;

        public ICommandManager CmdMgr { get; private set; } = null;

        public Hashtable OpenDocs { get; private set; } = new Hashtable();

        #endregion Local Variables

        #region SolidWorks Registration

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            #region Get Custom Attribute: SwAddinAttribute

            SwAddinAttribute SWattr = null;
            Type type = typeof(SwAddin);

            foreach (System.Attribute attr in type.GetCustomAttributes(false))
            {
                if (attr is SwAddinAttribute)
                {
                    SWattr = attr as SwAddinAttribute;
                    break;
                }
            }

            #endregion Get Custom Attribute: SwAddinAttribute

            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                logger.Info("Registering " + keyname);
                Microsoft.Win32.RegistryKey addinkey = hklm.CreateSubKey(keyname);
                addinkey.SetValue(null, 0);

                addinkey.SetValue("Description", SWattr.Description);
                addinkey.SetValue("Title", SWattr.Title);

                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                logger.Info("Registering " + keyname);
                addinkey = hkcu.CreateSubKey(keyname);
                addinkey.SetValue(
                    null, Convert.ToInt32(SWattr.LoadAtStartup), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (NullReferenceException nl)
            {
                logger.Error("There was a problem registering this dll: SWattr is null. \n\"" +
                    nl.Message + "\"", nl);
                // MessageBox.Show("There was a problem registering this dll: SWattr is null. \n\"" +
                //     nl.Message + "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
            catch (Exception e)
            {
                logger.Error(e.Message);
                // MessageBox.Show("There was a problem registering the function: \n\"" + e.Message +
                //    "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;

                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                logger.Info("Unregistering " + keyname);
                hklm.DeleteSubKey(keyname);

                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                logger.Info("Unregistering " + keyname);
                hkcu.DeleteSubKey(keyname);
            }
            catch (NullReferenceException nl)
            {
                logger.Error("There was a problem unregistering this dll: " + nl.Message);
                MessageBox.Show("There was a problem unregistering this dll: \n\"" +
                    nl.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
            catch (Exception e)
            {
                logger.Error("There was a problem unregistering this dll: " + e.Message);
                MessageBox.Show("There was a problem unregistering this dll: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        #endregion SolidWorks Registration

        #region ISwAddin Implementation

        public SwAddin()
        {
            Logger.Setup();
        }

        private void ExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            logger.Warn("Exception encountered in Assembly export form", e.Exception);
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger.Error("Unhandled exception in Assembly Export form\nEmail your maintainer " +
                "with the log file found at " +
                Logger.GetFileName(), (Exception)e.ExceptionObject);
        }

        public bool ConnectToSW(object ThisSW, int cookie)
        {
            logger.Info("Attempting to connect to SW");
            SwApp = (ISldWorks)ThisSW;
            add_in_id_ = cookie;

            //Setup callbacks
            logger.Info("Setting up callbacks");
            SwApp.SetAddinCallbackInfo(0, this, add_in_id_);

            #region Setup the Command Manager
            logger.Info("Setting up command manager");
            CmdMgr = SwApp.GetCommandManager(cookie);

            logger.Info("Adding command manager");
            AddCommandMgr();

            #endregion Setup the Command Manager

            #region Setup the Event Handlers
            logger.Info("Adding event handlers");
            SwEventPtr = (SldWorks)SwApp;
            OpenDocs = new Hashtable();
            AttachEventHandlers();

            #endregion Setup the Event Handlers

            logger.Info("Connecting plugin to SolidWorks");
            return true;
        }

        public bool DisconnectFromSW()
        {
            RemoveCommandMgr();
            DetachEventHandlers();

            Marshal.ReleaseComObject(CmdMgr);
            CmdMgr = null;
            Marshal.ReleaseComObject(SwApp);
            SwApp = null;
            //The addin _must_ call GC.Collect() here in order to retrieve all managed code pointers
            GC.Collect();
            GC.WaitForPendingFinalizers();

            GC.Collect();
            GC.WaitForPendingFinalizers();

            logger.Info("Disconnecting plugin from SolidWorks");
            return true;
        }

        #endregion ISwAddin Implementation

        #region UI Methods

        public void AddCommandMgr()
        {
            // Icon paths must be resolvable at SOLIDWORKS load time. Resolve
            // them relative to the loaded assembly and fall back through the
            // known install image folders so missing icons do not prevent menu
            // or toolbar registration.
            string[] images = BuildIconList();
            int ret = SwApp.AddMenuItem5((int)swDocumentTypes_e.swDocASSEMBLY, add_in_id_, "Export Robot Description@&Tools",
                -1, "AssemblyRobotDescriptionExporter", "", "Export assembly as a robot description (URDF or MJCF)", images);
            if (ret < 0)
            {
                logger.Error("Failure to add menu item 'Export Robot Description' to menu 'Tools'");
            }
            else
            {
                logger.Info("Adding Assembly export to Tools menu");
            }

            // Also publish the export action as a CommandManager toolbar
            // entry. If toolbar registration fails, the Tools menu entry still
            // gives users a stable way to launch the exporter.
            try
            {
                AddCommandManagerToolbar(images);
            }
            catch (Exception ex)
            {
                logger.Warn("AddCommandManagerToolbar failed (toolbar entry skipped, menu still works): " + ex.Message, ex);
            }

            // The exporter operates on assemblies. To export a single part,
            // open it from an assembly and configure it as one link.
        }

        public int ToolbarEnableMethod()
        {
            return 1;
        }
        public void RemoveCommandMgr()
        {
            // Tear down the dedicated Robot Description Exporter ribbon tab for
            // every doc type BEFORE RemoveCommandGroup2 drops the
            // commands the tab references. We own the tab outright, so
            // RemoveCommandTab here leaves no other add-in's boxes
            // behind. Belt-and-suspenders with the connect-time
            // RemoveCommandTab in AttachCommandToOwnTab: doing it here
            // too means a clean unload (no rebuild) also leaves the
            // ribbon free of stale tabs, which matters if the user
            // disables the add-in without restarting SW.
            int[] docTypes = new[]
            {
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocDRAWING,
            };
            foreach (int docType in docTypes)
            {
                try
                {
                    // RemoveCommandTab takes the concrete CommandTab
                    // coclass; GetCommandTab returns the same underlying
                    // RCW typed as ICommandTab. Explicit cast threads the
                    // SW interop signature without changing semantics.
                    CommandTab existing = (CommandTab)CmdMgr?.GetCommandTab(docType, CmdGroupTitle);
                    if (existing != null)
                    {
                        bool removed = CmdMgr.RemoveCommandTab(existing);
                        if (!removed)
                        {
                            logger.Warn("RemoveCommandTab returned false on disconnect for tab '" +
                                CmdGroupTitle + "', docType=" + docType);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("RemoveCommandTab (disconnect) failed for docType=" + docType +
                        ": " + ex.Message, ex);
                }
            }

            // RemoveCommandGroup2 is the inverse of CreateCommandGroup2;
            // it clears the underlying commands that the now-removed
            // ribbon tabs referenced. Order matters: tabs first
            // (above), then the group, so SW never sees a ribbon
            // referencing a freed command ID.
            try
            {
                CmdMgr?.RemoveCommandGroup2(mainCmdGroupID, true);
            }
            catch (Exception ex)
            {
                logger.Warn("RemoveCommandGroup2 failed: " + ex.Message, ex);
            }

            SwApp.RemoveMenu((int)swDocumentTypes_e.swDocASSEMBLY, "Export Robot Description@&Tools", "");
            logger.Info("Removing assembly export from Tools menu");
        }

        // Builds the list of icon PNG paths handed to AddMenuItem5 and
        // CommandGroup. Resolves the icons relative to the loaded DLL so
        // the same code path works for both the installer build (icons
        // sit alongside the DLL in the install dir) and the dev build
        // (icons sit alongside the DLL in bin\x64\<Configuration>\images
        // courtesy of the <Content CopyToOutputDirectory> entries in
        // SW2RD.csproj).
        //
        // SW expects the strings to point at PNGs at six standard sizes;
        // any missing file is dropped from the list and SW falls back
        // to the next-larger size, but a totally empty list causes the
        // CommandGroup to render with the SW default (an empty bitmap),
        // which is hard to spot on the toolbar.
        private string[] BuildIconList()
        {
            string[] candidates = ResolveIconDirectories();
            string[] sizes = new[] { "20x20", "32x32", "40x40", "64x64", "96x96", "128x128" };
            string[] result = new string[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                string fileName = "ros_logo_" + sizes[i] + ".png";
                string match = null;
                foreach (string dir in candidates)
                {
                    if (string.IsNullOrEmpty(dir))
                    {
                        continue;
                    }
                    string candidate = Path.Combine(dir, fileName);
                    if (File.Exists(candidate))
                    {
                        match = candidate;
                        break;
                    }
                }
                // Fall back to the legacy install path even if it does
                // not exist on disk - SW's AddMenuItem5 silently drops
                // missing entries, so the worst case is a placeholder
                // icon rather than a broken menu.
                result[i] = match ?? Path.Combine(
                    "C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS\\RobotDescriptionExporter\\images",
                    fileName);
            }
            return result;
        }

        // Search order for icon files: DLL dir\images, DLL dir, DLL
        // parent dir\images, and (last resort) the legacy install
        // directory. The "DLL parent dir\images" entry covers builds
        // where SW2RD.csproj copies images alongside the DLL but
        // the build system also produces a sibling "images" folder.
        private string[] ResolveIconDirectories()
        {
            string dllDir = null;
            try
            {
                dllDir = Path.GetDirectoryName(typeof(SwAddin).Assembly.Location);
            }
            catch (Exception ex)
            {
                logger.Warn("Could not resolve DLL location for icon search: " + ex.Message);
            }
            string[] dirs = new string[5];
            dirs[0] = string.IsNullOrEmpty(dllDir) ? null : Path.Combine(dllDir, "images");
            dirs[1] = dllDir;
            dirs[2] = string.IsNullOrEmpty(dllDir) ? null : Path.Combine(Path.GetDirectoryName(dllDir) ?? "", "images");
            dirs[3] = "C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS\\RobotDescriptionExporter\\images";
            dirs[4] = "C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS (2)\\RobotDescriptionExporter\\images";
            return dirs;
        }

        // Creates the CommandManager toolbar / ribbon entry. We put a
        // single command ("Export Robot Description") under a CommandGroup
        // titled CmdGroupTitle and attach a CommandTabBox to a ribbon
        // tab of the same title so the icon shows up for assembly /
        // part / drawing documents.
        //
        // The whole sequence (CreateCommandGroup2 -> AddCommandItem2 ->
        // Activate -> per-doc-type CommandTab wiring) is the canonical
        // SW add-in ribbon recipe. We pass `ignorePrevious = true` and own
        // a dedicated ribbon tab so reconnecting the add-in rebuilds cached
        // toolbar state instead of accumulating duplicate icons on a shared
        // tab. HasMenu stays false because AddMenuItem5 already publishes the
        // Tools menu entry.
        private void AddCommandManagerToolbar(string[] iconList)
        {
            if (CmdMgr == null)
            {
                logger.Warn("AddCommandManagerToolbar: CmdMgr is null; skipping toolbar setup");
                return;
            }

            int errors = 0;
            ICommandGroup cmdGroup = CmdMgr.CreateCommandGroup2(
                mainCmdGroupID,
                CmdGroupTitle,
                "Export the active assembly as URDF or MJCF",
                "Export the active assembly as URDF or MJCF",
                -1,
                true,  // ignorePrevious - always rebuild
                ref errors);

            if (cmdGroup == null)
            {
                logger.Warn("CreateCommandGroup2 returned null (errors=" + errors + "); toolbar entry skipped");
                return;
            }

            // SW 2017+ accepts a single multi-resolution PNG list for
            // both IconList (per-button icons) and MainIconList
            // (CommandGroup header icons). Reusing the same list keeps
            // the toolbar visually consistent at every DPI / icon-size
            // setting in SW.
            cmdGroup.IconList = iconList;
            cmdGroup.MainIconList = iconList;

            // SW exposes the placement bits as swMenuItem / swToolbarItem.
            // We only want the toolbar entry here - the menu entry is
            // already published via AddMenuItem5 above; setting both
            // would duplicate "Export Robot Description" under Tools.
            int menuToolbarOption = (int)swCommandItemType_e.swToolbarItem;
            int cmdIndex = cmdGroup.AddCommandItem2(
                "Export Robot Description",
                -1,
                "Export the active assembly as a robot description (URDF or MJCF)",
                "Robot Description",
                0,                              // image list index
                "AssemblyRobotDescriptionExporter", // callback function
                "ToolbarEnableMethod",          // enable method
                mainItemID1,
                menuToolbarOption);

            if (cmdIndex < 0)
            {
                logger.Warn("AddCommandItem2 returned " + cmdIndex + "; toolbar item skipped");
                return;
            }

            cmdGroup.HasToolbar = true;
            cmdGroup.HasMenu = false; // menu entry is published via AddMenuItem5
            cmdGroup.Activate();

            int commandID = cmdGroup.get_CommandID(cmdIndex);

            // Wire the new command into the dedicated Robot Description
            // Exporter ribbon tab for every document type the add-in is
            // active in. We own this tab outright (no other add-in
            // touches it) so we can drop any stale tab from a prior
            // registration cleanly via RemoveCommandTab before
            // recreating it - that's what prevents the duplicate-icon
            // accumulation across rebuilds.
            int[] docTypes = new[]
            {
                (int)swDocumentTypes_e.swDocASSEMBLY,
                (int)swDocumentTypes_e.swDocPART,
                (int)swDocumentTypes_e.swDocDRAWING,
            };
            foreach (int docType in docTypes)
            {
                try
                {
                    AttachCommandToOwnTab(docType, commandID);
                }
                catch (Exception ex)
                {
                    logger.Warn("AttachCommandToOwnTab(docType=" + docType + ") failed: " + ex.Message, ex);
                }
            }

            logger.Info("Added Robot Description Exporter CommandGroup to CommandManager toolbar");
        }

        // Attaches `commandID` to the dedicated Robot Description Exporter
        // ribbon tab for `docType`. The tab name is `CmdGroupTitle`,
        // which we own; recreating it on connect keeps SolidWorks's cached
        // ribbon layout from accumulating duplicate CommandTabBox entries.
        private void AttachCommandToOwnTab(int docType, int commandID)
        {
            string tabName = CmdGroupTitle;

            // Drop any cached tab before recreating it. SW serializes
            // ribbon layout to the per-user registry and re-applies it on
            // launch, so the tab can be present even though our CmdMgr was
            // just connected.
            // RemoveCommandTab on a non-existent tab is a no-op
            // (GetCommandTab returns null), so the guard is cheap. The
            // explicit (CommandTab) cast satisfies the SW interop
            // signature, which takes the concrete coclass rather than
            // the ICommandTab interface returned by GetCommandTab.
            CommandTab existing = (CommandTab)CmdMgr.GetCommandTab(docType, tabName);
            if (existing != null)
            {
                bool removed = CmdMgr.RemoveCommandTab(existing);
                if (!removed)
                {
                    logger.Warn("RemoveCommandTab returned false for tab '" + tabName +
                        "', docType=" + docType + "; existing tab may carry stale boxes");
                }
            }

            ICommandTab cmdTab = CmdMgr.AddCommandTab(docType, tabName);
            if (cmdTab == null)
            {
                logger.Warn("AddCommandTab returned null for tab '" + tabName +
                    "', docType=" + docType + "; toolbar item not pinned to ribbon");
                return;
            }

            ICommandTabBox cmdBox = cmdTab.AddCommandTabBox();
            if (cmdBox == null)
            {
                logger.Warn("AddCommandTabBox returned null for tab '" + tabName +
                    "', docType=" + docType + "; toolbar item not pinned to ribbon");
                return;
            }

            int[] cmdIDs = new[] { commandID };
            int[] textTypes = new[]
            {
                (int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow,
            };
            bool ok = cmdBox.AddCommands(cmdIDs, textTypes);
            if (!ok)
            {
                logger.Warn("ICommandTabBox.AddCommands returned false for tab '" + tabName +
                    "', docType=" + docType);
            }
        }

        #endregion UI Methods

        #region UI Callbacks

        public void SetupAssemblyExporter()
        {
            ModelDoc2 modeldoc = SwApp.ActiveDoc;
            logger.Info("Assembly export called for file " + modeldoc.GetTitle());
            bool saveAndRebuild = false;
            if (modeldoc.GetSaveFlag())
            {
                saveAndRebuild = true;
                logger.Info("Save is required");
            }
            else if (modeldoc.Extension.NeedsRebuild2 !=
                (int)swModelRebuildStatus_e.swModelRebuildStatus_FullyRebuilt)
            {
                saveAndRebuild = true;
                logger.Info("A rebuild is required");
            }
            if (saveAndRebuild ||
                MessageBox.Show("The Robot Description Exporter requires saving and/or rebuilding before continuing",
                "Save and rebuild document?", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                int options = (int)swSaveAsOptions_e.swSaveAsOptions_SaveReferenced |
                        (int)swSaveAsOptions_e.swSaveAsOptions_Silent;
                logger.Info("Saving assembly");
                modeldoc.Save3(options, 0, 0);

                logger.Info("Opening property manager");
                SetupPropertyManager();
            }
        }

        public void AssemblyRobotDescriptionExporter()
        {
            try
            {
                SetupAssemblyExporter();
            }
            catch (Exception e)
            {
                logger.Error("An exception was caught when trying to setup the assembly exporter", e);
                MessageBox.Show("There was a problem setting up the property manager: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        public void SetupPropertyManager()
        {
            ExportPropertyManager pm = new ExportPropertyManager((SldWorks)SwApp);
            logger.Info("Loading config tree");
            bool success = pm.LoadConfigTree();

            if (success)
            {
                logger.Info("Showing property manager");
                pm.Show();
            }
        }

        public void FlyoutCallback()
        {
            FlyoutGroup flyGroup = CmdMgr.GetFlyoutGroup(flyoutGroupID);
            flyGroup.RemoveAllCommandItems();

            flyGroup.AddCommandItem(
                DateTime.Now.ToLongTimeString(), "test", 0, "FlyoutCommandItem1", "FlyoutEnableCommandItem1");
        }

        public int FlyoutEnable()
        {
            return 1;
        }

        public void FlyoutCommandItem1()
        {
            SwApp.SendMsgToUser("Flyout command 1");
        }

        public int FlyoutEnableCommandItem1()
        {
            return 1;
        }

        #endregion UI Callbacks

        #region Event Methods

        public bool AttachEventHandlers()
        {
            AttachSwEvents();
            //Listen for events on all currently open docs
            AttachEventsToAllDocuments();
            return true;
        }

        private bool AttachSwEvents()
        {
            try
            {
                SwEventPtr.ActiveDocChangeNotify +=
                    new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
                SwEventPtr.DocumentLoadNotify2 +=
                    new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
                SwEventPtr.FileNewNotify2 +=
                    new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
                SwEventPtr.ActiveModelDocChangeNotify +=
                    new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
                SwEventPtr.FileOpenPostNotify +=
                    new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
                return true;
            }
            catch (Exception e)
            {
                logger.Error("Attaching SW events failed", e);
                return false;
            }
        }

        private bool DetachSwEvents()
        {
            try
            {
                SwEventPtr.ActiveDocChangeNotify -=
                    new DSldWorksEvents_ActiveDocChangeNotifyEventHandler(OnDocChange);
                SwEventPtr.DocumentLoadNotify2 -=
                    new DSldWorksEvents_DocumentLoadNotify2EventHandler(OnDocLoad);
                SwEventPtr.FileNewNotify2 -=
                    new DSldWorksEvents_FileNewNotify2EventHandler(OnFileNew);
                SwEventPtr.ActiveModelDocChangeNotify -=
                    new DSldWorksEvents_ActiveModelDocChangeNotifyEventHandler(OnModelChange);
                SwEventPtr.FileOpenPostNotify -=
                    new DSldWorksEvents_FileOpenPostNotifyEventHandler(FileOpenPostNotify);
                return true;
            }
            catch (Exception e)
            {
                logger.Error("Attaching SW events failed", e);
                return false;
            }
        }

        public void AttachEventsToAllDocuments()
        {
            ModelDoc2 modDoc = (ModelDoc2)SwApp.GetFirstDocument();
            while (modDoc != null)
            {
                if (!OpenDocs.Contains(modDoc))
                {
                    AttachModelDocEventHandler(modDoc);
                }
                else if (OpenDocs.Contains(modDoc))
                {
                    DocumentEventHandler docHandler = (DocumentEventHandler)OpenDocs[modDoc];
                    if (docHandler != null)
                    {
                        bool connected = docHandler.ConnectModelViews();
                        if (!connected)
                        {
                            logger.Warn("Failed to connect to model views");
                        }
                    }
                }

                modDoc = (ModelDoc2)modDoc.GetNext();
            }
        }

        public bool AttachModelDocEventHandler(ModelDoc2 modDoc)
        {
            if (modDoc == null)
            {
                return false;
            }

            if (!OpenDocs.Contains(modDoc))
            {
                DocumentEventHandler docHandler;
                switch (modDoc.GetType())
                {
                    case (int)swDocumentTypes_e.swDocPART:
                        {
                            docHandler = new PartEventHandler(modDoc, this);
                            break;
                        }
                    case (int)swDocumentTypes_e.swDocASSEMBLY:
                        {
                            docHandler = new AssemblyEventHandler(modDoc, this);
                            break;
                        }
                    case (int)swDocumentTypes_e.swDocDRAWING:
                        {
                            docHandler = new DrawingEventHandler(modDoc, this);
                            break;
                        }
                    default:
                        {
                            return false; //Unsupported document type
                        }
                }
                docHandler.AttachEventHandlers();
                OpenDocs.Add(modDoc, docHandler);
            }
            return true;
        }

        public bool DetachModelEventHandler(ModelDoc2 modDoc)
        {
            OpenDocs.Remove(modDoc);
            return true;
        }

        public bool DetachEventHandlers()
        {
            DetachSwEvents();

            //Close events on all currently open docs
            DocumentEventHandler docHandler;
            int numKeys = OpenDocs.Count;
            object[] keys = new Object[numKeys];

            //Remove all document event handlers
            OpenDocs.Keys.CopyTo(keys, 0);
            foreach (ModelDoc2 key in keys)
            {
                docHandler = (DocumentEventHandler)OpenDocs[key];
                docHandler.DetachEventHandlers(); //This also removes the pair from the hash
                docHandler = null;
            }
            return true;
        }

        #endregion Event Methods

        #region Event Handlers

        //Events
        public int OnDocChange()
        {
            return 0;
        }

        public int OnDocLoad(string docTitle, string docPath)
        {
            return 0;
        }

        private int FileOpenPostNotify(string FileName)
        {
            AttachEventsToAllDocuments();
            return 0;
        }

        public int OnFileNew(object newDoc, int docType, string templateName)
        {
            AttachEventsToAllDocuments();
            return 0;
        }

        public int OnModelChange()
        {
            return 0;
        }

        #endregion Event Handlers
    }
}