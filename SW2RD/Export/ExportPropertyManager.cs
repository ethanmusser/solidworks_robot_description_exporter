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
using SW2RD.Input;
using SW2RD.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SW2RD.Export
{
    // Which of the two purpose-built PropertyManagerPages this instance is.
    // Configure builds the link tree + the kinematic-config accordion
    // (Link/Joint, Visual, Collision, Inertial, Sites) and its green check
    // saves the configuration. Export builds only the Export group (output /
    // mesh options + Export button) and loads the saved configuration into an
    // in-memory tree to feed the export pipeline. One class serves both modes
    // so the Tree / Exporter / validation / export plumbing is shared rather
    // than duplicated. Public because the public ExportPropertyManager
    // constructor takes it as a parameter.
    public enum ExportPmMode
    {
        Configure,
        Export,
    }

    [ComVisible(true)]
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        #region class variables

        private static readonly log4net.ILog logger = Logger.GetLogger();
        public SldWorks swApp;
        public ModelDoc2 ActiveSWModel;

        // Configure vs Export. Set once in the constructor; gates group
        // creation, tree wiring, green-check save, and the export-time
        // SaveActiveNode skip.
        private readonly ExportPmMode mode;

        // SOLIDWORKS exposes this class to COM via IPropertyManagerPage2Handler,
        // not via .NET serialization. Configuration persistence runs through
        // Link / Joint, so the handler keeps only live UI state.
        public ExportHelper Exporter;
        public LinkNode previouslySelectedNode;
        public LinkNode rightClickedNode;
        private readonly ContextMenuStrip docMenu;
        private bool disposed;

        //General objects required for the PropertyManager page

        private readonly PropertyManagerPage2 PMPage;
        // The PropertyManagerPage is a single SolidWorks PropertyManagerPage2
        // run as a Flow-Simulation-style two-step wizard via the native
        // Next / Previous arrows (swPropertyManagerOptions_MultiplePages).
        // Each "page" of the wizard is simulated by toggling the Visible
        // property of a set of PropertyManagerPageGroups (SW has no
        // SetTabVisible, so tabs cannot drive wizard navigation):
        //
        //   Page 1 (Configure): PMTreeGroup (link tree, always expanded) +
        //     the five kinematic-config groups (Link/Joint, Visual,
        //     Collision, Inertial, Sites) which behave as an ACCORDION -
        //     expanding one collapses the others, and the expanded group is
        //     the "active section" that drives the SOLIDWORKS viewer
        //     highlight (see OnGroupExpand / RehydrateMarksForActiveSection).
        //   Page 2 (Export): PMExportGroup (output / mesh format, quality,
        //     rotation, angle, validation status, Export button).
        //
        // UpdateWizardVisibility flips the per-page group visibility and the
        // SetMessage3 step banner; OnNextPage / OnPreviousPage move between
        // the two pages.
        private PropertyManagerPageGroup PMTreeGroup;
        private PropertyManagerPageGroup PMLinkJointGroup;
        private PropertyManagerPageGroup PMVisualGroup;
        private PropertyManagerPageGroup PMCollisionGroup;
        private PropertyManagerPageGroup PMInertialGroup;
        private PropertyManagerPageGroup PMSitesGroup;
        private PropertyManagerPageGroup PMExportGroup;
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
        // Single role-polymorphic coordinate-system picker (global origin for
        // the WorldNode, world->body offset for a top-level body, joint
        // origin for a nested link). All three roles persist to
        // Link.Joint.CoordinateSystemName.
        private PropertyManagerPageSelectionbox PMSelectionJointCoordsys;
        private PropertyManagerPageSelectionbox PMSelectionJointAxis;
        // "Joint axis source" dropdown: Reference axis / Coordinate system
        // X / Y / Z / Auto-derive from kinematic chain. Item order MUST match
        // AxisSourceComboItems and the JointAxisSource enum (0..4). Drives
        // Joint.AxisSource; gates the reference-axis SelectionBox (enabled
        // only for "Reference axis") and the reverse-direction button
        // (enabled for everything except Auto-derive). Round-trips on link
        // switch via FillPropertyManager / SaveActiveNode.
        private PropertyManagerPageCombobox PMComboBoxAxisSource;

        // Export-time choices shown on the Setup tab. These read together
        // as "what should the next export do".
        private PropertyManagerPageCombobox PMComboBoxOutputFormat;
        private PropertyManagerPageCombobox PMComboBoxMeshFormat;
        private PropertyManagerPageCheckbox PMCheckExportMeshes;
        private PropertyManagerPageCheckbox PMCheckFastMeshExport;
        private PropertyManagerPageCheckbox PMCheckKeepResolved;
        private PropertyManagerPageCombobox PMComboBoxMeshQuality;
        // Manual mesh-quality overrides, shown/enabled only when the quality
        // dropdown is set to "Custom" (and the fast STL path is active).
        private PropertyManagerPageNumberbox PMNumberBoxCustomChordFraction;
        private PropertyManagerPageNumberbox PMNumberBoxCustomAngle;
        private PropertyManagerPageNumberbox PMNumberBoxCustomMaxChord;
        private PropertyManagerPageCombobox PMComboBoxRotationFormat;
        private PropertyManagerPageCombobox PMComboBoxAngleUnit;
        private PropertyManagerPageLabel PMLabelConfigurationCache;

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
        // Keep controls visible and toggle only Enabled. This keeps the
        // layout stable while still communicating that the collision editor
        // is inactive because visual meshes will be reused.
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
        // shown in the corresponding SelectionBox. -1 means the link currently
        // has no group of that role.
        private int activeVisualGroupIndex = -1;
        private int activeCollisionGroupIndex = -1;
        private int activeSiteIndex = -1;

        // Guard against re-entrancy: when LoadActiveVisualGroupIntoSelectionBox /
        // LoadActiveCollisionGroupIntoSelectionBox programmatically populate a
        // SelectionBox via CommonSwOperations.SelectComponents, every added item
        // fires OnSelectionboxListChanged. That handler would otherwise commit a
        // partial selection back to the active group and bounce the listbox count
        // while the load is in progress. The flag is set true around those
        // programmatic loads. PropertyManager events are delivered on the
        // SolidWorks UI thread, so a plain bool is safe here.
        private bool suppressGroupListboxRefresh;

        // Guard against re-entrancy when SyncVisualGroupNameTextbox /
        // SyncCollisionGroupNameTextbox programmatically write the active
        // group's name into PMTextBox*GroupName. That write fires
        // OnTextboxChanged, which would otherwise re-enter the rename
        // handler and write the (just-loaded) name straight back into the
        // group. The flag is set true around those programmatic loads, so
        // the rename handler only fires for real user keystrokes.
        private bool suppressGroupNameTextboxEvents;

        // ID of the kinematic-config GROUP that is currently the active
        // section on page 1. Drives the "viewer highlight follows the
        // active section" behavior: when this is e.g. VisualGroupID, only
        // the Visual SelectionBox's mark is populated and all other marks
        // are cleared, so the SOLIDWORKS viewer highlights ONLY the
        // components of the active visual group (not the union of every
        // group ever loaded by FillPropertyManager). OnGroupExpand updates
        // this whenever the user expands a different accordion group;
        // FillPropertyManager reads it when the active link changes, so a
        // link switch repopulates only the marks the currently-expanded
        // section needs. Defaults to LinkJointGroupID because that group
        // opens expanded (SW does not fire OnGroupExpand for the
        // initially-expanded group).
        private int currentActiveSectionId = LinkJointGroupID;

        // Wizard pagination machinery, kept DORMANT after the Configure /
        // Export split. The Configure PMP is a single accordion page today
        // (wizardPages has one entry), so the native Next / Previous arrows
        // are not even enabled (the swPropertyManagerOptions_MultiplePages
        // option is gated on TotalWizardPages > 1). The machinery is retained
        // so a future second Configure page is purely a data change: add a
        // second int[] of group IDs to wizardPages in SetupPropertyManagerPage
        // and the arrows, EnableButton logic, and per-page visibility all
        // light up automatically. Export mode never paginates.
        private int currentWizardPage = 1;

        // Per-page descriptor: each entry is the set of kinematic group IDs
        // shown on that wizard page. Populated in SetupPropertyManagerPage
        // (Configure mode only). The tree group is always visible in Configure
        // and is therefore NOT listed here. Null/empty in Export mode.
        private List<int[]> wizardPages;
        private int TotalWizardPages => wizardPages?.Count ?? 1;

        // Re-entrancy guard for the page-1 accordion. Programmatically
        // setting PropertyManagerPageGroup.Expanded fires OnGroupExpand,
        // so any code that collapses sibling groups (or sets the initial
        // accordion state) sets this true to stop the handler re-entering
        // itself.
        private bool suppressGroupExpandAccordion;

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
        // The listbox selects the active site; the textbox and
        // SelectionBox live-edit that active site's name and coordinate
        // system.
        private PropertyManagerPageListbox PMListBoxSites;
        private PropertyManagerPageTextbox PMTextBoxSiteName;
        private PropertyManagerPageSelectionbox PMSelectionSiteCoordSys;
        private PropertyManagerPageButton PMButtonSiteAdd;
        private PropertyManagerPageButton PMButtonSiteRemove;
        private bool suppressSiteEditorEvents;
        private bool suppressSiteListboxSelectionChanged;

        private PropertyManagerPageLabel PMLabelJointName;
        private PropertyManagerPageLabel PMLabelParentLink;
        private PropertyManagerPageLabel PMLabelAxes;
        private PropertyManagerPageLabel PMLabelCoordSys;
        private PropertyManagerPageLabel PMLabelJointType;
        private PropertyManagerPageLabel PMLabelInertialSource;
        private PropertyManagerPageLabel PMLabelVisualComponents;
        private PropertyManagerPageLabel PMLabelCollisionComponents;
        private PropertyManagerPageLabel PMLabelInertialComponents;

        // The world attachment (Welded / Free) for a top-level body is now
        // carried by the single role-aware PMComboBoxJointType dropdown (it
        // shows "fixed" / "free" when a top-level body is active). There is
        // no longer a separate world-attachment combobox.

        private PropertyManagerPageWindowFromHandle PMTree;

        // Fixed height (px) of the link-tree box and its WindowFromHandle host.
        // The tree no longer grows with the node count at runtime: SW PMPage
        // does NOT reflow sibling controls when a hosted control's height
        // changes after build (it only re-flows a group on an expand/collapse
        // pass), so a growing box overlapped the controls below it. The box is
        // a fixed size and the WinForms TreeView's native vertical scrollbar
        // handles overflow. Do NOT re-add runtime height growth.
        private const int LinkTreeBoxHeight = 250;

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
        // 25 retired (was IDLabelGlobalCoordsys); the global-origin coord-sys
        // picker was merged into the single PMSelectionJointCoordsys box.
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
        // Wizard group IDs. 90 (the old SetupTabID) is retired - the Setup
        // tab's controls moved to the Export group (page 2) and the tree /
        // child-count / cache label moved to the Tree group (page 1). The
        // five kinematic-config group IDs reuse the old per-tab IDs so the
        // active-section machinery (RehydrateMarksForActiveSection) reads
        // naturally; Tree / Export groups take fresh high-end IDs.
        private const int LinkJointGroupID = 91;
        private const int VisualGroupID = 92;
        private const int CollisionGroupID = 93;
        private const int InertialGroupID = 94;
        private const int SitesGroupID = 95;
        // 100 retired (was SelectionGlobalCoordsysID); merged into
        // SelectionJointCoordsysID (the single coordinate-system picker).
        private const int SelectionJointCoordsysID = 101;
        private const int SelectionJointAxisID = 102;
        private const int SelectionSiteCoordSysID = 103;
        private const int ValidationStatusLabelID = 104;

        // Tab-local labels for the Visual / Collision / Inertial / Sites
        // tabs so each page has an explicit visual heading.
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
        // 143 retired (was CheckAutoDeriveAxisID); the auto-derive checkbox was
        // merged into the role-aware "Joint axis source" dropdown
        // (ComboBoxAxisSourceID = 162).
        private const int LabelLinkNameStaticID = 144;
        private const int LabelVisualComponentsHeaderID = 145;
        private const int LabelCollisionComponentsHeaderID = 146;
        private const int LabelSiteCoordSysHeaderID = 147;
        private const int LabelActiveLinkTreeID = 148;
        // 149 retired (was LabelWorldAttachmentID) and 150 retired (was
        // ComboBoxWorldAttachmentID); the world-attachment combobox was merged
        // into the single role-aware joint-type dropdown (ComboBoxJointTypeID).
        private const int LabelConfigurationCacheID = 151;
        // 152 retired (was ButtonClearSavedConfigurationID); the Clear Saved
        // Configuration action moved to a dedicated ribbon command (see
        // SwAddin.ClearSavedConfigurationCommand).
        // 153 retired (was ButtonImportLegacyConfigurationID); legacy import removed.
        private const int FastMeshExportCheckID = 154;
        private const int MeshQualityComboID = 155;
        private const int LabelRotationFormatID = 156;
        private const int RotationFormatComboID = 157;
        private const int LabelAngleUnitID = 158;
        private const int AngleUnitComboID = 159;
        // Wizard group boxes (the page-1 tree group and the page-2 export
        // group). The five kinematic-config groups reuse the old per-tab
        // IDs (91-95) above.
        private const int TreeGroupID = 160;
        private const int ExportGroupID = 161;
        // "Joint axis source" dropdown on the Link/Joint tab (replaces the
        // retired auto-derive checkbox at slot 143).
        private const int ComboBoxAxisSourceID = 162;
        // "Keep components resolved after export" checkbox on the Export group.
        private const int KeepResolvedCheckID = 163;
        // Manual mesh-quality override numberboxes, active only for the "Custom"
        // quality level (chord fraction %, angle tolerance deg, max chord mm),
        // each preceded by its own descriptive label.
        private const int CustomChordFractionNumberID = 164;
        private const int CustomAngleNumberID = 165;
        private const int CustomMaxChordNumberID = 166;
        private const int LabelCustomChordFractionID = 167;
        private const int LabelCustomAngleID = 168;
        private const int LabelCustomMaxChordID = 169;
        // Label above the "Mesh quality" dropdown, matching the separate-label
        // style of the other Export-group comboboxes.
        private const int LabelMeshQualityID = 170;

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
        // Bits currently consumed: 0, 1, 2, 4, 5, 6. Bit 3 (value 8) is
        // FREE - it was the global-origin coord-sys mark, retired when the
        // global + joint coord-sys pickers were merged into the single
        // PMSelectionJointCoordsys box (bit 4). A future agent adding a new
        // SelectionBox should reclaim bit 3 (= 8) first, then bit 7 (= 128).
        private const int VisualSelectionMark = 1 << 0;          // 1
        private const int CollisionSelectionMark = 1 << 1;       // 2
        private const int InertialSelectionMark = 1 << 2;        // 4
        // bit 3 (1 << 3 = 8) free - see comment above
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
        public ExportPropertyManager(SldWorks swAppPtr, ExportPmMode pmMode)
        {
            mode = pmMode;
            swApp = swAppPtr;
            ActiveSWModel = swApp.ActiveDoc;
            Exporter = new ExportHelper(swApp);
            Exporter.AxisOverlayDirectionFlipped = OnAxisOverlayDirectionFlipped;
            Exporter.URDFRobot = new Robot();
            Exporter.URDFRobot.Name = ActiveSWModel.GetTitle();

            docMenu = new ContextMenuStrip();

            // Build the wizard page descriptor before creating the page so the
            // MultiplePages gate below sees the real page count. Configure mode
            // only; Export mode never paginates so wizardPages stays null.
            if (mode == ExportPmMode.Configure)
            {
                InitWizardPages();
            }

            int longerrors = 0;
            ActiveSWModel.ShowConfiguration2("Robot Description Export");

            //Set the variables for the page
            string PageTitle = mode == ExportPmMode.Configure
                ? "Configure Robot Description"
                : "Export Robot Description";
            // swPropertyManagerOptions_MultiplePages turns on the native
            // Next / Previous arrows in the PMP title bar. It is enabled ONLY
            // in Configure mode AND only when the wizard actually has more than
            // one page (TotalWizardPages > 1). Today Configure is a single
            // accordion page, so the option is off and no dead arrows render;
            // the gate flips it on automatically once a second page is added
            // to wizardPages. Export mode never shows the arrows.
            // swPropertyManagerOptions_GrayOutDisabledSelectionListboxes
            // (65536) makes SW paint a disabled SelectionBox with a greyed
            // background. SW does NOT grey disabled selection boxes by default
            // (an empty disabled box looks identical to an empty enabled one),
            // so without this flag the joint-axis SelectionBox stays visually
            // "active" when the axis source is a coordinate-system basis axis
            // or auto-derive even though it is functionally disabled. SW docs
            // note hiding is the "standard" alternative, but we keep the box
            // visible-but-greyed so the layout doesn't reflow when the user
            // switches axis source.
            long options = (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_HandleKeystrokes +
                (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_GrayOutDisabledSelectionListboxes;
            if (mode == ExportPmMode.Configure && TotalWizardPages > 1)
            {
                options += (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_MultiplePages;
            }

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

        // Builds the entire PropertyManagerPage as a two-step wizard, ordered
        // top to bottom on the side bar:
        //   Page 1 (Configure):
        //     Tree group       -> BuildTreeGroup       (link tree, child-count, saved-config label)
        //     Link/Joint group -> BuildLinkJointTab    (names, coord systems, axis, joint type, joint properties)
        //     Visual group     -> BuildComponentsTabs  (visual groups editor)
        //     Collision group  -> BuildComponentsTabs  (collision groups editor + use-visual-as-collision toggle)
        //     Inertial group   -> BuildComponentsTabs  (inertial source + components)
        //     Sites group      -> BuildSitesTab        (MJCF-only site editor)
        //   Page 2 (Export):
        //     Export group     -> BuildExportGroup     (output / mesh format, quality, rotation, angle, validation, Export button)
        //
        // Every group box is created up front so the build order (and
        // therefore the on-screen order) is explicit; the per-group
        // builders then hang controls off the appropriate group. The five
        // kinematic-config groups are an accordion (only one expanded at a
        // time); ApplyAccordionInitialState opens Link/Joint and collapses
        // the rest. UpdateWizardVisibility hides the page-2 group on first
        // show. Tree wiring happens last so the first TreeAfterSelect ->
        // FillPropertyManager call sees fully-constructed PM controls.
        private void SetupPropertyManagerPage()
        {
            if (mode == ExportPmMode.Configure)
            {
                SetupConfigurePage();
            }
            else
            {
                SetupExportPage();
            }
        }

        // Configure PMP: the link tree + the five kinematic-config groups
        // (Link/Joint, Visual, Collision, Inertial, Sites) as an accordion.
        // No Export group is created here - export lives in its own PMP.
        private void SetupConfigurePage()
        {
            int visible = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible;
            int visibleExpanded = visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

            // The tree group and the Link/Joint group open expanded; the
            // remaining kinematic groups open collapsed so the page reads as
            // an accordion.
            PMTreeGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                TreeGroupID, "Robot links", visibleExpanded);
            PMLinkJointGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                LinkJointGroupID, "Link / Joint", visibleExpanded);
            PMVisualGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                VisualGroupID, "Visual", visible);
            PMCollisionGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                CollisionGroupID, "Collision", visible);
            PMInertialGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                InertialGroupID, "Inertial", visible);
            PMSitesGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                SitesGroupID, "Sites", visible);

            BuildTreeGroup();
            BuildLinkJointTab();
            BuildComponentsTabs();
            BuildSitesTab();

            ApplyAccordionInitialState();
            UpdateWizardVisibility();

            CreateLinkTreeControl(interactive: true);
        }

        // Export PMP: only the Export group (output / mesh format, quality,
        // rotation, validation, Export button). The kinematic config is
        // read-only here - it was authored in the Configure PMP and is loaded
        // into an in-memory tree (CreateLinkTreeControl(interactive: false))
        // that the export pipeline reads. No tree UI, no accordion.
        private void SetupExportPage()
        {
            int visible = (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Visible;
            int visibleExpanded = visible +
                (int)swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded;

            PMExportGroup = (PropertyManagerPageGroup)PMPage.AddGroupBox(
                ExportGroupID, "Export", visibleExpanded);

            BuildExportGroup();

            CreateLinkTreeControl(interactive: false);
        }

        // Seed the wizard page descriptor (Configure mode). One entry today =
        // a single accordion page holding the five kinematic groups; the tree
        // group is always visible and is not listed. Add a second int[] here
        // to split Configure across two wizard pages (the MultiplePages
        // option, EnableButton arrows, and UpdateWizardVisibility all follow).
        private void InitWizardPages()
        {
            wizardPages = new List<int[]>
            {
                new[]
                {
                    LinkJointGroupID,
                    VisualGroupID,
                    CollisionGroupID,
                    InertialGroupID,
                    SitesGroupID,
                },
            };
        }

        // Page-1 accordion seed state: only the Link/Joint group is
        // expanded. Wrapped in suppressGroupExpandAccordion so the
        // programmatic Expanded writes don't re-enter the accordion
        // handler.
        private void ApplyAccordionInitialState()
        {
            suppressGroupExpandAccordion = true;
            try
            {
                if (PMLinkJointGroup != null) PMLinkJointGroup.Expanded = true;
                if (PMVisualGroup != null) PMVisualGroup.Expanded = false;
                if (PMCollisionGroup != null) PMCollisionGroup.Expanded = false;
                if (PMInertialGroup != null) PMInertialGroup.Expanded = false;
                if (PMSitesGroup != null) PMSitesGroup.Expanded = false;
            }
            finally
            {
                suppressGroupExpandAccordion = false;
            }
        }

        // Maps a wizard / kinematic group ID to its PropertyManagerPageGroup
        // object (Configure mode). Returns null for IDs whose group was not
        // created in the current mode.
        private PropertyManagerPageGroup GroupById(int id)
        {
            if (id == TreeGroupID) return PMTreeGroup;
            if (id == LinkJointGroupID) return PMLinkJointGroup;
            if (id == VisualGroupID) return PMVisualGroup;
            if (id == CollisionGroupID) return PMCollisionGroup;
            if (id == InertialGroupID) return PMInertialGroup;
            if (id == SitesGroupID) return PMSitesGroup;
            if (id == ExportGroupID) return PMExportGroup;
            return null;
        }

        // Applies per-wizard-page group visibility from the wizardPages
        // descriptor, manages the native Next / Back arrows, and writes the
        // step banner. Configure mode only (Export mode never paginates and
        // does not call this). The tree group is always visible. The embedded
        // WinForms tree is also toggled directly as belt-and-suspenders in
        // case hiding the WindowFromHandle's host group does not hide the
        // child control. With a single wizard page (today) every kinematic
        // group is visible, the arrows are disabled, and the banner reads as a
        // single-step page.
        private void UpdateWizardVisibility()
        {
            if (wizardPages == null || wizardPages.Count == 0) return;

            if (PMTreeGroup != null) PMTreeGroup.Visible = true;

            // Show only the groups belonging to the current page; hide the
            // rest. Page numbers are 1-based.
            for (int page = 1; page <= wizardPages.Count; page++)
            {
                bool onThisPage = page == currentWizardPage;
                foreach (int groupId in wizardPages[page - 1])
                {
                    PropertyManagerPageGroup grp = GroupById(groupId);
                    if (grp != null) grp.Visible = onThisPage;
                }
            }

            // SOLIDWORKS does NOT auto-manage the native multipage Next /
            // Back arrows from the OnNextPage / OnPreviousPage return values
            // - it leaves whatever enabled state the page was created with
            // until we call EnableButton. So drive them explicitly per page:
            // Back is live on every page but the first, Next on every page but
            // the last. With one page both are disabled (and the arrows are
            // not even shown because MultiplePages is off).
            try
            {
                PMPage.EnableButton(
                    (int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Back,
                    currentWizardPage > 1);
                PMPage.EnableButton(
                    (int)swPropertyManagerPageButtons_e.swPropertyManagerPageButton_Next,
                    currentWizardPage < TotalWizardPages);
            }
            catch (Exception ex)
            {
                logger.Warn("Updating wizard Next / Back button state failed: " + ex.Message);
            }

            if (Tree != null)
            {
                try
                {
                    Tree.Visible = true;
                }
                catch (Exception ex)
                {
                    logger.Warn("Toggling Tree.Visible in UpdateWizardVisibility failed: " + ex.Message);
                }
            }

            try
            {
                if (TotalWizardPages > 1)
                {
                    PMPage.SetMessage3(
                        "Step " + currentWizardPage + " of " + TotalWizardPages +
                        ": Configure the kinematic tree - select a link in the tree, then expand a section to edit it. Use the arrows to move between pages.",
                        (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible,
                        (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
                        "Step " + currentWizardPage + " of " + TotalWizardPages);
                }
                else
                {
                    PMPage.SetMessage3(
                        "Select a link in the tree, then expand a section (Link/Joint, Visual, Collision, Inertial, Sites) to edit it. Click the green check to save the configuration.",
                        (int)swPropertyManagerPageMessageVisibility.swMessageBoxVisible,
                        (int)swPropertyManagerPageMessageExpanded.swMessageBoxExpand,
                        "Configure Robot Description");
                }
            }
            catch (Exception ex)
            {
                logger.Warn("Updating wizard title / message failed: " + ex.Message);
            }
        }

        // Final initialization step run by SetupPropertyManagerPage. Wires
        // up the WinForms TreeView (events, context menu, root node) and
        // hands its window handle to the PropertyManagerPage host control
        // created earlier in BuildTreeGroup. Splitting this from the
        // builders keeps the per-section UI files free of TreeView /
        // context-menu concerns.
        // Builds the in-memory LinkNode Tree and (in Configure mode) hosts the
        // WinForms TreeView inside the PMP.
        //
        // interactive = true (Configure): the full editor tree - selection /
        // drag-drop / context-menu events, the PMTree WindowFromHandle host,
        // and PMPage focus. The first TreeAfterSelect -> FillPropertyManager
        // populates the kinematic groups.
        //
        // interactive = false (Export): a bare in-memory tree with a single
        // root node and NO event handlers / host / focus (PMTree and the
        // SelectionBoxes do not exist in Export mode). LoadConfigTree
        // repopulates the nodes from the saved attribute and ExportButtonPress
        // reads them; the user never sees or edits this tree.
        private void CreateLinkTreeControl(bool interactive)
        {
            Tree = new TreeView
            {
                Height = LinkTreeBoxHeight,
                Visible = true,
                // Native vertical scrollbar handles trees taller than the
                // fixed box (default is true; set explicitly to document that
                // scrolling - not runtime height growth - is how overflow is
                // handled).
                Scrollable = true
            };

            if (interactive)
            {
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
            }

            LinkNode node = CreateEmptyNode(null);
            if (interactive)
            {
                node.ContextMenuStrip = docMenu;
            }
            Tree.Nodes.Add(node);
            Tree.SelectedNode = Tree.Nodes[0];

            if (interactive)
            {
                PMSelectionVisual.SetSelectionFocus();
                PMPage.SetFocus(dotNetTree);
            }
        }
        // Toggle the Enabled state of every control in the Collision Groups
        // editor. Controls stay visible so loading an existing config and
        // clicking the checkbox produce the same stable, greyed-out layout.
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