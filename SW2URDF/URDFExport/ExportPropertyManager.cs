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
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();
        public SldWorks swApp;
        public ModelDoc2 ActiveSWModel;

        public ExportHelper Exporter;
        public LinkNode previouslySelectedNode;
        public Link previouslySelectedLink;
        public List<Link> linksToVisit;
        public LinkNode rightClickedNode;
        private readonly ContextMenuStrip docMenu;

        //General objects required for the PropertyManager page

        private readonly PropertyManagerPage2 PMPage;
        private PropertyManagerPageGroup PMGroup;
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

        // Sites sub-section: a small inline editor on the per-link page.
        private PropertyManagerPageGroup PMSitesGroup;
        private PropertyManagerPageListbox PMListBoxSites;
        private PropertyManagerPageTextbox PMTextBoxSiteName;
        private PropertyManagerPageCombobox PMComboBoxSiteCoordSys;
        private PropertyManagerPageButton PMButtonSiteAdd;
        private PropertyManagerPageButton PMButtonSiteRemove;

        // Import / Export action group lives below the Sites group so that the
        // "Load Configuration..." and "Preview and Export..." buttons are at the
        // bottom of the side-bar where users naturally finish their workflow.
        private PropertyManagerPageGroup PMActionsGroup;

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
        private const int ActionsGroupID = 49;

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

                default:
                    break;
            }
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

        //A method that sets up the Property Manager Page
        private void SetupPropertyManagerPage(ref string caption, ref string tip,
            ref long options, ref int controlType, ref int alignment)
        {
            //Begin adding the controls to the page
            //Create the group box
            caption = "Configure and Organize Links";
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

            // === Component selection: Visual / Collision / Inertial ===

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
            PMLabelInertialSource = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelInertialSourceID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Combobox;
            caption = "Inertial Source";
            tip = "Visual: use visual components. Collision: use collision components. Custom: use the inertial components box below.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMComboBoxInertialSource = (PropertyManagerPageCombobox)PMGroup.AddControl2(
                ComboInertialSourceID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComboBoxInertialSource.Style =
                (int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
            PMComboBoxInertialSource.AddItems(new string[] {
                "Visual",
                "Collision",
                "Custom (Inertial Components)" });
            PMComboBoxInertialSource.CurrentSelection = 0;

            // --- Visual components ---
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Visual Components";
            tip = "Select components used to build the visual mesh for this link/body";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelVisualComponents = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelVisualID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            caption = "Visual Components";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSelectionVisual = (PropertyManagerPageSelectionbox)PMGroup.AddControl2(
                SelectionVisualID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMSelectionVisual.AllowSelectInMultipleBoxes = true;
            PMSelectionVisual.SingleEntityOnly = false;
            PMSelectionVisual.AllowMultipleSelectOfSameEntity = false;
            PMSelectionVisual.Height = 40;
            PMSelectionVisual.SetSelectionFilters(filterObj);
            PMSelectionVisual.Mark = VisualSelectionMark;

            // --- Collision components ---
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Collision Components";
            tip = "Select components used to build the collision mesh. Empty re-uses the visual mesh.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelCollisionComponents = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelCollisionID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            caption = "Collision Components";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSelectionCollision = (PropertyManagerPageSelectionbox)PMGroup.AddControl2(
                SelectionCollisionID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMSelectionCollision.AllowSelectInMultipleBoxes = true;
            PMSelectionCollision.SingleEntityOnly = false;
            PMSelectionCollision.AllowMultipleSelectOfSameEntity = false;
            PMSelectionCollision.Height = 40;
            PMSelectionCollision.SetSelectionFilters(filterObj);
            PMSelectionCollision.Mark = CollisionSelectionMark;

            // --- Inertial components (only used when Inertial Source = Custom) ---
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Inertial Components (used when source = Custom)";
            tip = "Optional. When Inertial Source is Custom, mass and inertia are computed from these components.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMLabelInertialComponents = (PropertyManagerPageLabel)PMGroup.AddControl2(
                LabelInertialID, (short)controlType, caption, (short)alignment, (int)options, tip);

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Selectionbox;
            caption = "Inertial Components";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSelectionInertial = (PropertyManagerPageSelectionbox)PMGroup.AddControl2(
                SelectionInertialID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMSelectionInertial.AllowSelectInMultipleBoxes = true;
            PMSelectionInertial.SingleEntityOnly = false;
            PMSelectionInertial.AllowMultipleSelectOfSameEntity = false;
            PMSelectionInertial.Height = 40;
            PMSelectionInertial.SetSelectionFilters(filterObj);
            PMSelectionInertial.Mark = InertialSelectionMark;

            // === Sites sub-section (MJCF only; ignored by URDF writer) ===
            // Workflow is: type a name -> pick a reference coord system -> click
            // Add Site. The list at the bottom shows what has already been added.
            caption = "Sites (MJCF)";
            options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible;
            PMSitesGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                SitesGroupID, caption, (int)options);

            // Help label so users know the box is not a selection target like the
            // visual / collision selection boxes above.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Type a site name, pick a reference coord. system, then click Add Site.";
            tip = "Sites are MJCF-only frames attached to a body. They are ignored when exporting URDF.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSitesGroup.AddControl2(
                SitesHelpLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

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

            // Add site button.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Add Site";
            tip = "Add the entered site to this link";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonSiteAdd = (PropertyManagerPageButton)PMSitesGroup.AddControl2(
                SitesAddButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Label above the read-only listing of already-added sites.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Sites defined for this link";
            tip = "Read-only summary. Use Remove Selected Site to delete one.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMSitesGroup.AddControl2(
                SitesListLabelID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // List of existing sites (display + selection target for the Remove
            // button below). Not a SolidWorks SelectionBox; the user does not click
            // into the SolidWorks tree from here.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Listbox;
            caption = "";
            tip = "Sites already added to this link. Select one and click Remove Selected Site to delete it.";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMListBoxSites = (PropertyManagerPageListbox)PMSitesGroup.AddControl2(
                SitesListBoxID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMListBoxSites.Height = 50;

            // Remove site button.
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Remove Selected Site";
            tip = "Remove the selected site from the list";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonSiteRemove = (PropertyManagerPageButton)PMSitesGroup.AddControl2(
                SitesRemoveButtonID, (short)controlType, caption, (short)alignment, (int)options, tip);

            //Create the number box label
            //Create the link name text box label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Number of child links";
            tip = "Enter the number of child links and they will be automatically added";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            
            //Create the number box
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

            // === Import / Export action group ===
            // Sits below the Sites group so the workflow flows top-down: first
            // configure the link tree, then sites, and finally import / export.
            caption = "Import / Export";
            options = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;
            PMActionsGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                ActionsGroupID, caption, (int)options);

            // Load Configuration button
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Button;
            caption = "Load Configuration...";
            tip = "Import values from a CSV file";
            alignment = 0;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonLoad = PMActionsGroup.AddControl2(
                LoadConfigurationID, (short)controlType, caption, (short)alignment, (int)options, tip);
            (PMButtonLoad as IPropertyManagerPageControl).Width = 200;

            // Loaded CSV Filename label
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Label;
            caption = "Imported File: ";
            tip = "";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = 0;
            PMLabelCSVFilename = PMActionsGroup.AddControl2(
                LoadedCSVFilenameID, (short)controlType, caption, (short)alignment, (int)options, tip);

            // Create Check Boxes to select whether to recompute values
            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = "Compute Mass and Inertia";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = "External values have been loaded. Check this box to recompute the Mass and Inertia values";
            options = 0;
            PMComputeMassInertia = PMActionsGroup.AddControl2(
                ComputeMassInertiaID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeMassInertia.Checked = true;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = "Compute Visual and Collision";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = "External values have been loaded. Check this box to recompute the visual and collision values";
            options = 0;
            PMComputeVisualCollision = PMActionsGroup.AddControl2(
                ComputeVisualCollisionID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeVisualCollision.Checked = true;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = "Compute Joint Kinematics";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = "External values have been loaded. Check this box to recompute the joint kinematics";
            options = 0;
            PMComputeJointKinematics = PMActionsGroup.AddControl2(
                ComputeJointKinematicsID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeJointKinematics.Checked = true;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_Checkbox;
            caption = "Compute Joint Limits";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            tip = "External values have been loaded. Check this box to recompute the joint limits";
            options = 0;
            PMComputeJointLimits = PMActionsGroup.AddControl2(
                ComputeJointLimitsID, (short)controlType, caption, (short)alignment, (int)options, tip);
            PMComputeJointLimits.Checked = true;

            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMButtonExport = PMActionsGroup.AddControl2(ButtonExportID,
                (short)swPropertyManagerPageControlType_e.swControlType_Button,
                "Preview and Export...", 0, (int)options,
                "Preview the generated description and export the package");
            (PMButtonExport as IPropertyManagerPageControl).Width = 200;

            controlType = (int)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle;
            caption = "Link Tree";
            alignment = (int)swPropertyManagerPageControlLeftAlign_e.swControlAlign_LeftEdge;
            options = (int)swAddControlOptions_e.swControlOptions_Visible +
                (int)swAddControlOptions_e.swControlOptions_Enabled;
            PMTree = PMPage.AddControl2(dotNetTree,
                (short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle, caption, 0, (int)options, "");
            PMTree.Height = 163;
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
            //ToolStripMenuItem renameChild = new ToolStripMenuItem();
            addChild.Text = "Add Child Link";
            addChild.Click += new EventHandler(AddChildClick);

            removeChild.Text = "Remove";
            removeChild.Click += new EventHandler(RemoveChildClick);
            //renameChild.Text = "Rename";
            //renameChild.Click += new System.EventHandler(this.renameChild_Click);
            //docMenu.Items.AddRange(new ToolStripMenuItem[] { addChild, removeChild, renameChild });
            docMenu.Items.AddRange(new ToolStripMenuItem[] { addChild, removeChild });
            LinkNode node = CreateEmptyNode(null);
            node.ContextMenuStrip = docMenu;
            Tree.Nodes.Add(node);
            Tree.SelectedNode = Tree.Nodes[0];
            PMSelectionVisual.SetSelectionFocus();
            PMPage.SetFocus(dotNetTree);
        }

        #region Not implemented handler methods

        // These methods are still active. The exceptions that are thrown only cause the debugger
        // to pause. Comment out the exception if you choose not to implement it, but it gets
        // regularly called anyway
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked)
        {
            logger.Info("OnCheckboxCheck called. This method no longer throws an Exception. " +
                " It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text)
        {
            logger.Info("OnComboboxEditChanged called. This method no longer throws an Exception." +
                " It just silently does nothing. Ok, except for this logging message");
        }

        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item)
        {
            logger.Info("OnComboboxSelectionChanged called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
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
            logger.Info("OnListboxSelectionChanged called. This method no longer throws an " +
                "Exception. It just silently does nothing. Ok, except for this logging message");
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