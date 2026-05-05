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
using SW2URDF.UI;
using SW2URDF.URDF;
using SW2URDF.URDFExport.CSV;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    [ComVisible(true)]
    [Serializable]
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();
        public SldWorks swApp;
        public ModelDoc2 ActiveSWModel;

        // The non-serializable runtime references below are flagged with
        // [NonSerialized] so the BinaryFormatter doesn't try to walk them if
        // anything ever attempts to serialize this PMPage handler. We don't
        // actually persist the handler — config persistence happens via the
        // DataContract Link path — but the class carries [Serializable] for
        // historical COM-hosting reasons and the analyzer (CA2235) requires
        // the explicit opt-out on each non-serializable field.
        [NonSerialized]
        public ExportHelper Exporter;
        [NonSerialized]
        public LinkNode previouslySelectedNode;
        [NonSerialized]
        public Link previouslySelectedLink;
        public List<Link> linksToVisit;
        [NonSerialized]
        public LinkNode rightClickedNode;
        private readonly ContextMenuStrip docMenu;
        private bool disposed;

        //General objects required for the PropertyManager page

        private readonly PropertyManagerPage2 PMPage;
        // PMSetupGroup is the top-of-page anchor: it hosts the global controls
        // (Preview/Export, Load Configuration, Imported File label, the four
        // Compute checkboxes, and the Link Tree). Wrapping these in a group
        // box declared first is the supported way to keep them above the
        // per-link sub-sections; loose top-level controls aren't reliably
        // ordered above subsequent group boxes.
        // PMGroup hosts the link/joint property inputs (link name, joint name,
        // coord systems, axis, joint type, child count). PMComponentsGroup hosts
        // the visual / collision / inertial component selection blocks - they
        // are split into separate sub-sections so the side-bar reads top-down
        // as: Setup -> Link & Joint Properties -> Components -> Sites.
        private PropertyManagerPageGroup PMSetupGroup;
        private PropertyManagerPageGroup PMGroup;
        private PropertyManagerPageGroup PMComponentsGroup;
        // The Visual / Collision selection boxes hold the components for the
        // CURRENTLY ACTIVE group of that role on the active link. When the user
        // switches groups (via the listbox) or links (via the tree), we save
        // the SelectionBox contents back into the previously-active group and
        // re-load the new group's contents.
        private PropertyManagerPageSelectionbox PMSelectionVisual;
        private PropertyManagerPageSelectionbox PMSelectionCollision;
        private PropertyManagerPageSelectionbox PMSelectionInertial;
        private PropertyManagerPageCombobox PMComboBoxInertialSource;
        private PropertyManagerPageButton PMButtonExport;
        private PropertyManagerPageButton PMButtonLoad;
        private PropertyManagerPageTextbox PMTextBoxLinkName;
        private PropertyManagerPageTextbox PMTextBoxJointName;
        private PropertyManagerPageNumberbox PMNumberBoxChildCount;
        private PropertyManagerPageCombobox PMComboBoxGlobalCoordsys;
        private PropertyManagerPageCombobox PMComboBoxAxes;
        private PropertyManagerPageCombobox PMComboBoxCoordSys;
        private PropertyManagerPageCombobox PMComboBoxJointType;
        private PropertyManagerPageCheckbox PMComputeMassInertia;
        private PropertyManagerPageCheckbox PMComputeVisualCollision;
        private PropertyManagerPageCheckbox PMComputeJointKinematics;
        private PropertyManagerPageCheckbox PMComputeJointLimits;

        // Visual Groups sub-section: lets the user define multiple named groups
        // of components, each producing its own STL / mesh asset / geom on
        // export. The listbox shows the existing groups; selecting a row
        // populates the SelectionBox above with that group's components for
        // editing. Add Group / Remove Selected Group manage the list.
        private PropertyManagerPageListbox PMListBoxVisualGroups;
        private PropertyManagerPageTextbox PMTextBoxVisualGroupName;
        private PropertyManagerPageButton PMButtonVisualGroupAdd;
        private PropertyManagerPageButton PMButtonVisualGroupRemove;

        // Collision Groups sub-section, mirrors Visual Groups. The label fields
        // are captured so SetCollisionEditorVisible can hide / show the whole
        // collision editor when the user toggles "Use visual groups as
        // collision".
        private PropertyManagerPageListbox PMListBoxCollisionGroups;
        private PropertyManagerPageTextbox PMTextBoxCollisionGroupName;
        private PropertyManagerPageButton PMButtonCollisionGroupAdd;
        private PropertyManagerPageButton PMButtonCollisionGroupRemove;
        private PropertyManagerPageLabel PMLabelCollisionGroupsHelp;
        private PropertyManagerPageLabel PMLabelCollisionGroupsName;

        // "Use visual groups as collision" toggle. When checked, the collision
        // editor below it is hidden and the export pipeline reuses the visual
        // meshes for collision.
        private PropertyManagerPageCheckbox PMCheckCollisionUsesVisual;

        // "Reverse Direction" toggle for the joint axis. swControlType_BitmapButton
        // mimicking the same flip button SW uses on its own coord-system /
        // extrude / hole-wizard PMs (uses the standard
        // swBitmapButtonImage_reverse_direction icon). The button is a CLICK
        // event, not a check event, so we maintain the toggle state ourselves
        // in currentAxisFlipped. The overlay arrow drawn via
        // ExportHelper.DrawAxisOverlay is the user's visual feedback for
        // whether flip is on - the button itself has no "pressed" visual state.
        private PropertyManagerPageBitmapButton PMBitmapAxisFlip;
        private bool currentAxisFlipped;

        // Index of the visual / collision group whose components are currently
        // shown in the corresponding SelectionBox. Reset to 0 on every link
        // switch and adjusted as the user picks rows in the listbox.
        private int activeVisualGroupIndex = 0;
        private int activeCollisionGroupIndex = 0;

        // Guard against re-entrancy: when LoadActiveVisualGroupIntoSelectionBox /
        // LoadActiveCollisionGroupIntoSelectionBox programmatically populate a
        // SelectionBox via CommonSwOperations.SelectComponents, every added item
        // fires OnSelectionboxListChanged. That handler would otherwise commit a
        // partial selection back to the active group and bounce the listbox count
        // while the load is in progress. The flag is set true around those
        // programmatic loads. PropertyManager events are delivered on the
        // SolidWorks UI thread, so a plain bool is safe here.
        private bool suppressGroupListboxRefresh;

        // Set true while OnClose is executing. When the property-manager page
        // closes (green check OR PMPage.Close(true) from the Preview-and-Export
        // button), SolidWorks releases the marked selections owned by the
        // SelectionBoxes BEFORE OnClose runs. SaveActiveNode used to read those
        // marks via Commit*GroupSelection / GetSelectedComponents to rebuild
        // the active link's component lists - with the marks gone, that
        // refresh wipes the last-edited link's groups. The flag lets those
        // helpers skip the destructive Clear()+refill while still allowing
        // SaveActiveNode to commit non-SelectionMgr UI state (link name, joint
        // props, CollisionUsesVisual, InertialSource). The visual / collision /
        // inertial component lists are kept current via OnSelectionboxListChanged
        // for every pick, so skipping the close-time refresh is safe.
        private bool pageIsClosing;

        // Sites sub-section: a small inline editor on the per-link page.
        private PropertyManagerPageGroup PMSitesGroup;
        private PropertyManagerPageListbox PMListBoxSites;
        private PropertyManagerPageTextbox PMTextBoxSiteName;
        private PropertyManagerPageCombobox PMComboBoxSiteCoordSys;
        private PropertyManagerPageButton PMButtonSiteAdd;
        private PropertyManagerPageButton PMButtonSiteRemove;

        // Import controls (Load Configuration button + the four post-import
        // recompute checkboxes + the "Imported File:" label) all live as
        // top-level controls on PMPage; there is no longer an "Import" group
        // box. The post-import controls are created hidden (options = 0) and
        // become visible after a successful CSV merge via TreeMergeCompleted ->
        // EnableControl.

        private PropertyManagerPageLabel PMLabelJointName;
        private PropertyManagerPageLabel PMLabelParentLink;
        private PropertyManagerPageLabel PMLabelAxes;
        private PropertyManagerPageLabel PMLabelCoordSys;
        private PropertyManagerPageLabel PMLabelJointType;
        private PropertyManagerPageLabel PMLabelGlobalCoordsys;
        private PropertyManagerPageLabel PMLabelCSVFilename;
        private PropertyManagerPageLabel PMLabelInertialSource;
        private PropertyManagerPageLabel PMLabelVisualComponents;
        private PropertyManagerPageLabel PMLabelCollisionComponents;
        private PropertyManagerPageLabel PMLabelInertialComponents;

        private PropertyManagerPageWindowFromHandle PMTree;

        public TreeView Tree
        { get; set; }

        private bool automaticallySwitched = false;

        //Each object in the page needs a unique ID

        private const int GroupID = 1;
        private const int TextBoxLinkNameID = 2;
        private const int SelectionVisualID = 3;
        private const int SelectionCollisionID = 4;
        private const int SelectionInertialID = 5;
        private const int ComboInertialSourceID = 6;
        private const int NumBoxChildCountID = 7;
        private const int LabelLinkNameID = 8;
        private const int LabelInertialSourceID = 9;
        private const int LabelVisualID = 10;
        private const int LabelCollisionID = 11;
        private const int LabelInertialID = 12;
        private const int LabelJointNameID = 14;
        private const int dotNetTree = 16;
        private const int ButtonExportID = 17;
        private const int ComboBoxCoordSysID = 19;
        private const int LabelAxesID = 20;
        private const int LabelCoordSysID = 21;
        private const int IDGlobalCoordsys = 24;
        private const int IDLabelGlobalCoordsys = 25;
        private const int LoadConfigurationID = 26;
        private const int ComputeMassInertiaID = 27;
        private const int ComputeVisualCollisionID = 28;
        private const int ComputeJointKinematicsID = 29;
        private const int ComputeJointLimitsID = 30;
        private const int LoadedCSVFilenameID = 31;
        private const int SitesGroupID = 40;
        private const int SitesListBoxID = 41;
        private const int SitesNameTextBoxID = 42;
        private const int SitesCoordSysComboID = 43;
        private const int SitesAddButtonID = 44;
        private const int SitesRemoveButtonID = 45;
        private const int SitesHelpLabelID = 46;
        private const int SitesNameLabelID = 47;
        private const int SitesListLabelID = 48;
        private const int SetupGroupID = 49;
        private const int ComponentsGroupID = 50;

        // Visual Groups editor controls.
        private const int VisualGroupsHelpLabelID = 60;
        private const int VisualGroupsListBoxID = 61;
        private const int VisualGroupsNameLabelID = 62;
        private const int VisualGroupsNameTextBoxID = 63;
        private const int VisualGroupsAddButtonID = 64;
        private const int VisualGroupsRemoveButtonID = 65;

        // Collision Groups editor controls.
        private const int CollisionGroupsHelpLabelID = 70;
        private const int CollisionGroupsListBoxID = 71;
        private const int CollisionGroupsNameLabelID = 72;
        private const int CollisionGroupsNameTextBoxID = 73;
        private const int CollisionGroupsAddButtonID = 74;
        private const int CollisionGroupsRemoveButtonID = 75;
        private const int CheckCollisionUsesVisualID = 76;

        // "Reverse Direction" bitmap button next to the Reference Axis combo.
        private const int BitmapAxisFlipID = 80;

        // Marks for the visual/collision/inertial selection boxes so SolidWorks can
        // attribute the user's selection to the right list. -1 (default mark) is reserved
        // by SolidWorks itself; using small distinct positive numbers per box.
        private const int VisualSelectionMark = 11;
        private const int CollisionSelectionMark = 12;
        private const int InertialSelectionMark = 13;

        #endregion class variables

        public void Show()
        {
            PMPage.Show2(0);
        }

        public void Close(bool ok)
        {
            PMPage.Close(ok);
        }

        // Releases the .NET-only Forms objects this handler owns (the
        // TreeView and ContextMenuStrip). SolidWorks itself does not call
        // Dispose; the IPropertyManagerPage2Handler9.OnClose hook invokes
        // this after the page tear-down so the WinForms resources are
        // released along with the SolidWorks-side handles. Idempotent: a
        // second call is a no-op.
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            try
            {
                Tree?.Dispose();
            }
            catch (Exception ex)
            {
                logger.Warn("Exception disposing TreeView in ExportPropertyManager.Dispose", ex);
            }
            try
            {
                docMenu?.Dispose();
            }
            catch (Exception ex)
            {
                logger.Warn("Exception disposing docMenu in ExportPropertyManager.Dispose", ex);
            }
        }

        //The following runs when a new instance of the class is created
        public ExportPropertyManager(SldWorks swAppPtr)
        {
            swApp = swAppPtr;
            ActiveSWModel = swApp.ActiveDoc;
            Exporter = new ExportHelper(swApp);
            Exporter.URDFRobot = new Robot();
            Exporter.URDFRobot.Name = ActiveSWModel.GetTitle();

            linksToVisit = new List<Link>();
            docMenu = new ContextMenuStrip();

            string caption = null;
            string tip = null;
            int longerrors = 0;
            int controlType = 0;
            int alignment = 0;

            ActiveSWModel.ShowConfiguration2("URDF Export");

            #region Create and instantiate components of PM page

            //Set the variables for the page
            string PageTitle = "URDF Exporter";
            long options = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_HandleKeystrokes;

            //Create the PropertyManager page
            PMPage = (PropertyManagerPage2)swApp.CreatePropertyManagerPage(
                PageTitle, (int)options, this, ref longerrors);

            //Make sure that the page was created properly
            if (longerrors == (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
            {
                SetupPropertyManagerPage(ref caption, ref tip, ref options,
                    ref controlType, ref alignment);
            }
            else
            {
                //If the page is not created
                logger.Error("An error occurred while attempting to create the PropertyManager Page\nError: " + longerrors);
                MessageBox.Show("There was a problem setting up the property manager: " +
                    "\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }

            #endregion Create and instantiate components of PM page
        }

        private void ExceptionHandler(object sender, ThreadExceptionEventArgs e)
        {
            logger.Warn("Exception encountered in URDF configuration form\n" +
                "Email your maintainer with the log file found at " + Logger.GetFileName(),
                e.Exception);
        }

        private void UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            logger.Error("Unhandled exception in URDF configuration form\n" +
                "Email your maintainer with the log file found at " + Logger.GetFileName(),
                (Exception)e.ExceptionObject);
        }

        #region Implemented Property Manager Page Handler Methods

        void IPropertyManagerPage2Handler9.AfterActivation()
        {
            //Turns the selection box blue so that selected components are added to the PMPage
            // selection box
            PMSelectionVisual.SetSelectionFocus();
        }

        private void ExportButtonPress()
        {
            SaveActiveNode();

            Exporter.SetComputeInertial(PMComputeMassInertia.Checked);
            Exporter.SetComputeVisualCollision(PMComputeVisualCollision.Checked);
            Exporter.SetComputeJointKinematics(PMComputeJointKinematics.Checked);
            Exporter.SetComputeJointLimits(PMComputeJointLimits.Checked);

            // Only if everything is A-OK, then do we proceed.
            if (CheckIfNamesAreUnique((LinkNode)Tree.Nodes[0]) && CheckNodesComplete(Tree))
            {
                //It saves automatically when sending Okay as true;
                PMPage.Close(true); 
                AssemblyDoc assy = (AssemblyDoc)ActiveSWModel;

                //This call can be a real sink of processing time if the model is large.
                //Unfortunately there isn't a way around it I believe.
                int result = assy.ResolveAllLightWeightComponents(true);

                // If the user confirms to resolve the components and they are successfully
                // resolved we can continue
                if (result == (int)swComponentResolveStatus_e.swResolveOk)
                {
                    List<string> unresolvedComponents = new List<string>();
                    CheckModelDocsExist((LinkNode)Tree.Nodes[0], unresolvedComponents);
                    if (unresolvedComponents.Count > 0)
                    {
                        string componentNames = string.Join("\r\n", unresolvedComponents);
                        logger.Error("SolidWorks told us the resolve succeeded, but ModelDocs" +
                            " could not be obtained for: " + componentNames);
                        MessageBox.Show("Model Documents could not be obtained for the following" +
                            " components. Plesae resolve them:\r\n" + componentNames);
                        return;
                    }

                    // Builds the links and joints from the PMPage configuration
                    LinkNode BaseNode = (LinkNode)Tree.Nodes[0];
                    automaticallySwitched = true;
                    Tree.Nodes.Remove(BaseNode);

                    bool exportSuccess = Exporter.CreateRobotFromTreeView(BaseNode);
                    if (exportSuccess)
                    {
                        AssemblyExportForm exportForm = new AssemblyExportForm(swApp, BaseNode, Exporter);
                        exportForm.Exporter = Exporter;
                        exportForm.Show();
                    }
                }
                else if (result == (int)swComponentResolveStatus_e.swResolveError ||
                    result == (int)swComponentResolveStatus_e.swResolveNotPerformed)
                {
                    logger.Warn("Resolving components failed. Warning user to do so on their own");
                    MessageBox.Show("Resolving components failed. In order to export to URDF, " +
                        " this tool needs all components to be resolved. Try resolving " +
                        "lightweight components manually before attempting to export again");
                }
                else if (result == (int)swComponentResolveStatus_e.swResolveAbortedByUser)
                {
                    logger.Warn("Components were not resolved by user");
                    MessageBox.Show("In order to export to URDF, this tool needs all " +
                        "components to be resolved. You can resolve them manually or try " +
                        "exporting again");
                }
            }
        }

        private void EnableControl(IPropertyManagerPageControl control, bool isEnabled = true)
        {
            control.Enabled = isEnabled;
            control.Visible = true;
        }

        private void TreeMergeCompleted(object sender, TreeMergedEventArgs e)
        {
            if (!e.Success)
            {
                MessageBox.Show("Merging the loaded CSV configuration with the assembly's configuration " +
                    "failed. Check your CSV file. If you continue to run into errors, delete the " +
                    "configuration in the assembly and load a proper CSV.");
                return;
            }

            Tree.Nodes.Clear();
            foreach (System.Windows.Controls.TreeViewItem item in e.MergedTree.Items)
            {
                Tree.Nodes.Add(LinkNodeFromTreeViewItem(item));
            }

            Tree.ExpandAll();
            if (Tree.Nodes.Count > 0)
            {
                Tree.SelectedNode = Tree.Nodes[0];
            }

            PMComputeMassInertia.Checked = !e.UsedCSVInertial;
            PMComputeVisualCollision.Checked = !e.UsedCSVVisualCollision;
            PMComputeJointKinematics.Checked = !e.UsedCSVJointKinematics;
            PMComputeJointLimits.Checked = !e.UsedCSVJointOther;
            PMLabelCSVFilename.Caption = "Filename: " + e.CSVFilename;

            // Make the controls visible, but only enable them if values have been loaded from the CSV
            // otherwise they do need to be computed.
            EnableControl((IPropertyManagerPageControl)PMComputeMassInertia, e.UsedCSVInertial);
            EnableControl((IPropertyManagerPageControl)PMComputeVisualCollision, e.UsedCSVVisualCollision);
            EnableControl((IPropertyManagerPageControl)PMComputeJointKinematics, e.UsedCSVJointKinematics);
            EnableControl((IPropertyManagerPageControl)PMComputeJointLimits, e.UsedCSVJointOther);
            EnableControl((IPropertyManagerPageControl)PMLabelCSVFilename);
        }

        private LinkNode LinkNodeFromTreeViewItem(System.Windows.Controls.TreeViewItem item)
        {
            Link itemLink = (Link)item.Tag;
            LinkNode node = new LinkNode
            {
                Link = itemLink,
                Name = itemLink.Name,
                Text = itemLink.Name
            };
            node.IsBaseNode = item.Parent.GetType() != typeof(System.Windows.Controls.TreeViewItem);
            foreach (System.Windows.Controls.TreeViewItem child in item.Items)
            {
                node.Nodes.Add(LinkNodeFromTreeViewItem(child));
            }
            return node;
        }

        private void LoadFromCSV()
        {
            SaveActiveNode();

            LinkNode existingBaseNode = (LinkNode)Tree.Nodes[0].Clone();
            IPropertyManagerPageControl loadConfigurationControl = (IPropertyManagerPageControl)PMButtonLoad;

            if (existingBaseNode == null || !existingBaseNode.RebuildLink().AreRequiredFieldsSatisfied())
            {
                logger.Warn("Loading a configuration with an incomplete export");
                if (MessageBox.Show(
                    "This model has not been fully exported and saved. Merging may result in an incomplete URDF, " +
                    "would you like to continue?", "Continue with incomplete export?", MessageBoxButtons.YesNo) == 
                        DialogResult.No) {
                    return;
                }
            }

            OpenFileDialog loadFileDialog = new OpenFileDialog
            {
                Filter = "CSV (.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false,
                ValidateNames = true,
                CheckPathExists = true
            };

            if (loadFileDialog.ShowDialog() == DialogResult.OK)
            {
                logger.Info("Loading configuration " + loadFileDialog.FileName);
                using (Stream stream = loadFileDialog.OpenFile())
                {
                    List<Link> loadedLinks = ImportExport.LoadURDFRobotFromCSV(stream);
                    if (loadedLinks == null)
                    {
                        return;
                    }

                    logger.Info("Link successfully loaded");

                    string filename = loadFileDialog.SafeFileName;
                    string assemblyTitle = ActiveSWModel.GetTitle();

                    Link existingBaseLink = existingBaseNode.RebuildLink();
                    TreeMergeWPF wpf = new TreeMergeWPF(existingBaseLink, loadedLinks,
                        filename, assemblyTitle);
                    wpf.TreeMerged += TreeMergeCompleted;
                    wpf.Show();
                }
            }
        }

        private void OnButtonPress(int Id)
        {
            switch (Id)
            {
                case ButtonExportID:
                    ExportButtonPress();
                    break;

                case LoadConfigurationID:
                    LoadFromCSV();
                    break;

                case SitesAddButtonID:
                    AddSiteFromForm();
                    break;

                case SitesRemoveButtonID:
                    RemoveSelectedSiteFromForm();
                    break;

                case VisualGroupsAddButtonID:
                    AddVisualGroupFromForm();
                    break;

                case VisualGroupsRemoveButtonID:
                    RemoveSelectedVisualGroupFromForm();
                    break;

                case CollisionGroupsAddButtonID:
                    AddCollisionGroupFromForm();
                    break;

                case CollisionGroupsRemoveButtonID:
                    RemoveSelectedCollisionGroupFromForm();
                    break;

                case BitmapAxisFlipID:
                    ToggleAxisFlip();
                    break;

                default:
                    break;
            }
        }

        // Handler for the "Reverse Direction" bitmap button next to the
        // Reference Axis combobox. swControlType_BitmapButton fires CLICK
        // events (not check events) so we maintain the toggle state ourselves
        // in currentAxisFlipped. The new state is written through to the
        // active node's Joint.AxisFlipped immediately (rather than waiting
        // for SaveActiveNode) so the persisted state and the redrawn overlay
        // arrow stay in lockstep without needing a "dirty" flag.
        private void ToggleAxisFlip()
        {
            currentAxisFlipped = !currentAxisFlipped;

            LinkNode active = (LinkNode)Tree.SelectedNode;
            if (active != null && !active.IsBaseNode && active.Link != null && active.Link.Joint != null)
            {
                active.Link.Joint.AxisFlipped = currentAxisFlipped;
            }

            RefreshAxisDirectionPreview();
        }

        // Re-resolves the joint coord-sys + axis (with the current flip state)
        // and (re)draws the overlay arrow in the SW viewport. Called whenever
        // the user changes the axis combobox, the coord-sys combobox, the
        // flip button, or switches links in the tree. Pure UI side effect:
        // does NOT mutate any Joint state - that lives on currentAxisFlipped
        // and is persisted by ToggleAxisFlip / SaveActiveNode.
        private void RefreshAxisDirectionPreview()
        {
            if (PMComboBoxAxes == null || PMComboBoxCoordSys == null)
            {
                return;
            }

            string axisName = PMComboBoxAxes.get_ItemText(-1);
            string coordSysName = PMComboBoxCoordSys.get_ItemText(-1);

            // Use SW's own selection coloring to highlight the chosen axis
            // line in the model view. Skips placeholders ("None" /
            // "Automatically Generate") because those have no resolvable SW
            // feature. Same primitive SelectFeatures uses on tree-node
            // switch, so the highlight behavior is consistent across both
            // entry paths.
            if (!string.IsNullOrWhiteSpace(axisName) &&
                axisName != "None" && axisName != "Automatically Generate")
            {
                try
                {
                    ActiveSWModel.Extension.SelectByID2(
                        axisName, "AXIS", 0, 0, 0, true, -1, null, 0);
                }
                catch (Exception ex)
                {
                    logger.Warn("RefreshAxisDirectionPreview: SelectByID2 highlight failed: " + ex.Message);
                }
            }

            ExportHelper.AxisPreview preview =
                Exporter.PreviewAxisDirection(coordSysName, axisName, currentAxisFlipped);

            if (!preview.IsValid)
            {
                Exporter.ClearAxisOverlay();
                return;
            }

            Exporter.DrawAxisOverlay(preview.OriginGlobal, preview.AxisGlobal);
        }

        // Saves the components currently selected in the visual SelectionBox
        // into the active visual group of the active link, creates a new empty
        // group, and refreshes the listbox so the user can populate it.
        private void AddVisualGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);

            // Commit the user's current selection into the previously-active
            // group before we create a new one.
            CommitActiveVisualGroupSelection(node);

            string requestedName = (PMTextBoxVisualGroupName.Text ?? "").Trim();
            string newName = !string.IsNullOrEmpty(requestedName)
                ? requestedName
                : NextDefaultGroupName(node.Link.VisualGroups, MeshGroup.DefaultVisualName(node.Link.Name));
            node.Link.VisualGroups.Add(new MeshGroup(newName));
            PMTextBoxVisualGroupName.Text = "";

            activeVisualGroupIndex = node.Link.VisualGroups.Count - 1;
            RefreshVisualGroupsListbox(node);
            LoadActiveVisualGroupIntoSelectionBox(node);
        }

        private void RemoveSelectedVisualGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (node.Link.VisualGroups.Count == 0)
            {
                return;
            }
            short selected = PMListBoxVisualGroups.CurrentSelection;
            if (selected < 0 || selected >= node.Link.VisualGroups.Count)
            {
                return;
            }
            node.Link.VisualGroups.RemoveAt(selected);
            if (activeVisualGroupIndex >= node.Link.VisualGroups.Count)
            {
                activeVisualGroupIndex = node.Link.VisualGroups.Count - 1;
            }
            if (activeVisualGroupIndex < 0)
            {
                activeVisualGroupIndex = 0;
            }
            RefreshVisualGroupsListbox(node);
            LoadActiveVisualGroupIntoSelectionBox(node);
        }

        private void AddCollisionGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);

            CommitActiveCollisionGroupSelection(node);

            string requestedName = (PMTextBoxCollisionGroupName.Text ?? "").Trim();
            string newName = !string.IsNullOrEmpty(requestedName)
                ? requestedName
                : NextDefaultGroupName(node.Link.CollisionGroups, MeshGroup.DefaultCollisionName(node.Link.Name));
            node.Link.CollisionGroups.Add(new MeshGroup(newName));
            PMTextBoxCollisionGroupName.Text = "";

            activeCollisionGroupIndex = node.Link.CollisionGroups.Count - 1;
            RefreshCollisionGroupsListbox(node);
            LoadActiveCollisionGroupIntoSelectionBox(node);
        }

        private void RemoveSelectedCollisionGroupFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (node.Link.CollisionGroups.Count == 0)
            {
                return;
            }
            short selected = PMListBoxCollisionGroups.CurrentSelection;
            if (selected < 0 || selected >= node.Link.CollisionGroups.Count)
            {
                return;
            }
            node.Link.CollisionGroups.RemoveAt(selected);
            if (activeCollisionGroupIndex >= node.Link.CollisionGroups.Count)
            {
                activeCollisionGroupIndex = node.Link.CollisionGroups.Count - 1;
            }
            if (activeCollisionGroupIndex < 0)
            {
                activeCollisionGroupIndex = 0;
            }
            RefreshCollisionGroupsListbox(node);
            LoadActiveCollisionGroupIntoSelectionBox(node);
        }

        // Commits the visual SelectionBox's current contents into the active
        // visual group on the active link. Called whenever the user is about
        // to change the active group or active node.
        private void CommitActiveVisualGroupSelection(LinkNode node)
        {
            if (node == null)
            {
                return;
            }
            // The page is closing: SolidWorks has already released the marks
            // that back this SelectionBox, so reading them back would clear
            // the group with whatever stale state happens to be there.
            // OnSelectionboxListChanged has kept the group in sync on every
            // user pick, so the in-memory data is already authoritative.
            if (pageIsClosing)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (activeVisualGroupIndex < 0 || activeVisualGroupIndex >= node.Link.VisualGroups.Count)
            {
                return;
            }
            MeshGroup group = node.Link.VisualGroups[activeVisualGroupIndex];
            if (group.Components == null)
            {
                group.Components = new List<Component2>();
            }

            // Teardown defense: if SolidWorks has 0 marked items but the
            // active group already holds components, this commit is almost
            // certainly being driven by a programmatic clear (PMPage tear-
            // down on green-check, or another loader's ClearSelection2(true)
            // cascade) rather than a deliberate user action. The destructive
            // Clear+Refill below would wipe a freshly-picked component, so
            // we bail out and let the existing in-memory state stand. The
            // OnSelectionboxListChanged handler kept group.Components in
            // sync for every user pick on the way in, so we already have
            // the authoritative list. Trade-off: the user cannot clear the
            // LAST component in a group through the SelectionBox UI alone -
            // they need to remove the group entirely or pick a different
            // component first. That UX cost is worth avoiding silent data
            // loss on the last-edited link.
            int markedCount = ActiveSWModel.SelectionManager.GetSelectedObjectCount2(PMSelectionVisual.Mark);
            if (markedCount == 0 && group.Components.Count > 0)
            {
                return;
            }

            group.Components.Clear();
            CommonSwOperations.GetSelectedComponents(
                ActiveSWModel, group.Components, PMSelectionVisual.Mark);
        }

        private void CommitActiveCollisionGroupSelection(LinkNode node)
        {
            if (node == null)
            {
                return;
            }
            // See CommitActiveVisualGroupSelection: skip during OnClose so we
            // don't clobber the active group from an empty SelectionMgr.
            if (pageIsClosing)
            {
                return;
            }
            EnsureGroupsInitialized(node);
            if (activeCollisionGroupIndex < 0 || activeCollisionGroupIndex >= node.Link.CollisionGroups.Count)
            {
                return;
            }
            MeshGroup group = node.Link.CollisionGroups[activeCollisionGroupIndex];
            if (group.Components == null)
            {
                group.Components = new List<Component2>();
            }

            // See CommitActiveVisualGroupSelection: same teardown / cascade
            // defense applied to the collision side.
            int markedCount = ActiveSWModel.SelectionManager.GetSelectedObjectCount2(PMSelectionCollision.Mark);
            if (markedCount == 0 && group.Components.Count > 0)
            {
                return;
            }

            group.Components.Clear();
            CommonSwOperations.GetSelectedComponents(
                ActiveSWModel, group.Components, PMSelectionCollision.Mark);
        }

        // Loads the active visual group's components into the visual
        // SelectionBox. Called after the active group changes.
        private void LoadActiveVisualGroupIntoSelectionBox(LinkNode node)
        {
            // Both the ClearSelection2 below and the subsequent
            // SelectComponents call fire OnSelectionboxListChanged once per
            // affected item. Without the suppress guard around the WHOLE
            // body, the Count=0 event from ClearSelection2 would re-enter
            // CommitActiveVisualGroupSelection and clobber group.Components
            // with an empty SelectionMgr read - that's the data-loss path
            // the user hit on the end-effector link.
            suppressGroupListboxRefresh = true;
            try
            {
                // Drop the previous selection so we don't accumulate
                // components from the previously-active group.
                ActiveSWModel.ClearSelection2(true);
                if (node == null)
                {
                    return;
                }
                EnsureGroupsInitialized(node);
                if (activeVisualGroupIndex < 0 || activeVisualGroupIndex >= node.Link.VisualGroups.Count)
                {
                    return;
                }
                MeshGroup group = node.Link.VisualGroups[activeVisualGroupIndex];
                if (group.Components == null)
                {
                    return;
                }
                CommonSwOperations.SelectComponents(
                    ActiveSWModel, group.Components, false, PMSelectionVisual.Mark);
            }
            finally
            {
                suppressGroupListboxRefresh = false;
            }
        }

        private void LoadActiveCollisionGroupIntoSelectionBox(LinkNode node)
        {
            // See LoadActiveVisualGroupIntoSelectionBox: ClearSelection2(true)
            // clears all marks, including the visual mark just populated by
            // the prior load call. The Count=0 event for the visual box that
            // SolidWorks fires would otherwise clobber the visual group's
            // components, so we suppress for the entire body.
            suppressGroupListboxRefresh = true;
            try
            {
                ActiveSWModel.ClearSelection2(true);
                if (node == null)
                {
                    return;
                }
                EnsureGroupsInitialized(node);
                if (activeCollisionGroupIndex < 0 || activeCollisionGroupIndex >= node.Link.CollisionGroups.Count)
                {
                    return;
                }
                MeshGroup group = node.Link.CollisionGroups[activeCollisionGroupIndex];
                if (group.Components == null)
                {
                    return;
                }
                CommonSwOperations.SelectComponents(
                    ActiveSWModel, group.Components, false, PMSelectionCollision.Mark);
            }
            finally
            {
                suppressGroupListboxRefresh = false;
            }
        }

        public void RefreshVisualGroupsListbox(LinkNode node)
        {
            PMListBoxVisualGroups.Clear();
            if (node == null || node.Link.VisualGroups == null)
            {
                return;
            }
            for (int i = 0; i < node.Link.VisualGroups.Count; i++)
            {
                MeshGroup g = node.Link.VisualGroups[i];
                int count = (g.Components != null) ? g.Components.Count : 0;
                string label = (string.IsNullOrEmpty(g.Name) ? "(unnamed)" : g.Name) +
                    " (" + count + " comp.)";
                PMListBoxVisualGroups.AddItems(label);
            }
            if (activeVisualGroupIndex >= 0 && activeVisualGroupIndex < node.Link.VisualGroups.Count)
            {
                PMListBoxVisualGroups.CurrentSelection = (short)activeVisualGroupIndex;
            }
        }

        public void RefreshCollisionGroupsListbox(LinkNode node)
        {
            PMListBoxCollisionGroups.Clear();
            if (node == null || node.Link.CollisionGroups == null)
            {
                return;
            }
            for (int i = 0; i < node.Link.CollisionGroups.Count; i++)
            {
                MeshGroup g = node.Link.CollisionGroups[i];
                int count = (g.Components != null) ? g.Components.Count : 0;
                string label = (string.IsNullOrEmpty(g.Name) ? "(unnamed)" : g.Name) +
                    " (" + count + " comp.)";
                PMListBoxCollisionGroups.AddItems(label);
            }
            if (activeCollisionGroupIndex >= 0 && activeCollisionGroupIndex < node.Link.CollisionGroups.Count)
            {
                PMListBoxCollisionGroups.CurrentSelection = (short)activeCollisionGroupIndex;
            }
        }

        // Ensures the link has a non-null VisualGroups / CollisionGroups list
        // and at least one visual group (so the SelectionBox always has a
        // place to commit to). Collision is allowed to be empty (URDF
        // fallback).
        private static void EnsureGroupsInitialized(LinkNode node)
        {
            if (node == null || node.Link == null)
            {
                return;
            }
            node.Link.MigrateLegacyComponents();
            if (node.Link.VisualGroups == null)
            {
                node.Link.VisualGroups = new List<MeshGroup>();
            }
            if (node.Link.VisualGroups.Count == 0)
            {
                node.Link.VisualGroups.Add(new MeshGroup(MeshGroup.DefaultVisualName(node.Link.Name)));
            }
            if (node.Link.CollisionGroups == null)
            {
                node.Link.CollisionGroups = new List<MeshGroup>();
            }
        }

        // Builds a default name for a brand-new group that doesn't collide
        // with the existing names on the link (e.g. "<link>_visual_2").
        private static string NextDefaultGroupName(List<MeshGroup> groups, string baseName)
        {
            HashSet<string> existing = new HashSet<string>();
            foreach (MeshGroup g in groups)
            {
                if (!string.IsNullOrEmpty(g.Name))
                {
                    existing.Add(g.Name);
                }
            }
            if (!existing.Contains(baseName))
            {
                return baseName;
            }
            int n = 2;
            while (existing.Contains(baseName + "_" + n))
            {
                n++;
            }
            return baseName + "_" + n;
        }

        private void AddSiteFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null)
            {
                return;
            }
            string name = (PMTextBoxSiteName.Text ?? "").Trim();
            string coord = PMComboBoxSiteCoordSys.get_ItemText(-1);
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a site name before adding the site.");
                return;
            }
            if (string.IsNullOrEmpty(coord) || coord == "Automatically Generate")
            {
                MessageBox.Show("Please select a reference coordinate system for the site.");
                return;
            }
            if (node.Link.Sites == null)
            {
                node.Link.Sites = new List<SiteSpec>();
            }
            node.Link.Sites.Add(new SiteSpec(name, coord));
            PMTextBoxSiteName.Text = "";
            RefreshSitesListbox(node);
        }

        private void RemoveSelectedSiteFromForm()
        {
            LinkNode node = (LinkNode)Tree.SelectedNode;
            if (node == null || node.Link.Sites == null || node.Link.Sites.Count == 0)
            {
                return;
            }
            short selected = PMListBoxSites.CurrentSelection;
            if (selected < 0 || selected >= node.Link.Sites.Count)
            {
                return;
            }
            node.Link.Sites.RemoveAt(selected);
            RefreshSitesListbox(node);
        }

        public void RefreshSitesListbox(LinkNode node)
        {
            PMListBoxSites.Clear();
            if (node == null || node.Link.Sites == null)
            {
                return;
            }
            foreach (SiteSpec site in node.Link.Sites)
            {
                PMListBoxSites.AddItems(site.Name + " : " + site.CoordinateSystemName);
            }
        }

        // Called when a PropertyManagerPageButton is pressed. In our case, that's only the
        // export button for now
        void IPropertyManagerPage2Handler9.OnButtonPress(int Id)
        {
            try
            {
                OnButtonPress(Id);
            }
            catch (Exception e)
            {
                logger.Error("Exception caught handling button press " + Id, e);
                MessageBox.Show("There was a problem with the configuration property manager: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
        }

        void IPropertyManagerPage2Handler9.OnClose(int Reason)
        {
            // Marked selections owned by the SelectionBoxes are released by
            // SolidWorks before OnClose runs, so SaveActiveNode must not try
            // to refresh the active link's component lists from the SelectionMgr
            // (the read would return 0 items and clobber data the user committed
            // via OnSelectionboxListChanged). The pageIsClosing guard makes the
            // SelectionMgr-derived commits no-op for the duration of this call.
            pageIsClosing = true;
            try
            {
                if (Reason ==
                    (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Cancel)
                {
                    logger.Info("Configuration canceled");
                    SaveActiveNode();
                }
                else if (Reason ==
                    (int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay)
                {
                    logger.Info("Configuration saved");
                    SaveActiveNode();
                    SaveConfigTree(ActiveSWModel, (LinkNode)Tree.Nodes[0], false);
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception caught on close ", e);
                MessageBox.Show("There was a problem closing the property manager: \n\"" +
                    e.Message + "\"\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
            finally
            {
                pageIsClosing = false;
                // Clear any axis overlay arrow we drew via IBody2.Display3.
                // Transient bodies are session-scoped (not saved with the
                // document) but they remain visible in the viewport until
                // explicitly hidden, so we must drop our refs here.
                try
                {
                    Exporter.ClearAxisOverlay();
                }
                catch (Exception ex)
                {
                    logger.Warn("Failed to clear axis overlay on PM close: " + ex.Message);
                }
                // NOTE: we deliberately do NOT call Dispose() here. The Tree
                // TreeView's child nodes (the LinkNode hierarchy) are
                // about to be transferred to AssemblyExportForm in
                // ExportButtonPress; disposing Tree at this point would
                // invalidate those TreeNodes (they would still hold a
                // stale TreeView reference and any subsequent
                // TreeNodeCollection.Add would throw "Cannot add or
                // insert ... in more than one place"). Dispose() is
                // available for callers that want to release the .NET
                // Forms resources after the export workflow has fully
                // detached BaseNode from Tree.
            }
        }

        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id)
        {
        }

        bool IPropertyManagerPage2Handler9.OnHelp()
        {
            return true;
        }

        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id)
        {
            if (Wparam == (int)Keys.Enter)
            {
                return true;
            }
            return false;
        }

        void IPropertyManagerPage2Handler9.OnLostFocus(int Id)
        {
            Debug.Print("Control box " + Id + " has lost focus");
        }

        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value)
        {
            if (Id == NumBoxChildCountID)
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                CreateNewNodes(node);
            }
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id)
        {
            Debug.Print("The focus has moved to selection box " + Id);
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count)
        {
            // Move focus to next selection box if right-mouse button pressed
            PMPage.SetCursor((int)swPropertyManagerPageCursors_e.swPropertyManagerPageCursors_Advance);

            // The Visual / Collision SelectionBoxes mirror the active group's
            // components; when the user adds or removes a pick we must commit
            // back to the group and rebuild the listbox row text so the
            // "(N comp.)" count stays in sync without requiring a re-click.
            // The suppress flag short-circuits programmatic populates done by
            // LoadActive*GroupIntoSelectionBox / FillPropertyManager.
            if (suppressGroupListboxRefresh)
            {
                return;
            }

            // Skip when the page is in the middle of closing. SolidWorks
            // releases marked selections at PMPage teardown, which can
            // re-enter this handler with Count=0; the destructive Clear+
            // refill in CommitActive*GroupSelection would wipe the last-
            // edited link's groups in that case.
            if (pageIsClosing)
            {
                return;
            }

            LinkNode active = (Tree != null) ? (LinkNode)Tree.SelectedNode : null;
            if (active == null)
            {
                return;
            }

            if (Id == SelectionVisualID)
            {
                CommitActiveVisualGroupSelection(active);
                RefreshVisualGroupsListbox(active);
            }
            else if (Id == SelectionCollisionID)
            {
                CommitActiveCollisionGroupSelection(active);
                RefreshCollisionGroupsListbox(active);
            }
            else if (Id == SelectionInertialID)
            {
                // Mirror the visual / collision pattern: commit on every pick
                // so InertialComponents stays current without depending on the
                // SelectionMgr being live during OnClose. SaveActiveNode skips
                // its inertial refresh when pageIsClosing is true, so this
                // incremental commit is the authoritative path for the
                // green-check-without-navigating case.
                if (active.Link.InertialComponents == null)
                {
                    active.Link.InertialComponents = new List<Component2>();
                }

                // Same teardown defense as CommitActiveVisualGroupSelection:
                // if the inertial mark is empty but we already hold inertial
                // components, treat this as a programmatic teardown / clear
                // and skip the destructive refresh.
                int markedCount = ActiveSWModel.SelectionManager.GetSelectedObjectCount2(
                    PMSelectionInertial.Mark);
                if (markedCount == 0 && active.Link.InertialComponents.Count > 0)
                {
                    return;
                }

                CommonSwOperations.GetSelectedComponents(
                    ActiveSWModel, active.Link.InertialComponents, PMSelectionInertial.Mark);
            }
        }

        bool IPropertyManagerPage2Handler9.OnSubmitSelection(
            int Id, object Selection, int SelType, ref string ItemText)
        {
            // This method must return true for selections to occur
            return true;
        }

        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text)
        {
            if (Id == TextBoxLinkNameID)
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                node.Text = PMTextBoxLinkName.Text;
                node.Name = PMTextBoxLinkName.Text;
            }
        }

        int IPropertyManagerPage2Handler9.OnWindowFromHandleControlCreated(int Id, bool Status)
        {
            return 0;
        }

        #endregion Implemented Property Manager Page Handler Methods

        #region TreeView handler methods

        // Upon selection of a node, the node displayed on the PMPage is saved and the
        // selected one is then set
        private void TreeAfterSelect(object sender, TreeViewEventArgs e)
        {
            try
            {
                if (!automaticallySwitched && e.Node != null)
                {
                    SwitchActiveNodes((LinkNode)e.Node);
                }
                automaticallySwitched = false;
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view AfterSelect ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        // Captures which node was right clicked
        private void TreeNodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            rightClickedNode = (LinkNode)e.Node;
        }

        //When a keyboard key is pressed on the tree
        private void TreeKeyDown(object sender, KeyEventArgs e)
        {
            if (rightClickedNode.IsEditing)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    rightClickedNode.EndEdit(false);
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    rightClickedNode.EndEdit(true);
                }
            }
        }

        // The callback for the configuration page context menu 'Add Child' option
        private void AddChildClick(object sender, EventArgs e)
        {
            try
            {
                CreateNewNodes(rightClickedNode, 1);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view add child ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        // The callback for the configuration page context menu 'Remove Child' option
        private void RemoveChildClick(object sender, EventArgs e)
        {
            try
            {
                LinkNode parent = (LinkNode)rightClickedNode.Parent;
                parent.Nodes.Remove(rightClickedNode);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view remove child ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        // The callback for the configuration page context menu 'Rename Child' option
        // This isn't really working right now, so the option was deactivated from the
        // context menu
        private void RenameChildClick(object sender, EventArgs e)
        {
            try
            {
                Tree.SelectedNode = rightClickedNode;
                Tree.LabelEdit = true;
                rightClickedNode.BeginEdit();
                PMPage.SetFocus(dotNetTree);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view rename child ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        private void TreeItemDrag(object sender, ItemDragEventArgs e)
        {
            try
            {
                Tree.DoDragDrop(e.Item, DragDropEffects.Move);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view Drag ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        private void TreeDragOver(object sender, DragEventArgs e)
        {
            try
            {
                // Retrieve the client coordinates of the mouse position.
                Point targetPoint = Tree.PointToClient(new Point(e.X, e.Y));

                // Select the node at the mouse position.
                Tree.SelectedNode = Tree.GetNodeAt(targetPoint);
                e.Effect = DragDropEffects.Move;
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view Drag Over ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        private void TreeDragEnter(object sender, DragEventArgs e)
        {
            try
            {
                // Retrieve the client coordinates of the mouse position.
                Point targetPoint = Tree.PointToClient(new Point(e.X, e.Y));

                // Select the node at the mouse position.
                Tree.SelectedNode = Tree.GetNodeAt(targetPoint);
                e.Effect = DragDropEffects.Move;
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view DragEnter ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        private void DoDragDrop(DragEventArgs e)
        {
            // Retrieve the client coordinates of the drop location.
            Point point = Tree.PointToClient(new Point(e.X, e.Y));

            // Retrieve the node at the drop location.
            LinkNode targetNode = (LinkNode)Tree.GetNodeAt(point);

            LinkNode draggedNode = (LinkNode)e.Data.GetData(typeof(LinkNode));

            // Check if the move is valid, if not then we won't do anything
            if (draggedNode == null || draggedNode == targetNode || draggedNode.TreeView != Tree)
            {
                return;
            }

            // If the it was dropped into the box itself, but not onto an actual node
            targetNode = targetNode ?? (LinkNode)Tree.TopNode;

            draggedNode.Remove();
            targetNode.Nodes.Add(draggedNode);
            targetNode.ExpandAll();
        }

        private void TreeDragDrop(object sender, DragEventArgs e)
        {
            try
            {
                DoDragDrop(e);
            }
            catch (Exception ex)
            {
                logger.Error("Exception caught on tree view Drag Drop ", ex);
                MessageBox.Show("There was a problem with the property manager: \n\"" +
                    ex.Message + "\"\nEmail your maintainer with the log file found at " +
                    Logger.GetFileName());
            }
        }

        #endregion TreeView handler methods

        //A method that sets up the Property Manager Page.
        //
        // Visual order (top -> bottom):
        //   Sub-section "Setup": Preview/Export, Load Configuration,
        //                "Imported File:" label (hidden until CSV import),
        //                4 "Compute X" checkboxes (hidden until CSV import),
        //                Link Tree (host control)
        //   Sub-section "Link & Joint Properties"
        //   Sub-section "Components" (visual / collision toggle / collision /
        //                inertial)
        //   Sub-section "Sites (MJCF)"
        //
        // The Tree object's full setup (event wiring, handle bind, root node,
        // focus) happens at the bottom of this method so the first
        // TreeAfterSelect -> FillPropertyManager call sees fully-constructed
        // PM controls.
        private void SetupPropertyManagerPage(ref string caption, ref string tip,
            ref long options, ref int controlType, ref int alignment)
        {
            // === Sub-section "Setup" (declared first to anchor the top of the page) ===
            // SolidWorks renders AddGroupBox items in declaration order, but
            // free top-level controls (PMPage.AddControl2 outside any group)
            // are not guaranteed to sit above subsequent group boxes. Wrapping
            // the global controls (Preview/Export, Load Configuration, the
            // post-import options, and the Link Tree) in their own group box
            // declared first is the supported way to keep them at the top of
            // the page across SolidWorks versions.
            caption = "Setup";
            options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            PMSetupGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                SetupGroupID, caption, (int)options);

            // Setup row 1: Export button.
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonExport = PMSetupGroup.AddControl2(ButtonExportID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Preview/Export", 0, (int)options,
                "Preview and export the generated description");

            // Setup row 2: Load Configuration... button.
            PMButtonLoad = PMSetupGroup.AddControl2(LoadConfigurationID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Load Configuration", 0, (int)options,
                "Import values from a CSV file");

            // Setup row 3: "Imported File:" label, hidden until a CSV import.
            // options = 0 keeps it invisible by default; TreeMergeCompleted ->
            // EnableControl flips Visible / Enabled to true after a successful
            // CSV merge.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Imported File: ";
            tip = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = 0;
            PMLabelCSVFilename = PMSetupGroup.AddControl2(
                LoadedCSVFilenameID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Setup rows 4-7: post-import "Compute X" checkboxes. Same hidden-
            // by-default treatment as the label above; gate which CSV-loaded
            // values get recomputed from CAD on export. Only the user-visible
            // label text and field assignments differ across the four.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = "Compute Mass and Inertia";
            tip = "External values have been loaded. Check this box to recompute the Mass and Inertia values";
            options = 0;
            PMComputeMassInertia = PMSetupGroup.AddControl2(
                ComputeMassInertiaID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeMassInertia.Checked = true;

            caption = "Compute Visual and Collision";
            tip = "External values have been loaded. Check this box to recompute the visual and collision values";
            PMComputeVisualCollision = PMSetupGroup.AddControl2(
                ComputeVisualCollisionID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeVisualCollision.Checked = true;

            caption = "Compute Joint Kinematics";
            tip = "External values have been loaded. Check this box to recompute the joint kinematics";
            PMComputeJointKinematics = PMSetupGroup.AddControl2(
                ComputeJointKinematicsID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeJointKinematics.Checked = true;

            caption = "Compute Joint Limits";
            tip = "External values have been loaded. Check this box to recompute the joint limits";
            PMComputeJointLimits = PMSetupGroup.AddControl2(
                ComputeJointLimitsID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeJointLimits.Checked = true;

            // Setup row 8: Link Tree host control. Only the host
            // PropertyManagerPageWindowFromHandle is created here. The actual
            // TreeView (event handlers, root node, focus) is wired up at the
            // very end of this method so the first TreeAfterSelect ->
            // FillPropertyManager call sees fully-constructed PM controls.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle;
            caption = "Link Tree";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTree = PMSetupGroup.AddControl2(dotNetTree,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle, caption, 0, (int)options, "");
            PMTree.Height = 163;

            // === Sub-section "Link & Joint Properties" ===
            // Per-link inputs that don't involve component selection:
            // names, coord systems, axis, joint type, child count.
            caption = "Link & Joint Properties";
            options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            PMGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(GroupID, caption, (int)options);

            //Create the parent link label (static)
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Parent Link";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            //Create the parent link name label, the one that is updated
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible + (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelParentLink = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelLinkNameID, (short)controlType, caption, (short)alignment, (int)options, "");

            //Create the link name text box label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Link Name";
            tip = "Enter the name of the link";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            //Create the link name text box
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            caption = "base_link";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            tip = "Enter the name of the link";
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTextBoxLinkName = (PropertyManagerPageTextbox)PMGroup.AddControl2(
                TextBoxLinkNameID, (short)(controlType), caption, (short)alignment, (int)options, tip);

            //Create the joint name text box label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Joint Name";
            tip = "Enter the name of the joint";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelJointName = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelJointNameID, (short)controlType, caption, (short)alignment, (int)options, tip);

            //Create the joint name text box
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            caption = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            tip = "Enter the name of the joint";
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMTextBoxJointName = (PropertyManagerPageTextbox)PMGroup.AddControl2(
                TextBoxLinkNameID, (short)(controlType), caption, (short)alignment, (int)options, tip);

            //Create the global origin coordinate sys label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Global Origin Coordinate System";
            tip = "Select the reference coordinate system for the global origin";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelGlobalCoordsys = (PropertyManagerPageLabel)PMGroup.AddControl2(
                IDLabelGlobalCoordsys, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for Coordinate systems
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = "Global Origin Coordinate System Name";
            tip = "Select the reference coordinate system for the global origin";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMComboBoxGlobalCoordsys = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                IDGlobalCoordsys, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxGlobalCoordsys.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            //Create the ref coordinate sys label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Reference Coordinate System";
            tip = "Select the reference coordinate system for the joint origin";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = 0;
            PMLabelCoordSys = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelCoordSysID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for Coordinate systems
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = "Reference Coordinate System Name";
            tip = "Select the reference coordinate system for the joint origin";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = 0;
            PMComboBoxCoordSys = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                ComboBoxCoordSysID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxCoordSys.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            //Create the ref axis label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Reference Axis";
            tip = "Select the reference axis for the joint";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelAxes = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelAxesID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for axes
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = "Reference Axis Name";
            tip = "Select the reference axis for the joint";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMComboBoxAxes = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                ComboBoxCoordSysID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxAxes.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;

            // "Reverse Direction" bitmap button - same standard icon SW uses on
            // its own coord-system / extrude PMs. Stacked on the row below the
            // axis combobox; SW does not reliably honor side-by-side layout
            // hints for PM controls (see AGENTS.md "PropertyManagerPage layout
            // quirks"). The icon makes the intent clear regardless of position.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_BitmapButton;
            caption = "Reverse Direction";
            tip = "Reverse the positive direction of the reference axis";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMBitmapAxisFlip = (PropertyManagerPageBitmapButton)PMGroup.AddControl2(
                BitmapAxisFlipID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMBitmapAxisFlip.SetStandardBitmaps(
                (int)swPropertyManagerPageBitmapButtons_e.swBitmapButtonImage_reverse_direction);

            //Create the joint type label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Joint Type";
            tip = "Select the joint type";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMLabelJointType = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelAxesID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create pull down menu for joint type
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = "Joint type";
            tip = "Select the joint type";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible;
            PMComboBoxJointType = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                ComboBoxCoordSysID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxJointType.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxJointType.AddItems(new string[] {
                "Automatically Detect", "continuous", "revolute", "prismatic", "fixed" });

            //Number of child links - kept inside Link & Joint Properties so the
            //per-link basics (names, frames, joint, child count) are grouped.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Number of child links";
            tip = "Enter the number of child links and they will be automatically added";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Numberbox;
            caption = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            tip = "Enter the number of child links and they will be automatically added";
            options = (int)swAddControlOptions_e.swControlOptions_Enabled +
                (int)swAddControlOptions_e.swControlOptions_Visible;
            PMNumberBoxChildCount = PMGroup.AddControl2(
                NumBoxChildCountID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMNumberBoxChildCount.SetRange2(
                (int)swNumberboxUnitType_e.swNumberBox_UnitlessInteger, 0, int.MaxValue, true, 1, 1, 1);
            PMNumberBoxChildCount.Value = 0;

            // === Sub-section "Components" ===
            // Inertial source selector + Visual / Collision group editors +
            // optional Custom-mode Inertial Components selection box.
            caption = "Components";
            options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            PMComponentsGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                ComponentsGroupID, caption, (int)options);

            swSelectType_e[] filters = new swSelectType_e[1];
            filters[0] = swSelectType_e.swSelCOMPONENTS;
            object filterObj = filters;

            // Inertial source combobox.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Inertial Source";
            tip = "Choose which set of components drives the link's mass and inertia";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelInertialSource = (PropertyManagerPageLabel)PMComponentsGroup.AddControl2(
                LabelInertialSourceID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = "Inertial Source";
            tip = "Visual: use visual components. Collision: use collision components. Custom: use the inertial components box below.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMComboBoxInertialSource = (PropertyManagerPageCombobox)PMComponentsGroup.AddControl2(
                ComboInertialSourceID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxInertialSource.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxInertialSource.AddItems(new string[] {
                "Visual",
                "Collision",
                "Custom (Inertial Components)" });
            PMComboBoxInertialSource.CurrentSelection = 0;

            // --- Visual Groups -------------------------------------------------
            // Each visual group becomes one STL + one <visual> (URDF) /
            // <mesh>+<geom class="visual"> (MJCF) on export. Single-group case
            // keeps the historical "<link>_visual.STL" filename and behaves
            // identically to the legacy single-list UI.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Visual Groups";
            tip = "Define one or more named groups of components. Each group is exported as its own visual mesh.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelVisualComponents = (PropertyManagerPageLabel)PMComponentsGroup.AddControl2(
                LabelVisualID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Click a row to load that group's components into the box below. To add a new group, type a name and click Add Group.";
            tip = "Components selected in the box below belong to the highlighted group.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMComponentsGroup.AddControl2(
                VisualGroupsHelpLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            caption = "";
            tip = "Visual groups defined for this link. Click a row to edit it; click Remove Selected Group to delete it.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMListBoxVisualGroups = (PropertyManagerPageListbox)PMComponentsGroup.AddControl2(
                VisualGroupsListBoxID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMListBoxVisualGroups.Height = 50;

            // SelectionBox sits directly under the listbox so the visual flow
            // is "pick a row -> edit its components below". The bottom half of
            // the editor (name label / textbox / Add / Remove) handles the
            // separate workflow of creating or removing groups.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            caption = "Components for the highlighted visual group";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSelectionVisual = (PropertyManagerPageSelectionbox)PMComponentsGroup.AddControl2(
                SelectionVisualID, (short)controlType, caption, (short)alignment, (int)options,
                "Components belonging to the visual group selected above.");
            PMSelectionVisual.AllowSelectInMultipleBoxes = true;
            PMSelectionVisual.SingleEntityOnly = false;
            PMSelectionVisual.AllowMultipleSelectOfSameEntity = false;
            PMSelectionVisual.Height = 40;
            PMSelectionVisual.SetSelectionFilters(filterObj);
            PMSelectionVisual.Mark = VisualSelectionMark;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Group name (for new group)";
            tip = "Used as the new group's display name and as the suffix on its mesh filename.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMComponentsGroup.AddControl2(
                VisualGroupsNameLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            caption = "";
            tip = "Group name for the next group to add.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTextBoxVisualGroupName = (PropertyManagerPageTextbox)PMComponentsGroup.AddControl2(
                VisualGroupsNameTextBoxID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Add Visual Group";
            tip = "Save the current selection into the highlighted group, then create a new empty group.";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonVisualGroupAdd = (PropertyManagerPageButton)PMComponentsGroup.AddControl2(
                VisualGroupsAddButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Remove Selected Visual Group";
            tip = "Delete the highlighted visual group from this link.";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonVisualGroupRemove = (PropertyManagerPageButton)PMComponentsGroup.AddControl2(
                VisualGroupsRemoveButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // --- "Use visual groups as collision" toggle ----------------------
            // Sits between the Visual Groups block and the Collision Groups
            // editor. When checked, SetCollisionEditorVisible(false) hides the
            // entire collision editor below and ExportHelper reuses the visual
            // meshes for collision via Link.CollisionUsesVisual.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = "Use visual groups as collision";
            tip = "When checked, the visual groups are reused as collision meshes; the collision editor below is hidden so you don't have to re-pick the same components.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMCheckCollisionUsesVisual = PMComponentsGroup.AddControl2(
                CheckCollisionUsesVisualID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMCheckCollisionUsesVisual.Checked = false;

            // --- Collision Groups ----------------------------------------------
            // Mirrors Visual Groups. An empty Collision Groups list falls back
            // to using the visual meshes for collision (URDF/MJCF backward-
            // compat behavior).
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Collision Groups";
            tip = "Define one or more named groups of components. Each group is exported as its own collision mesh. Empty list reuses the visual meshes for collision.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelCollisionComponents = (PropertyManagerPageLabel)PMComponentsGroup.AddControl2(
                LabelCollisionID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Click a row to load that group's components into the box below. To add a new group, type a name and click Add Group.";
            tip = "Components selected in the box below belong to the highlighted group.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelCollisionGroupsHelp = (PropertyManagerPageLabel)PMComponentsGroup.AddControl2(
                CollisionGroupsHelpLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            caption = "";
            tip = "Collision groups defined for this link.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMListBoxCollisionGroups = (PropertyManagerPageListbox)PMComponentsGroup.AddControl2(
                CollisionGroupsListBoxID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMListBoxCollisionGroups.Height = 50;

            // SelectionBox sits directly under the listbox so the visual flow
            // is "pick a row -> edit its components below". The bottom half of
            // the editor (name label / textbox / Add / Remove) handles the
            // separate workflow of creating or removing groups.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            caption = "Components for the highlighted collision group";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSelectionCollision = (PropertyManagerPageSelectionbox)PMComponentsGroup.AddControl2(
                SelectionCollisionID, (short)controlType, caption, (short)alignment, (int)options,
                "Components belonging to the collision group selected above.");
            PMSelectionCollision.AllowSelectInMultipleBoxes = true;
            PMSelectionCollision.SingleEntityOnly = false;
            PMSelectionCollision.AllowMultipleSelectOfSameEntity = false;
            PMSelectionCollision.Height = 40;
            PMSelectionCollision.SetSelectionFilters(filterObj);
            PMSelectionCollision.Mark = CollisionSelectionMark;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Group name (for new group)";
            tip = "Used as the new group's display name and as the suffix on its mesh filename.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelCollisionGroupsName = (PropertyManagerPageLabel)PMComponentsGroup.AddControl2(
                CollisionGroupsNameLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            caption = "";
            tip = "Group name for the next group to add.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTextBoxCollisionGroupName = (PropertyManagerPageTextbox)PMComponentsGroup.AddControl2(
                CollisionGroupsNameTextBoxID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Add Collision Group";
            tip = "Save the current selection into the highlighted group, then create a new empty group.";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonCollisionGroupAdd = (PropertyManagerPageButton)PMComponentsGroup.AddControl2(
                CollisionGroupsAddButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Remove Selected Collision Group";
            tip = "Delete the highlighted collision group from this link.";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonCollisionGroupRemove = (PropertyManagerPageButton)PMComponentsGroup.AddControl2(
                CollisionGroupsRemoveButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // --- Inertial components (only used when Inertial Source = Custom) ---
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Inertial Components (used when source = Custom)";
            tip = "Optional. When Inertial Source is Custom, mass and inertia are computed from these components.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelInertialComponents = (PropertyManagerPageLabel)PMComponentsGroup.AddControl2(
                LabelInertialID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            caption = "Inertial Components";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSelectionInertial = (PropertyManagerPageSelectionbox)PMComponentsGroup.AddControl2(
                SelectionInertialID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMSelectionInertial.AllowSelectInMultipleBoxes = true;
            PMSelectionInertial.SingleEntityOnly = false;
            PMSelectionInertial.AllowMultipleSelectOfSameEntity = false;
            PMSelectionInertial.Height = 40;
            PMSelectionInertial.SetSelectionFilters(filterObj);
            PMSelectionInertial.Mark = InertialSelectionMark;

            // === Sub-section "Sites (MJCF)" ===
            // Layout mirrors the Visual / Collision groups editor:
            //   help -> sites listbox + label -> name input -> coord-system combo
            //   -> Add | Remove buttons (side-by-side).
            // MJCF-only; ignored by the URDF writer.
            caption = "Sites (MJCF)";
            options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible;
            PMSitesGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                SitesGroupID, caption, (int)options);

            // Help label so users know the box is not a selection target like
            // the visual / collision selection boxes above.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Type a site name, pick a reference coord. system, then click Add Site.";
            tip = "Sites are MJCF-only frames attached to a body. They are ignored when exporting URDF.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSitesGroup.AddControl2(
                SitesHelpLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Sites listbox + its header label, placed directly under the help
            // label so this section reads top-down like the Groups editor.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Sites defined for this link";
            tip = "Read-only summary. Use Remove Selected Site to delete one.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSitesGroup.AddControl2(
                SitesListLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            caption = "";
            tip = "Sites already added to this link. Select one and click Remove Selected Site to delete it.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMListBoxSites = (PropertyManagerPageListbox)PMSitesGroup.AddControl2(
                SitesListBoxID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMListBoxSites.Height = 50;

            // Site name label + textbox.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Site name";
            tip = "Identifier that will appear as <site name=...> in the MJCF file.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSitesGroup.AddControl2(
                SitesNameLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Textbox;
            caption = "";
            tip = "Site name (will appear as <site name=...>)";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTextBoxSiteName = (PropertyManagerPageTextbox)PMSitesGroup.AddControl2(
                SitesNameTextBoxID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Site coord-system combobox.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = "Site coord. system";
            tip = "Reference coordinate system that defines the site's pose relative to the parent body";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMComboBoxSiteCoordSys = (PropertyManagerPageCombobox)PMSitesGroup.AddControl2(
                SitesCoordSysComboID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxSiteCoordSys.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxSiteCoordSys.Height = 18;

            // Add | Remove site buttons, laid out side-by-side at the bottom of
            // the Sites editor.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Add Site";
            tip = "Add the entered site to this link";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonSiteAdd = (PropertyManagerPageButton)PMSitesGroup.AddControl2(
                SitesAddButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Remove Selected Site";
            tip = "Remove the selected site from the list";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonSiteRemove = (PropertyManagerPageButton)PMSitesGroup.AddControl2(
                SitesRemoveButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // === Tree object setup (deferred to the end) ===
            // Wired up here so the first TreeAfterSelect -> FillPropertyManager
            // call sees fully-constructed PMComboBox / PMListBox / PMSelection
            // controls. The host PMTree control was created at the top of the
            // method as a top-level page control.
            Tree = new TreeView
            {
                Height = 163,
                Visible = true
            };

            Tree.AfterSelect += new TreeViewEventHandler(TreeAfterSelect);
            Tree.NodeMouseClick += new TreeNodeMouseClickEventHandler(TreeNodeMouseClick);
            Tree.KeyDown += new KeyEventHandler(TreeKeyDown);
            Tree.DragDrop += new DragEventHandler(TreeDragDrop);
            Tree.DragOver += new DragEventHandler(TreeDragOver);
            Tree.DragEnter += new DragEventHandler(TreeDragEnter);
            Tree.ItemDrag += new ItemDragEventHandler(TreeItemDrag);
            Tree.AllowDrop = true;
            PMTree.SetWindowHandlex64(Tree.Handle.ToInt64());

            ToolStripMenuItem addChild = new ToolStripMenuItem();
            ToolStripMenuItem removeChild = new ToolStripMenuItem();
            addChild.Text = "Add Child Link";
            addChild.Click += new EventHandler(AddChildClick);

            removeChild.Text = "Remove";
            removeChild.Click += new EventHandler(RemoveChildClick);
            docMenu.Items.AddRange(new ToolStripMenuItem[] { addChild, removeChild });
            LinkNode node = CreateEmptyNode(null);
            node.ContextMenuStrip = docMenu;
            Tree.Nodes.Add(node);
            Tree.SelectedNode = Tree.Nodes[0];
            PMSelectionVisual.SetSelectionFocus();
            PMPage.SetFocus(dotNetTree);
        }

        // Toggle the visibility of every control in the Collision Groups
        // editor. Used by OnCheckboxCheck to hide the editor when the user
        // checks "Use visual groups as collision". Both Visible and Enabled are
        // flipped together so a hidden control is also non-interactive.
        private void SetCollisionEditorVisible(bool visible)
        {
            object[] collisionEditorControls = new object[]
            {
                PMLabelCollisionComponents,
                PMLabelCollisionGroupsHelp,
                PMListBoxCollisionGroups,
                PMLabelCollisionGroupsName,
                PMTextBoxCollisionGroupName,
                PMSelectionCollision,
                PMButtonCollisionGroupAdd,
                PMButtonCollisionGroupRemove,
            };
            foreach (object ctl in collisionEditorControls)
            {
                IPropertyManagerPageControl pageControl = ctl as IPropertyManagerPageControl;
                if (pageControl != null)
                {
                    pageControl.Visible = visible;
                    pageControl.Enabled = visible;
                }
            }
        }

        #region Not implemented handler methods

        // These methods are still active. The exceptions that are thrown only cause the debugger
        // to pause. Comment out the exception if you choose not to implement it, but it gets
        // regularly called anyway
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked)
        {
            if (Id == CheckCollisionUsesVisualID)
            {
                SetCollisionEditorVisible(!Checked);

                // Persist the toggle on the active node so a later save round-
                // trip captures it. SaveActiveNode is also called when the
                // user navigates away, but flipping this flag immediately keeps
                // the data model in sync with the UI for any code path that
                // peeks at node.Link.CollisionUsesVisual before the next save.
                LinkNode active = (LinkNode)Tree?.SelectedNode;
                if (active != null)
                {
                    active.Link.CollisionUsesVisual = Checked;
                }
                return;
            }

            logger.Info("OnCheckboxCheck called for Id=" + Id + ". No special handler registered.");
        }

        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text)
        {
            logger.Info("OnComboboxEditChanged called. This method no longer throws an Exception." +
                " It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            // The axis combobox (PMComboBoxAxes) and the joint coord-sys combobox
            // (PMComboBoxCoordSys) are both registered with ComboBoxCoordSysID
            // (along with PMComboBoxJointType - a pre-existing ID-sharing oddity
            // in this file). Any selection change in that group should refresh
            // the overlay arrow; the helper re-reads both combos fresh so it
            // doesn't matter which one fired the event. Joint-type changes
            // also fire here and are a no-op for the overlay (no axis change),
            // so the extra refresh is harmless.
            if (Id == ComboBoxCoordSysID)
            {
                RefreshAxisDirectionPreview();
            }
        }

        void IPropertyManagerPage2Handler9.OnGroupCheck(int Id, bool Checked)
        {
            logger.Info("OnGroupCheck called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnGroupExpand(int Id, bool Expanded)
        {
            logger.Info("OnGroupExpand called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item)
        {
            try
            {
                LinkNode node = (LinkNode)Tree.SelectedNode;
                if (node == null)
                {
                    return;
                }
                if (Id == VisualGroupsListBoxID)
                {
                    // Save the previous group's selection before switching.
                    CommitActiveVisualGroupSelection(node);
                    if (Item >= 0 && Item < (node.Link.VisualGroups != null ? node.Link.VisualGroups.Count : 0))
                    {
                        activeVisualGroupIndex = Item;
                        LoadActiveVisualGroupIntoSelectionBox(node);
                        RefreshVisualGroupsListbox(node);
                    }
                }
                else if (Id == CollisionGroupsListBoxID)
                {
                    CommitActiveCollisionGroupSelection(node);
                    if (Item >= 0 && Item < (node.Link.CollisionGroups != null ? node.Link.CollisionGroups.Count : 0))
                    {
                        activeCollisionGroupIndex = Item;
                        LoadActiveCollisionGroupIntoSelectionBox(node);
                        RefreshCollisionGroupsListbox(node);
                    }
                }
            }
            catch (Exception e)
            {
                logger.Error("Exception caught handling listbox selection change " + Id, e);
            }
        }

        bool IPropertyManagerPage2Handler9.OnNextPage()
        {
            logger.Info("OnNextPage called. This method no longer throws an Exception. It just " + "" +
                "silently does nothing. Ok, except for this logging message");
            return true;
        }

        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id)
        {
            logger.Info("OnOptionCheck called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItem(int Id)
        {
            logger.Info("OnPopupMenuItem called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnPopupMenuItemUpdate(int Id, ref int retval)
        {
            logger.Info("OnPopupMenuItemUpdate called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        bool IPropertyManagerPage2Handler9.OnPreview()
        {
            logger.Info("OnPreview called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
            return true;
        }

        bool IPropertyManagerPage2Handler9.OnPreviousPage()
        {
            logger.Info("OnPreviousPage called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
            return true;
        }

        void IPropertyManagerPage2Handler9.OnRedo()
        {
            logger.Info("OnRedo called. This method no longer throws an Exception. " +
                "It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id)
        {
            logger.Info("OnSelectionboxCalloutCreated called. This method no longer throws " +
                " an Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id)
        {
            logger.Info("OnSelectionboxCalloutDestroyed called. This method no longer throws " +
                "an Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSliderPositionChanged(int Id, double Value)
        {
            logger.Info("OnSliderPositionChanged called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnSliderTrackingCompleted(int Id, double Value)
        {
            logger.Info("OnSliderTrackingCompleted called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id)
        {
            logger.Info("OnTabClicked called. This method no longer throws an Exception. It " +
                " just silently does nothing. Ok, except for this logging message");
            return true;
        }

        void IPropertyManagerPage2Handler9.OnUndo()
        {
            logger.Info("OnUndo called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnWhatsNew()
        {
            logger.Info("OnWhatsNew called. This method no longer throws an Exception. It just " +
                " silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnListboxRMBUp(int Id, int PosX, int PosY)
        {
            logger.Info("OnListboxRMBUp called. This method no longer throws an Exception. It " +
                " just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value)
        {
            logger.Info("OnNumberBoxTrackingCompleted called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.AfterClose()
        {
            logger.Info("AfterClose called. This method no longer throws an Exception. It just " +
                "silently does nothing. Ok, except for this logging message");
        }

        int IPropertyManagerPage2Handler9.OnActiveXControlCreated(int Id, bool Status)
        {
            logger.Info("OnActiveXControlCreated called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
            return 0;
        }

        #endregion Not implemented handler methods
    }
}