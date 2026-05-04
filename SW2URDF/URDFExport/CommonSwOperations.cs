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
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
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

        //Finds the selected components and returns them, used when pulling the items from
        // the selection box because it would be too hard for SolidWorks to allow you to
        // access the selectionbox components directly.
        public static void GetSelectedComponents(
            ModelDoc2 model, List<Component2> Components, int Mark = -1)
        {
            SelectionMgr selectionManager = model.SelectionManager;
            Components.Clear();
            for (int i = 0; i < selectionManager.GetSelectedObjectCount2(Mark); i++)
            {
                object obj = selectionManager.GetSelectedObject6(i + 1, Mark);
                Component2 comp = (Component2)obj;
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
                Component2 comp = (Component2)obj;
                if (comp.IsHidden(false))
                {
                    hiddenComp.Add(comp.Name2);
                }
            }
            return hiddenComp;
        }

        //Except for an exclusionary list, this shows all the components
        public static void ShowAllComponents(ModelDoc2 model, List<string> hiddenComponents)
        {
            AssemblyDoc assyDoc = (AssemblyDoc)model;
            List<Component2> componentsToShow = new List<Component2>();
            object[] varComps = assyDoc.GetComponents(false);
            foreach (Component2 comp in varComps)
            {
                if (!hiddenComponents.Contains(comp.Name2))
                {
                    componentsToShow.Add(comp);
                }
            }
            ShowComponents(model, componentsToShow);
        }

        //Shows the components in the list. Useful  for exporting STLs
        public static void ShowComponents(ModelDoc2 model, List<Component2> components)
        {
            SelectComponents(model, components, true);
            model.ShowComponent2();
        }

        //Hides the components from a list
        public static void HideComponents(ModelDoc2 model, List<Component2> components)
        {
            SelectComponents(model, components, true);
            model.HideComponent2();
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

            // Mirror the flattened component lists into the legacy single-list
            // PID fields so previous versions of this addin (which don't know
            // about VisualGroups) still see the components on a re-read.
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