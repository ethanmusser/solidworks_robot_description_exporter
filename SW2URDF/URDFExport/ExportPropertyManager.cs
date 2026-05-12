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
using SW2URDF.URDF;
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
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();
        public SldWorks swApp;
        public ModelDoc2 ActiveSWModel;

        // SOLIDWORKS exposes this class to COM via the IPropertyManagerPage2Handler
        // interface, NOT via .NET (de)serialization. Configuration persistence runs
        // through the DataContract path on Link / Joint, not through this handler.
        // Earlier revisions carried a [Serializable] attribute and per-field
        // [NonSerialized] annotations purely to silence the now-retired
        // FxCopAnalyzers CA2235 warning; both are gone with the analyzer removal.
        public ExportHelper Exporter;
        public LinkNode previouslySelectedNode;
        public LinkNode rightClickedNode;
        private readonly ContextMenuStrip docMenu;
        private bool disposed;

        //General objects required for the PropertyManager page

        private readonly PropertyManagerPage2 PMPage;
        // The PropertyManagerPage is organized as a fixed always-visible
        // header (Preview/Export button + status label + link tree +
        // child-count spinner) followed by six tabs. Builders attach
        // controls directly to the relevant tab; the per-tab single-group
        // wrapper layer was retired because it added a redundant
        // collapse / expand affordance with no organizational value.
        private PropertyManagerPageTab PMSetupTab;
        private PropertyManagerPageTab PMLinkJointTab;
        private PropertyManagerPageTab PMVisualTab;
        private PropertyManagerPageTab PMCollisionTab;
        private PropertyManagerPageTab PMInertialTab;
        private PropertyManagerPageTab PMSitesTab;
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
        private PropertyManagerPageLabel PMLabelValidationStatus;
        private PropertyManagerPageTextbox PMTextBoxLinkName;
        private PropertyManagerPageTextbox PMTextBoxJointName;
        private PropertyManagerPageNumberbox PMNumberBoxChildCount;
        // Joint type is the only remaining read-only combobox on the Link
        // & Joint tab; coordinate-system / axis pickers are SelectionBox-
        // only now (the user picks features in the SW tree directly,
        // commits via OnSelectionboxListChanged, and SelectByID2 on
        // FillPropertyManager / OnTabClicked rehydrates the box).
        private PropertyManagerPageCombobox PMComboBoxJointType;
        private PropertyManagerPageSelectionbox PMSelectionGlobalCoordsys;
        private PropertyManagerPageSelectionbox PMSelectionJointCoordsys;
        private PropertyManagerPageSelectionbox PMSelectionJointAxis;
        // "Auto-derive axis from kinematic chain" toggle. When checked,
        // the joint axis SelectionBox is disabled and AxisName is
        // ignored at export time (CreateJoint /
        // EstimateGlobalJointFromComponents resolve the axis from the
        // SW mates instead). Mirrored to Joint.AutoDeriveAxis on every
        // toggle and on SaveActiveNode.
        private PropertyManagerPageCheckbox PMCheckAutoDeriveAxis;

        // Export-time choices that used to live on the now-retired
        // AssemblyExportForm. They sit on the Setup tab and read together
        // as "what should the next export do".
        private PropertyManagerPageCombobox PMComboBoxOutputFormat;
        private PropertyManagerPageCombobox PMComboBoxMeshFormat;
        private PropertyManagerPageCheckbox PMCheckExportMeshes;

        // Per-joint properties (Limits / Dynamics / Reference / Armature) and
        // the per-joint "auto-compute lower/upper from limit mate" toggle.
        // Round-trip on link switch via FillPropertyManager / SaveActiveNode.
        private PropertyManagerPageCheckbox PMCheckAutoComputeLimits;
        private PropertyManagerPageTextbox PMTextBoxJointLower;
        private PropertyManagerPageTextbox PMTextBoxJointUpper;
        private PropertyManagerPageTextbox PMTextBoxJointEffort;
        private PropertyManagerPageTextbox PMTextBoxJointVelocity;
        private PropertyManagerPageTextbox PMTextBoxJointDamping;
        private PropertyManagerPageTextbox PMTextBoxJointFriction;
        private PropertyManagerPageTextbox PMTextBoxJointArmature;
        private PropertyManagerPageTextbox PMTextBoxJointReference;
        private PropertyManagerPageLabel PMLabelJointProperties;
        private PropertyManagerPageLabel PMLabelJointLower;
        private PropertyManagerPageLabel PMLabelJointUpper;
        private PropertyManagerPageLabel PMLabelJointEffort;
        private PropertyManagerPageLabel PMLabelJointVelocity;
        private PropertyManagerPageLabel PMLabelJointDamping;
        private PropertyManagerPageLabel PMLabelJointFriction;
        private PropertyManagerPageLabel PMLabelJointArmature;
        private PropertyManagerPageLabel PMLabelJointReference;

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
        // are captured so SetCollisionEditorEnabled can grey out the whole
        // collision editor when the user toggles "Use visual groups as
        // collision".
        private PropertyManagerPageListbox PMListBoxCollisionGroups;
        private PropertyManagerPageTextbox PMTextBoxCollisionGroupName;
        private PropertyManagerPageButton PMButtonCollisionGroupAdd;
        private PropertyManagerPageButton PMButtonCollisionGroupRemove;
        private PropertyManagerPageLabel PMLabelCollisionGroupsHelp;
        private PropertyManagerPageLabel PMLabelCollisionGroupsName;

        // "Use visual groups as collision" toggle. When checked, the collision
        // editor below it is greyed out (Enabled=false but still rendered)
        // and the export pipeline reuses the visual meshes for collision.
        // We deliberately do NOT also flip Visible: the prior implementation
        // toggled both, which produced inconsistent UX between the two
        // entry paths (FillPropertyManager loading a config with the box
        // pre-checked rendered the controls "greyed but visible", whereas
        // the user clicking the checkbox at runtime made them disappear).
        // Enabled-only toggling keeps the layout stable and matches the
        // SW idiom for "this section is inactive but still discoverable".
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

        // Monotonic counter used to label every diagnostic log line on the
        // axis-preview / SelectionBox loop suspects. Without a per-event
        // sequence number the log is hard to read because OnSelectionbox
        // ListChanged + DeferRefreshAxisPreview + RefreshAxisDirectionPreview
        // + DrawAxisOverlay all interleave on the same UI thread; the
        // counter makes it possible to pair entry/exit lines and spot
        // duplicate fires. Diagnostic only - has no functional effect.
        // Safe across threads in this codebase: every site that bumps it
        // runs on the WinForms UI thread, so a plain int suffices.
        private int axisPreviewLogSeq;

        // Re-entrancy guard for the deferred axis-preview refresh. Set true
        // when DeferRefreshAxisPreview successfully queues a Tree.BeginInvoke,
        // cleared when RefreshAxisDirectionPreview finishes running.
        //
        // WHY: DrawAxisOverlay's `Manipulator.Show(ActiveSWModel)` empirically
        // perturbs SW's selection state (likely deselect+reselect of the
        // entity in the focused feature SelectionBox), which fires
        // OnSelectionboxListChanged with the still-present pick. Our handler
        // re-commits the (unchanged) Joint.CoordinateSystemName / AxisName
        // and calls DeferRefreshAxisPreview again. Without this guard each
        // queued refresh draws another manipulator, fires another box event,
        // queues another refresh - an unbounded busy loop. The slow
        // location-aware log4net pattern (`%filename: %line` walks the
        // managed stack on every Info call - see Logger.cs comment about
        // "this ConversionPattern is slow") amplifies the loop into what
        // looks like a hard hang rather than a runaway loop. The guard
        // collapses any in-flight + already-queued refresh into a single
        // pending one; the eventual run sees the latest persisted state
        // (the CoordinateSystemName / AxisName / AxisFlipped fields are
        // already up to date by the time the queue drains).
        private bool axisPreviewRefreshPending;

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

        // ID of the tab that is currently active in the PMP. Drives the
        // "viewer highlight follows the active tab" behavior: when this
        // is e.g. VisualTabID, only the Visual SelectionBox's mark is
        // populated and all other marks are cleared, so the SOLIDWORKS
        // viewer highlights ONLY the components of the active visual
        // group (not the union of every group ever loaded by
        // FillPropertyManager). OnTabClicked updates this on every tab
        // change; FillPropertyManager reads it when the active link
        // changes, so a link switch repopulates only the marks the
        // currently-visible tab needs. Defaults to SetupTabID because
        // SW shows the first-added tab (Setup) on initial PMP open and
        // does not fire OnTabClicked for it.
        private int currentActiveTabId = SetupTabID;

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
        // The site coord-sys is picked through PMSelectionSiteCoordSys
        // (SelectionBox-only); AddSiteFromForm reads the marked
        // SelectionMgr entry at click time, so there is no read-only
        // echo combobox.
        private PropertyManagerPageListbox PMListBoxSites;
        private PropertyManagerPageTextbox PMTextBoxSiteName;
        private PropertyManagerPageSelectionbox PMSelectionSiteCoordSys;
        private PropertyManagerPageButton PMButtonSiteAdd;
        private PropertyManagerPageButton PMButtonSiteRemove;

        private PropertyManagerPageLabel PMLabelJointName;
        private PropertyManagerPageLabel PMLabelParentLink;
        private PropertyManagerPageLabel PMLabelAxes;
        private PropertyManagerPageLabel PMLabelCoordSys;
        private PropertyManagerPageLabel PMLabelJointType;
        private PropertyManagerPageLabel PMLabelGlobalCoordsys;
        private PropertyManagerPageLabel PMLabelInertialSource;
        private PropertyManagerPageLabel PMLabelVisualComponents;
        private PropertyManagerPageLabel PMLabelCollisionComponents;
        private PropertyManagerPageLabel PMLabelInertialComponents;

        // World attachment combobox (Welded / Free) on the Link/Joint tab.
        // Only enabled when the active node is a top-level body (immediate
        // child of the WorldNode). Welded -> body is rigidly fixed to the
        // world; Free -> MJCF emits a <freejoint/> on the body. URDF
        // ignores this field (the first top-level body is always written
        // as a fixed-base base_link, with a warning if Free was selected).
        private PropertyManagerPageLabel PMLabelWorldAttachment;
        private PropertyManagerPageCombobox PMComboBoxWorldAttachment;

        private PropertyManagerPageWindowFromHandle PMTree;

        public TreeView Tree
        { get; set; }

        private bool automaticallySwitched = false;

        //Each object in the page needs a unique ID

        // CRITICAL: every control attached to PMPage / a PMPage tab MUST
        // have a UNIQUE ID across the ENTIRE page. SolidWorks happily
        // accepts duplicate IDs at AddControl2 time but renders the
        // duplicate elsewhere on the page (e.g. an ID-2 textbox added on
        // the Link/Joint tab will leak onto the Visual / Collision /
        // Inertial tabs that share that ID). The constants below are
        // grouped by tab purely for readability; uniqueness is what
        // matters.
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
        private const int LabelAxesID = 20;
        private const int LabelCoordSysID = 21;
        private const int IDLabelGlobalCoordsys = 25;
        private const int LabelChildCountID = 26;
        private const int OutputFormatComboID = 31;
        private const int MeshFormatComboID = 32;
        private const int ExportMeshesCheckID = 33;
        private const int LabelOutputFormatID = 34;
        private const int LabelMeshFormatID = 35;
        private const int SitesListBoxID = 41;
        private const int SitesNameTextBoxID = 42;
        private const int SitesAddButtonID = 44;
        private const int SitesRemoveButtonID = 45;
        private const int SitesHelpLabelID = 46;
        private const int SitesNameLabelID = 47;
        private const int SitesListLabelID = 48;

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
        private const int SetupTabID = 90;
        private const int LinkJointTabID = 91;
        private const int VisualTabID = 92;
        private const int CollisionTabID = 93;
        private const int InertialTabID = 94;
        private const int SitesTabID = 95;
        private const int SelectionGlobalCoordsysID = 100;
        private const int SelectionJointCoordsysID = 101;
        private const int SelectionJointAxisID = 102;
        private const int SelectionSiteCoordSysID = 103;
        private const int ValidationStatusLabelID = 104;

        // Tab-local labels added to Visual / Collision / Inertial / Sites
        // tabs to preserve the visual heading after the per-tab single-group
        // wrapper was retired.
        private const int LabelVisualHeaderID = 110;
        private const int LabelCollisionHeaderID = 111;
        private const int LabelInertialHeaderID = 112;
        private const int LabelSitesHeaderID = 113;

        // Joint Properties section on the Link/Joint tab. One ID per
        // editable field (Lower/Upper/Effort/Velocity/Damping/Friction/
        // Armature/Reference) plus per-field labels and the auto-compute
        // toggle.
        private const int LabelJointPropertiesID = 120;
        private const int CheckAutoComputeLimitsID = 121;
        private const int LabelJointLowerID = 122;
        private const int TextBoxJointLowerID = 123;
        private const int LabelJointUpperID = 124;
        private const int TextBoxJointUpperID = 125;
        private const int LabelJointEffortID = 126;
        private const int TextBoxJointEffortID = 127;
        private const int LabelJointVelocityID = 128;
        private const int TextBoxJointVelocityID = 129;
        private const int LabelJointDampingID = 130;
        private const int TextBoxJointDampingID = 131;
        private const int LabelJointFrictionID = 132;
        private const int TextBoxJointFrictionID = 133;
        private const int LabelJointArmatureID = 134;
        private const int TextBoxJointArmatureID = 135;
        private const int LabelJointReferenceID = 136;
        private const int TextBoxJointReferenceID = 137;

        // Link/Joint tab: ID slots that used to share the obsolete
        // ComboBoxCoordSysID and LabelAxesID with other controls. Each
        // control now gets its own unique ID to avoid the cross-tab
        // leakage that duplicate IDs cause.
        private const int TextBoxJointNameID = 140;
        private const int LabelJointTypeID = 141;
        private const int ComboBoxJointTypeID = 142;
        private const int CheckAutoDeriveAxisID = 143;
        private const int LabelLinkNameStaticID = 144;
        private const int LabelVisualComponentsHeaderID = 145;
        private const int LabelCollisionComponentsHeaderID = 146;
        private const int LabelSiteCoordSysHeaderID = 147;
        private const int LabelActiveLinkTreeID = 148;
        private const int LabelWorldAttachmentID = 149;
        private const int ComboBoxWorldAttachmentID = 150;

        // Marks for every PMP SelectionBox so SOLIDWORKS can attribute the
        // user's selection to the right list. CRITICAL:
        // IPropertyManagerPageSelectionbox.Mark is a BITMASK, not an
        // arbitrary integer. SW dispatches a pick to every box whose Mark
        // shares ANY bit with the pick's mark, so marks MUST be unique
        // powers of 2 (1, 2, 4, 8, 16, ...) - one bit per box. Using
        // multi-bit values (we shipped 11, 12, 13, 21-24 briefly) caused
        // the picked entity to render in every sibling box whose mask
        // overlapped, AND caused programmatic loaders that wrote to one
        // box's mark to be cross-cleared by sibling DeselectAllAtMark
        // calls that scoped to an overlapping bit. See
        // https://codestack.net/labs/solidworks/swex/pmpage/controls/selection-box
        // and the official SW C# multi-select sample
        // (Select_Multiple_Objects_for_Selection_Boxes_Example_CSharp.htm)
        // which uses mark = 1 / mark2 = 2 for two adjacent boxes. -1 is
        // SW's "no mark" sentinel and must never be assigned to a box.
        // Bits currently consumed (0..6); a future agent adding an eighth
        // SelectionBox should claim bit 7 (= 128) and so on.
        private const int VisualSelectionMark = 1 << 0;          // 1
        private const int CollisionSelectionMark = 1 << 1;       // 2
        private const int InertialSelectionMark = 1 << 2;        // 4
        private const int GlobalCoordSysSelectionMark = 1 << 3;  // 8
        private const int JointCoordSysSelectionMark = 1 << 4;   // 16
        private const int JointAxisSelectionMark = 1 << 5;       // 32
        private const int SiteCoordSysSelectionMark = 1 << 6;    // 64

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
            Exporter.AxisOverlayDirectionFlipped = OnAxisOverlayDirectionFlipped;
            Exporter.URDFRobot = new Robot();
            Exporter.URDFRobot.Name = ActiveSWModel.GetTitle();

            docMenu = new ContextMenuStrip();

            int longerrors = 0;
            ActiveSWModel.ShowConfiguration2("URDF Export");

            //Set the variables for the page
            string PageTitle = "URDF/MJCF Export";
            long options = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_HandleKeystrokes;

            //Create the PropertyManager page
            PMPage = (PropertyManagerPage2)swApp.CreatePropertyManagerPage(
                PageTitle, (int)options, this, ref longerrors);

            //Make sure that the page was created properly
            if (longerrors == (int)swPropertyManagerPageStatus_e.swPropertyManagerPage_Okay)
            {
                SetupPropertyManagerPage();
            }
            else
            {
                //If the page is not created
                logger.Error("An error occurred while attempting to create the PropertyManager Page\nError: " + longerrors);
                MessageBox.Show("There was a problem setting up the property manager: " +
                    "\nEmail your maintainer with the log file found at " + Logger.GetFileName());
            }
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

        // Builds the entire PropertyManagerPage, ordered top to bottom on
        // the side bar:
        //   Header         -> BuildPmPageHeader    (Preview/Export, status, link tree, child-count)
        //   Setup tab      -> BuildSetupTab        (output / mesh format, export-meshes toggle)
        //   Link/Joint     -> BuildLinkJointTab    (names, coord systems, axis, joint type, joint properties)
        //   Visual tab     -> BuildComponentsTabs  (visual groups editor)
        //   Collision tab  -> BuildComponentsTabs  (collision groups editor + use-visual-as-collision toggle)
        //   Inertial tab   -> BuildComponentsTabs  (inertial source + components)
        //   Sites tab      -> BuildSitesTab        (MJCF-only site editor)
        //
        // The header builder is called BEFORE PMPage.AddTab so SolidWorks
        // renders the tree, child-count spinner, status label and
        // Preview/Export button above the tab strip and they remain
        // visible regardless of which tab is currently active.
        //
        // Tabs are created BEFORE the per-tab builders run so each builder
        // can hang controls directly off its own tab without ordering
        // concerns. Tree wiring happens last so the first
        // TreeAfterSelect -> FillPropertyManager call sees fully-
        // constructed PM controls.
        private void SetupPropertyManagerPage()
        {
            BuildPmPageHeader();

            PMSetupTab = (PropertyManagerPageTab)PMPage.AddTab(SetupTabID, "Setup", "", 0);
            // Use '/' as the separator: SW's PMP tab strip parses '&' as
            // a Windows-style keyboard mnemonic accelerator marker (the
            // character after '&' becomes the Alt-key shortcut and the
            // '&' itself is stripped from the rendered caption), so a
            // literal "Link & Joint" caption renders as "Link  Joint".
            PMLinkJointTab = (PropertyManagerPageTab)PMPage.AddTab(LinkJointTabID, "Link/Joint", "", 0);
            PMVisualTab = (PropertyManagerPageTab)PMPage.AddTab(VisualTabID, "Visual", "", 0);
            PMCollisionTab = (PropertyManagerPageTab)PMPage.AddTab(CollisionTabID, "Collision", "", 0);
            PMInertialTab = (PropertyManagerPageTab)PMPage.AddTab(InertialTabID, "Inertial", "", 0);
            PMSitesTab = (PropertyManagerPageTab)PMPage.AddTab(SitesTabID, "Sites", "", 0);

            BuildSetupTab();
            BuildLinkJointTab();
            BuildComponentsTabs();
            BuildSitesTab();

            WireUpLinkTree();
        }

        // Final initialization step run by SetupPropertyManagerPage. Wires
        // up the WinForms TreeView (events, context menu, root node) and
        // hands its window handle to the PropertyManagerPage host control
        // created earlier in BuildSetupTab. Splitting this from the
        // builders keeps the per-tab UI files free of TreeView / context-
        // menu concerns.
        private void WireUpLinkTree()
        {
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

            ToolStripMenuItem addChild = new ToolStripMenuItem { Text = "Add Child Link" };
            addChild.Click += new EventHandler(AddChildClick);
            ToolStripMenuItem removeChild = new ToolStripMenuItem { Text = "Remove" };
            removeChild.Click += new EventHandler(RemoveChildClick);
            docMenu.Items.AddRange(new ToolStripMenuItem[] { addChild, removeChild });

            LinkNode node = CreateEmptyNode(null);
            node.ContextMenuStrip = docMenu;
            Tree.Nodes.Add(node);
            Tree.SelectedNode = Tree.Nodes[0];
            PMSelectionVisual.SetSelectionFocus();
            PMPage.SetFocus(dotNetTree);
        }
        // Toggle the Enabled state of every control in the Collision Groups
        // editor. Used by OnCheckboxCheck to grey out the editor when the
        // user checks "Use visual groups as collision". Visible is NOT
        // flipped: the prior implementation toggled both Visible and
        // Enabled, which produced inconsistent UX between the two entry
        // paths. FillPropertyManager (which fires for an existing config
        // already carrying the toggle) ran before SW had laid out the
        // controls, so the resulting Visible=false had no immediate
        // visual effect and the controls appeared "greyed but visible";
        // OnCheckboxCheck (firing after the controls had rendered) flipped
        // Visible=false on rendered controls and they disappeared.
        // Enabled-only avoids that split: every entry path produces the
        // same "greyed out but still in layout" result, which is also
        // closer to the SW idiom for an inactive section.
        private void SetCollisionEditorEnabled(bool enabled)
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
                    pageControl.Enabled = enabled;
                }
            }
        }


    }
}