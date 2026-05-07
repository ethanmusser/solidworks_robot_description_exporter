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

using SolidWorks.Interop.swpublished;
using SW2URDF.URDF;
using SW2URDF.Utilities;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace SW2URDF.URDFExport
{
    // WinForms TreeView event handlers: select / drag / drop, plus the
    // context-menu callbacks for add / remove / rename child. The tree is
    // hosted inside the PMPage via a swControlType_WindowFromHandle. Split
    // out of ExportPropertyManager.cs as part of the Phase 1 partial-class
    // refactor; no behavior changes.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
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
    }
}
