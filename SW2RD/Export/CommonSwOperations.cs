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

using log4net;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2RD.URDF;
using SW2RD.Utilities;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace SW2RD.Export
{
    public static class CommonSwOperations
    {
        private static readonly ILog logger = Logger.GetLogger();

        //Selects the components of a link. Helps highlight when the associated node is
        // selected from the tree
        public static void SelectComponents(ModelDoc2 model, Link Link, bool clearSelection, int mark = -1)
        {
            if (clearSelection)
            {
                model.ClearSelection2(true);
            }
            SelectionMgr manager = model.SelectionManager;
            SelectData data = manager.CreateSelectData();
            data.Mark = mark;
            SelectComponents(model, Link.SWComponents, false);
            foreach (Link child in Link.Children)
            {
                SelectComponents(model, child, false, mark);
            }
        }

        //Selects components from a list.
        public static void SelectComponents(
            ModelDoc2 model, List<Component2> components, bool clearSelection = true, int mark = -1)
        {
            if (clearSelection)
            {
                model.ClearSelection2(true);
            }
            SelectionMgr manager = model.SelectionManager;
            SelectData data = manager.CreateSelectData();
            data.Mark = mark;
            foreach (Component2 component in components)
            {
                component.Select4(true, data, false);
            }
        }

        // Deselects every entity at a single mark, leaving every other
        // mark untouched. The natural-feeling alternative,
        // model.ClearSelection2(true), is GLOBAL across every mark - so
        // a SelectionBox loader (e.g. LoadActiveCollisionGroupInto
        // SelectionBox at mark 12) that opens with ClearSelection2(true)
        // wipes the contents of every OTHER SelectionBox in the same
        // PMP (visual mark 11, inertial mark 13, the four feature
        // pickers at marks 21-24). That cross-mark wipe was the root
        // cause of the "Visual tab opens empty under (1 comp.) listbox
        // entry" symptom - the visual loader populated mark 11, then
        // the collision loader (called next from FillPropertyManager)
        // immediately wiped it before returning early on a base link
        // with no collision groups configured.
        //
        // Permanent rule: a SelectionBox loader owns exactly one mark
        // and must call DeselectAllAtMark(model, ownMark) for its
        // pre-populate clear, never ClearSelection2(true).
        public static void DeselectAllAtMark(ModelDoc2 model, int mark)
        {
            if (model == null) return;
            SelectionMgr selMgr = model.SelectionManager;
            if (selMgr == null) return;
            int count = selMgr.GetSelectedObjectCount2(mark);
            // DeSelect2 is 1-indexed and shifts the remaining entries
            // down on each removal; iterate descending so the indices
            // stay valid.
            for (int i = count; i >= 1; i--)
            {
                selMgr.DeSelect2(i, mark);
            }
        }

        // Pulls Component2 instances out of the SelectionMgr at the given
        // mark. Used to read the live contents of the visual / collision /
        // inertial SelectionBoxes back onto the data model.
        //
        // MUST use `as Component2` (not an explicit cast) because SW's
        // SelectionMgr can return non-Component2 entities at any mark in
        // certain scenarios. Concretely: ExportHelper.GetRefAxis runs
        // `Extension.SelectByID2(<axis>, "AXIS", 0,0,0, false, 0, null, 0)`
        // on the axis-feature owning doc to read the axis vector via
        // Feature.GetSpecificFeature2 -> RefAxis.GetRefAxisParams. When the
        // joint axis lives at the ASSEMBLY level (the common case for the
        // 3_DOF_ARM example assemblies), `r.OwningDoc` is the same
        // ActiveSWModel that backs the PMP SelectionBoxes, and the
        // `Append=false / mark=0` SelectByID2 leaves a RefAxis feature in
        // the assembly's SelectionMgr. SolidWorks then sometimes surfaces
        // that mark-0 / unmarked feature in `GetSelectedObjectCount2(N)` /
        // `GetSelectedObject6(i, N)` queries for positive marks N - the
        // exact behavior varies by SW version. An explicit `(Component2)obj`
        // cast on a RefAxis throws InvalidCastException, which propagates
        // up through TreeAfterSelect's catch to the user as a
        // "There was a problem with the property manager" popup.
        // Skipping non-Component2 entries here makes this defensive
        // against the leak; the teardown defense in CommitActive*
        // GroupSelection is what protects the saved state from being
        // wiped by the leak.
        public static void GetSelectedComponents(
            ModelDoc2 model, List<Component2> Components, int Mark = -1)
        {
            SelectionMgr selectionManager = model.SelectionManager;
            Components.Clear();
            for (int i = 0; i < selectionManager.GetSelectedObjectCount2(Mark); i++)
            {
                object obj = selectionManager.GetSelectedObject6(i + 1, Mark);
                Component2 comp = obj as Component2;
                if (comp != null)
                {
                    Components.Add(comp);
                }
            }
        }

        //finds all the hidden components, which will be added to a new display state. Also
        // used when exporting STLs, so that hidden components remain hidden
        public static List<string> FindHiddenComponents(object[] varComp)
        {
            List<string> hiddenComp = new List<string>();
            foreach (object obj in varComp)
            {
                // Defensive `as` cast for the same reason as
                // GetSelectedComponents above: SW can return non-Component2
                // entities here in some configurations and the legacy
                // explicit cast crashed the export.
                Component2 comp = obj as Component2;
                if (comp != null && comp.IsHidden(false))
                {
                    hiddenComp.Add(comp.Name2);
                }
            }
            return hiddenComp;
        }

        //Except for an exclusionary list, this shows all the components
        public static void ShowAllComponents(ModelDoc2 model, List<string> hiddenComponents)
        {
            // Restore every component that the export hid back to visible, in a
            // SINGLE bulk SelectionMgr.AddSelectionListObjects + ShowComponent2
            // round trip - the symmetric counterpart to the fast bulk hide
            // (Extension.SelectAll + HideComponent2, ~1s for ~1800 components).
            //
            // Components that were ALREADY hidden before the export (the
            // hiddenComponents exclusion list) are left untouched so they stay
            // hidden.
            //
            // History of this method (THREE prior approaches, all slower or
            // wrong - do NOT regress to any of them):
            //   1. Select each reveal-target individually via Component2.Select4,
            //      then one ShowComponent2. Correct, but the per-component
            //      Select4 SelectionMgr round trips dominated - ~3.5 min
            //      restoring ~2000 lightweight/network-PDM components.
            //   2. Extension.SelectAll() + ShowComponent2(). Fast, but WRONG:
            //      SelectAll selects only VISIBLE entities, and by this point the
            //      whole assembly is hidden, so SelectAll selects nothing and
            //      ShowComponent2 is a no-op - the assembly stayed hidden.
            //   3. Per-component Component2.Visible = swComponentVisible. Correct
            //      and avoids Select4, but each property set is its own SW round
            //      trip; measured ~38s restoring ~1800 components even with
            //      viewport graphics updates suppressed. (Retained below as the
            //      verified fallback, since it is the provably-correct path.)
            // AddSelectionListObjects selects the entire reveal set in ONE COM
            // call (a SAFEARRAY of Component2), so the subsequent single
            // ShowComponent2 does the show in bulk like the hide does. This is
            // the same selection semantics as #1's Select4 (which is known to
            // reveal currently-hidden components), just without the per-item
            // round trips. We VERIFY it actually worked (count selected >=
            // requested AND no target left hidden) and fall back to #3 if a
            // future SW version refuses to bulk-select hidden components - so
            // correctness can never regress, only speed.
            HashSet<string> hiddenSet = (hiddenComponents != null)
                ? new HashSet<string>(hiddenComponents)
                : new HashSet<string>();

            AssemblyDoc assyDoc = (AssemblyDoc)model;
            object[] varComps = assyDoc.GetComponents(false);

            List<Component2> toShow = new List<Component2>();
            foreach (Component2 comp in varComps)
            {
                if (comp == null)
                {
                    continue;
                }
                if (!hiddenSet.Contains(comp.Name2))
                {
                    toShow.Add(comp);
                }
            }
            if (toShow.Count == 0)
            {
                return;
            }

            bool bulkOk = false;
            try
            {
                model.ClearSelection2(true);
                SelectionMgr selMgr = model.SelectionManager;
                SelectData data = selMgr.CreateSelectData();
                // Mark -1 == "no mark"; matches the transient-selection pattern
                // SelectComponents already uses for show/hide (these are not
                // PMP SelectionBoxes, so the bitmask-mark rules do not apply).
                data.Mark = -1;
                int selected = selMgr.AddSelectionListObjects(toShow.ToArray(), data);
                if (selected > 0)
                {
                    model.ShowComponent2();
                }
                model.ClearSelection2(true);

                // Confirm the bulk path revealed everything before trusting it.
                // AnyStillHidden short-circuits on the first hidden target, so
                // on success it is a single cheap IsHidden walk and on failure
                // it returns immediately.
                bulkOk = (selected >= toShow.Count) && !AnyStillHidden(toShow);
            }
            catch
            {
                bulkOk = false;
            }

            if (bulkOk)
            {
                return;
            }

            logger.Warn("Bulk ShowComponent2 did not reveal every component; " +
                "falling back to per-component visibility restore.");
            foreach (Component2 comp in toShow)
            {
                if (comp == null)
                {
                    continue;
                }
                if (comp.Visible != (int)swComponentVisibilityState_e.swComponentVisible)
                {
                    comp.Visible = (int)swComponentVisibilityState_e.swComponentVisible;
                }
            }
        }

        // True if any component in the list is still hidden. Used to verify the
        // bulk ShowComponent2 path in ShowAllComponents actually revealed every
        // target before we trust it over the per-component fallback.
        private static bool AnyStillHidden(List<Component2> comps)
        {
            foreach (Component2 comp in comps)
            {
                if (comp != null && comp.IsHidden(false))
                {
                    return true;
                }
            }
            return false;
        }

        //Shows the components in the list. Useful  for exporting STLs
        public static void ShowComponents(ModelDoc2 model, List<Component2> components)
        {
            List<Component2> expanded = ExpandWithChildren(components);
            SelectComponents(model, expanded, true);
            model.ShowComponent2();
        }

        //Hides the components from a list
        public static void HideComponents(ModelDoc2 model, List<Component2> components)
        {
            List<Component2> expanded = ExpandWithChildren(components);
            SelectComponents(model, expanded, true);
            model.HideComponent2();
        }

        // Returns the input components PLUS every descendant component
        // (recursively, deduped by Name2).
        //
        // Why this is required for the STL export show/hide: a visual or
        // collision group may name a SUB-ASSEMBLY as its (single) component
        // rather than a leaf part. The whole-assembly hide-all in
        // ExportRobotCore (Extension.SelectAll + HideComponent2) hides every
        // component at every level, including that sub-assembly's leaf parts.
        // ShowComponent2 on the sub-assembly NODE alone does NOT re-reveal its
        // already-hidden leaf parts, so the per-link assembly STL SaveAs - which
        // exports only VISIBLE geometry - sees nothing inside the sub-assembly
        // and writes an EMPTY mesh (which MuJoCo / downstream consumers reject).
        // Expanding to descendants makes the sub-assembly export the union of
        // its parts as one mesh. The expansion MUST be applied symmetrically in
        // HideComponents too: after the SaveAs, the same descendants have to be
        // re-hidden, otherwise they stay visible and contaminate every
        // subsequent link's SaveAs. Leaf-part groups are unaffected -
        // GetChildren returns nothing for a part, so the expansion is a no-op
        // and the result is just the original list.
        private static List<Component2> ExpandWithChildren(List<Component2> components)
        {
            List<Component2> result = new List<Component2>();
            if (components == null)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>();
            Stack<Component2> stack = new Stack<Component2>();
            foreach (Component2 c in components)
            {
                if (c != null)
                {
                    stack.Push(c);
                }
            }

            while (stack.Count > 0)
            {
                Component2 comp = stack.Pop();
                if (comp == null)
                {
                    continue;
                }
                string key = comp.Name2;
                // Dedup by Name2 so a component named in the group AND reached as
                // a descendant of another group member is only shown/hidden once.
                if (key != null && !seen.Add(key))
                {
                    continue;
                }
                result.Add(comp);

                object childrenObj = comp.GetChildren();
                if (childrenObj is object[] children)
                {
                    foreach (object o in children)
                    {
                        Component2 child = o as Component2;
                        if (child != null)
                        {
                            stack.Push(child);
                        }
                    }
                }
            }
            return result;
        }

        public static int GetCount(Link Link)
        {
            int count = 1;
            foreach (Link child in Link.Children)
            {
                count += GetCount(child);
            }
            return count;
        }

        public static int GetCount(TreeNodeCollection nodes)
        {
            int count = 0;
            foreach (LinkNode node in nodes)
            {
                count += 1;
                count += GetCount(node.Nodes);
            }
            return count;
        }

        public static void RetrieveSWComponentPIDs(ModelDoc2 model, LinkNode node)
        {
            // Refresh the per-group PIDs first; the legacy flat lists below are
            // derived from them so older readers keep working.
            if (node.Link.VisualGroups != null)
            {
                foreach (MeshGroup group in node.Link.VisualGroups)
                {
                    group.ComponentPIDs = SaveSWComponents(model, group.Components);
                }
            }
            if (node.Link.CollisionGroups != null)
            {
                foreach (MeshGroup group in node.Link.CollisionGroups)
                {
                    group.ComponentPIDs = SaveSWComponents(model, group.Components);
                }
            }

            // Mirror the flattened component lists into the single-list PID
            // fields used by the fallback component storage path.
            node.Link.SWComponentPIDs = SaveSWComponents(model, node.Link.VisualComponents);
            node.Link.CollisionComponentPIDs = SaveSWComponents(model, node.Link.CollisionComponents);

            if (node.Link.InertialComponents != null)
            {
                node.Link.InertialComponentPIDs = SaveSWComponents(model, node.Link.InertialComponents);
            }
            foreach (LinkNode child in node.Nodes)
            {
                RetrieveSWComponentPIDs(model, child);
            }
        }

        //Converts the SW component references to PIDs
        public static void SaveSWComponents(ModelDoc2 model, Link Link)
        {
            model.ClearSelection2(true);
            byte[] PID = SaveSWComponent(model, Link.SWMainComponent);
            if (PID != null)
            {
                Link.SWMainComponentPID = PID;
            }

            // Persist per-group PIDs first.
            if (Link.VisualGroups != null)
            {
                foreach (MeshGroup group in Link.VisualGroups)
                {
                    group.ComponentPIDs = SaveSWComponents(model, group.Components);
                }
            }
            if (Link.CollisionGroups != null)
            {
                foreach (MeshGroup group in Link.CollisionGroups)
                {
                    group.ComponentPIDs = SaveSWComponents(model, group.Components);
                }
            }

            // Legacy flat lists, kept in sync for older readers.
            Link.SWComponentPIDs = SaveSWComponents(model, Link.VisualComponents);
            Link.CollisionComponentPIDs = SaveSWComponents(model, Link.CollisionComponents);
            Link.InertialComponentPIDs = SaveSWComponents(model, Link.InertialComponents);

            foreach (Link Child in Link.Children)
            {
                SaveSWComponents(model, Child);
            }
        }

        //Converts SW component references to PIDs
        public static List<byte[]> SaveSWComponents(ModelDoc2 model, List<Component2> components)
        {
            List<byte[]> PIDs = new List<byte[]>();
            foreach (Component2 component in components)
            {
                byte[] PID = SaveSWComponent(model, component);
                if (PID != null)
                {
                    PIDs.Add(PID);
                }
            }
            return PIDs;
        }

        public static byte[] SaveSWComponent(ModelDoc2 model, Component2 component)
        {
            if (component != null)
            {
                return model.Extension.GetPersistReference3(component);
            }
            return null;
        }

        // Converts the PIDs to actual references to the components and proceeds recursively
        // through the child nodes
        public static void LoadSWComponents(ModelDoc2 model, LinkNode node, List<string> problemLinks)
        {
            logger.Info("Loading SolidWorks components for " +
                node.Link.Name + " from " + model.GetPathName());

            // Make sure legacy fields have been migrated into VisualGroups /
            // CollisionGroups in case the configuration was constructed by a
            // path that bypassed the DataContract OnDeserialized callback.
            node.Link.MigrateLegacyComponents();

            // Resolve each visual group's components from its PID list. The
            // legacy SWComponentPIDs is a flattened mirror used only by older
            // readers; it is rebuilt from the groups on save.
            int totalVisualPIDs = 0;
            int totalVisualLoaded = 0;
            foreach (MeshGroup group in node.Link.VisualGroups)
            {
                if (group.ComponentPIDs == null)
                {
                    group.ComponentPIDs = new List<byte[]>();
                }
                group.Components = LoadSWComponents(model, group.ComponentPIDs);
                totalVisualPIDs += group.ComponentPIDs.Count;
                totalVisualLoaded += group.Components.Count;
            }
            if (totalVisualLoaded != totalVisualPIDs)
            {
                problemLinks.Add(node.Link.Name);
                logger.Error("Link " + node.Link.Name + " did not fully load all visual components");
            }
            logger.Info("Loaded " + totalVisualLoaded + " visual components for link " + node.Link.Name +
                " across " + node.Link.VisualGroups.Count + " group(s)");

            int totalCollisionPIDs = 0;
            int totalCollisionLoaded = 0;
            foreach (MeshGroup group in node.Link.CollisionGroups)
            {
                if (group.ComponentPIDs == null)
                {
                    group.ComponentPIDs = new List<byte[]>();
                }
                group.Components = LoadSWComponents(model, group.ComponentPIDs);
                totalCollisionPIDs += group.ComponentPIDs.Count;
                totalCollisionLoaded += group.Components.Count;
            }
            if (totalCollisionLoaded != totalCollisionPIDs)
            {
                problemLinks.Add(node.Link.Name);
                logger.Error("Link " + node.Link.Name + " did not fully load all collision components");
            }
            logger.Info("Loaded " + totalCollisionLoaded + " collision components for link " + node.Link.Name +
                " across " + node.Link.CollisionGroups.Count + " group(s)");

            if (node.Link.InertialComponentPIDs == null)
            {
                node.Link.InertialComponentPIDs = new List<byte[]>();
            }
            node.Link.InertialComponents = LoadSWComponents(model, node.Link.InertialComponentPIDs);
            if (node.Link.InertialComponents.Count != node.Link.InertialComponentPIDs.Count)
            {
                problemLinks.Add(node.Link.Name);
                logger.Error("Link " + node.Link.Name + " did not fully load all inertial components");
            }
            logger.Info("Loaded " + node.Link.InertialComponents.Count + " inertial components for link " + node.Link.Name);

            if (node.Link.Sites == null)
            {
                node.Link.Sites = new List<SiteSpec>();
            }

            foreach (LinkNode Child in node.Nodes)
            {
                LoadSWComponents(model, Child, problemLinks);
            }
        }

        // Converts the PIDs to actual references to the components
        public static List<Component2> LoadSWComponents(ModelDoc2 model, List<byte[]> PIDs)
        {
            List<Component2> components = new List<Component2>();
            foreach (byte[] PID in PIDs)
            {
                string byteAsString = PIDToString(PID);
                logger.Info("Loading component with PID " + byteAsString);
                Component2 comp = LoadSWComponent(model, PID);
                if (comp == null)
                {
                    logger.Warn("Component with PID " + byteAsString + " failed to load");
                }
                else
                {
                    components.Add(comp);
                    logger.Info("Successfully loaded component " + comp.GetPathName());
                }
            }
            return components;
        }

        // Converts a single PID to a Component2 object
        public static Component2 LoadSWComponent(ModelDoc2 model, byte[] PID)
        {
            string byteAsString = PIDToString(PID);
            if (PID == null)
            {
                throw new System.Exception("PID " + byteAsString + " was null. Is the configuration corrupted?");    
            }

            object obj = model.Extension.GetObjectByPersistReference3(PID, out int Errors);
            if (Errors == 0)
            {
                return (Component2)obj;
            }
            switch ((swPersistReferencedObjectStates_e)Errors)
            {
                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Deleted:
                    logger.Error("The component associated with PID " + byteAsString + " was deleted");
                    break;

                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Invalid:
                    logger.Error("The component associated with PID " + byteAsString + " was found to be invalid");
                    break;

                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Suppressed:
                    logger.Error("The component associated with PID " + byteAsString + " is suppressed");
                    break;

                case swPersistReferencedObjectStates_e.swPersistReferencedObject_Ok:
                    break;

                default:
                    logger.Error("The component associated with PID " + byteAsString +
                        " was not loaded due to an unspecified error (" + Errors + ")");
                    break;
            }
            return null;
        }

        public static string PIDToString(byte[] pid)
        {
            return Encoding.ASCII.GetString(pid);
        }
    }
}