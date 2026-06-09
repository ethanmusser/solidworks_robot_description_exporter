/*
Copyright (c) 2026 Ethan J. Musser

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
using SolidWorks.Interop.swpublished;
using SW2RD.Input;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace SW2RD.Export
{
    // Pre-export validation: per-link "is this link complete?" checks
    // (CheckNodeXxxComplete + CheckNodesComplete), unresolved-component
    // detection (CheckModelDocsExist), and link / joint name uniqueness
    // (CheckIfNamesAreUnique). Carved out of ExportPropertyManagerExtension
    // as part of the Phase 1 partial-class refactor; one concern per file
    // makes locating a specific validator easier.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        private void CheckNodeInertialComplete(LinkNode node)
        {
            if (node.Nodes.Count > 0 && node.Link.SWComponents.Count == 0)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Links with children cannot be empty. Select its associated components\r\n";
            }
        }

        private void CheckNodeVisualComplete(LinkNode node)
        {
            if (node.Nodes.Count > 0 && node.Link.SWComponents.Count == 0)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Links with children cannot be empty. Select its associated components\r\n";
            }
        }

        private void CheckNodeJointComplete(LinkNode node)
        {
            string jointType = node.Link.Joint.Type ?? "";
            if (!IsSupportedUserJointType(jointType))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Joint type is empty or unsupported. Choose fixed, revolute, or prismatic.\r\n";
            }

            if (node.Link.SWComponents.Count == 0 && node.Link.Joint.CoordinateSystemName == "Automatically Generate")
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        The origin reference coordinate system cannot be automatically generated\r\n" +
                    "        without components. Either select an origin or at least one component.\r\n";
            }

            if (node.Link.SWComponents.Count == 0 && node.Link.Joint.AxisName == "Automatically Generate")
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        The reference axis cannot be automatically generated\r\n" +
                    "        without components. Either select an axis or at least one component.\r\n";
            }

            // A reference-axis-sourced moving joint needs a picked axis. A
            // coordinate-system basis axis or auto-derive resolves without one.
            if (jointType != "fixed" &&
                node.Link.Joint.AxisSource == JointAxisSource.ReferenceAxis &&
                string.IsNullOrWhiteSpace(node.Link.Joint.AxisName))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Joint axis is empty. Pick a reference axis, a coordinate-system axis, " +
                    "or enable auto-derive axis from the kinematic chain.\r\n";
            }

            // A coordinate-system basis axis needs the joint coordinate system
            // it draws the basis vector from.
            if (jointType != "fixed" &&
                node.Link.Joint.UsesCoordinateSystemAxis &&
                string.IsNullOrWhiteSpace(node.Link.Joint.CoordinateSystemName))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        Joint axis uses a coordinate-system basis vector but no coordinate " +
                    "system is selected. Pick a coordinate system.\r\n";
            }

            if (node.Link.SWComponents.Count == 0 &&
                (node.Link.Joint.Type == "Automatically Generate" || node.Link.Joint.Type == "Automatically Detect"))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete +=
                    "        The joint type cannot be automatically detected\r\n" +
                    "        without components. Choose fixed, revolute, or prismatic.";
            }
        }

        private static bool IsSupportedUserJointType(string jointType)
        {
            return jointType == "fixed" || jointType == "revolute" || jointType == "prismatic";
        }

        // Sets the node's IsIncomplete flag if the node has key items that
        // need to be completed. The PMPage uses IsIncomplete to drive a
        // visual indicator on each tree node and to gate Preview/Export.
        public void CheckNodeComplete(LinkNode node)
        {
            node.WhyIncomplete = "";
            node.IsIncomplete = false;

            // The WorldNode is not a link in the URDF/MJCF sense - it has
            // no name requirement (the underlying sentinel Link is named
            // "world" by convention) and no joint. Skip the link/joint
            // name + joint-completeness checks entirely.
            if (node is WorldNode)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(node.Link.Name))
            {
                node.IsIncomplete = true;
                node.WhyIncomplete += "        Link name is empty. Fill in a unique link name\r\n";
            }
            // Joint name only required for nested links - top-level bodies
            // attach to the world via WorldAttachment (not via an incoming
            // <joint>), so their Link.Joint.Name is intentionally empty.
            bool requiresJointName = !node.IsBaseNode && !node.IsTopLevelBody;
            if (string.IsNullOrWhiteSpace(node.Link.Joint.Name) && requiresJointName)
            {
                node.IsIncomplete = true;
                node.WhyIncomplete += "        Joint name is empty. Fill in a unique joint name\r\n";
            }

            CheckNodeInertialComplete(node);
            CheckNodeVisualComplete(node);

            // Joint shape checks (parent/child consistency, sentinel
            // strings) only meaningful for nested links.
            if (requiresJointName)
            {
                CheckNodeJointComplete(node);
            }
        }

        // After the user clicks Preview/Export but before the pipeline
        // tries to read transforms / mass props from each component, walk
        // every selected component for every link and report any whose
        // ModelDoc2 is null. Lightweight components in unresolved bodies
        // surface here, as do components that the user deleted in SW
        // since the last save without first updating the PM tree.
        private void CheckModelDocsExist(LinkNode node, List<string> problemComponents)
        {
            CheckModelDocsExistFor(node.Link.VisualComponents, problemComponents);
            CheckModelDocsExistFor(node.Link.CollisionComponents, problemComponents);
            CheckModelDocsExistFor(node.Link.InertialComponents, problemComponents);

            foreach (LinkNode child in node.Nodes)
            {
                CheckModelDocsExist(child, problemComponents);
            }
        }

        private static void CheckModelDocsExistFor(List<Component2> components, List<string> problemComponents)
        {
            if (components == null)
            {
                return;
            }
            foreach (Component2 component in components)
            {
                ModelDoc2 doc = component.GetModelDoc2();
                if (doc == null)
                {
                    problemComponents.Add(component.Name2);
                }
            }
        }

        // Recursive function to iterate through nodes and build a message
        // containing those that are incomplete.
        public string CheckNodesComplete(LinkNode node, string incompleteNodes)
        {
            CheckNodeComplete(node);
            if (node.IsIncomplete)
            {
                incompleteNodes += "    '" + node.Text + "':\r\n" + node.WhyIncomplete + "\r\n\r\n";
            }
            foreach (LinkNode child in node.Nodes)
            {
                incompleteNodes = CheckNodesComplete(child, incompleteNodes);
            }
            return incompleteNodes;
        }

        // Finds all the nodes in a TreeView that need to be completed
        // before exporting. Returns true if everything is complete; on
        // failure, fires a MessageBox with the per-node breakdown.
        // Called by ExportButtonPress as the second pre-close validator
        // (after CheckIfNamesAreUnique).
        public bool CheckNodesComplete(TreeView tree)
        {
            string incompleteNodes = CheckNodesComplete((LinkNode)tree.Nodes[0], "");
            if (!string.IsNullOrWhiteSpace(incompleteNodes))
            {
                MessageBox.Show(
                    "The following nodes are incomplete. You need to fix them before continuing.\r\n\r\n" + incompleteNodes);
                return false;
            }
            return true;
        }

        public void CheckIfLinkNamesAreUnique(LinkNode node, string linkName, List<string> conflict)
        {
            if (node.Link.Name == linkName)
            {
                conflict.Add(node.Link.Name);
            }

            foreach (LinkNode child in node.Nodes)
            {
                CheckIfLinkNamesAreUnique(child, linkName, conflict);
            }
        }

        public void CheckIfJointNamesAreUnique(LinkNode node, string jointName, List<string> conflict)
        {
            // Skip the WorldNode and any top-level body: they don't emit a
            // <joint> element so their (empty) Joint.Name should not flag
            // a uniqueness conflict against a nested link's empty
            // pre-fill or against another top-level body.
            bool participatesInJointNames = !(node is WorldNode) && !node.IsTopLevelBody;
            if (participatesInJointNames && node.Link.Joint.Name == jointName)
            {
                conflict.Add(node.Link.Joint.Name);
            }
            foreach (LinkNode child in node.Nodes)
            {
                // Recursive descent uses the link-name walker by intent
                // (the original code's "CheckIfLinkNamesAreUnique" call
                // here is a copy-paste artifact, retained to avoid a
                // behavior change).
                CheckIfLinkNamesAreUnique(child, jointName, conflict);
            }
        }

        // Top-level entry point for the link / joint name uniqueness
        // check. Returns true if every link name is unique AND every
        // joint name is unique; on failure, fires a MessageBox listing
        // the conflicts. Called by ExportButtonPress as the first
        // pre-close validator.
        public bool CheckIfNamesAreUnique(LinkNode node)
        {
            List<List<string>> linkConflicts = new List<List<string>>();
            List<List<string>> jointConflicts = new List<List<string>>();
            CheckIfLinkNamesAreUnique(node, node, linkConflicts);
            CheckIfJointNamesAreUnique(node, node, jointConflicts);

            string message = "\r\nPlease fix these errors before proceeding.";
            string specificErrors = "";
            bool displayInitialMessage = true;
            bool linkNamesInConflict = false;
            foreach (List<string> conflict in linkConflicts)
            {
                if (conflict.Count > 1)
                {
                    linkNamesInConflict = true;
                    if (displayInitialMessage)
                    {
                        specificErrors +=
                            "The following links have LINK names that conflict:\r\n\r\n";
                        displayInitialMessage = false;
                    }
                    bool isFirst = true;
                    foreach (string linkName in conflict)
                    {
                        specificErrors += (isFirst) ? "     " + linkName : ", " + linkName;
                        isFirst = false;
                    }
                    specificErrors += "\r\n";
                }
            }
            displayInitialMessage = true;
            foreach (List<string> conflict in jointConflicts)
            {
                if (conflict.Count > 1)
                {
                    linkNamesInConflict = true;
                    if (displayInitialMessage)
                    {
                        specificErrors +=
                            "The following links have JOINT names that conflict:\r\n\r\n";
                        displayInitialMessage = false;
                    }
                    bool isFirst = true;
                    foreach (string linkName in conflict)
                    {
                        specificErrors += (isFirst) ? "     " + linkName : ", " + linkName;
                        isFirst = false;
                    }
                    specificErrors += "\r\n";
                }
            }
            if (linkNamesInConflict)
            {
                MessageBox.Show(specificErrors + message);
                return false;
            }
            return true;
        }

        public void CheckIfLinkNamesAreUnique(
            LinkNode basenode, LinkNode currentNode, List<List<string>> conflicts)
        {
            List<string> conflict = new List<string>();

            CheckIfLinkNamesAreUnique(basenode, currentNode.Link.Name, conflict);
            bool alreadyExists = false;
            foreach (List<string> existingConflict in conflicts)
            {
                if (existingConflict.Contains(conflict[0]))
                {
                    alreadyExists = true;
                }
            }
            if (!alreadyExists)
            {
                conflicts.Add(conflict);
            }
            foreach (LinkNode child in currentNode.Nodes)
            {
                CheckIfLinkNamesAreUnique(basenode, child, conflicts);
            }
        }

        public void CheckIfJointNamesAreUnique(
            LinkNode basenode, LinkNode currentNode, List<List<string>> conflicts)
        {
            List<string> conflict = new List<string>();

            CheckIfJointNamesAreUnique(basenode, currentNode.Link.Joint.Name, conflict);
            bool alreadyExists = false;
            foreach (List<string> existingConflict in conflicts)
            {
                if (conflict.Count > 0 && existingConflict.Contains(conflict[0]))
                {
                    alreadyExists = true;
                }
            }

            if (!alreadyExists)
            {
                conflicts.Add(conflict);
            }
            foreach (LinkNode child in currentNode.Nodes)
            {
                CheckIfJointNamesAreUnique(basenode, child, conflicts);
            }
        }
    }
}
