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
using SW2RD.Input;
using System;

namespace SW2RD.Export
{
    // PMPage handlers for the joint-axis "Reverse Direction" bitmap button
    // and the live preview arrow drawn in the SW viewport via
    // ExportHelper.DrawAxisOverlay.
    public sealed partial class ExportPropertyManager : PropertyManagerPage2Handler9, IDisposable
    {
        // Handler for the "Reverse Direction" bitmap button next to the
        // Reference Axis combobox. swControlType_BitmapButton fires CLICK
        // events (not check events) so we maintain the toggle state ourselves
        // in currentAxisFlipped. The new state is written through to the
        // active node's Joint.AxisFlipped immediately (rather than waiting
        // for SaveActiveNode) so the persisted state and the redrawn overlay
        // arrow stay in lockstep without needing a "dirty" flag.
        //
        // The preview redraw MUST be deferred for the same reason as the
        // OnSelectionboxListChanged path: RefreshAxisDirectionPreview ->
        // EstimateAxis -> GetRefAxis can run WithComponentConfiguration
        // (an expensive part-config switch on sub-component axes), and
        // DrawAxisOverlay creates / removes a SW manipulator. Doing
        // those synchronously on SW's button-event dispatch thread
        // blocks the UI for seconds and triggers the "not responding"
        // hang loop. DeferRefreshAxisPreview routes the work through
        // the WinForms message pump so the SW button handler returns
        // immediately.
        private void ToggleAxisFlip()
        {
            FlipPersistedAxisDirection();
            DeferRefreshAxisPreview();
        }

        // Manipulator-callback variant of ToggleAxisFlip. The SW arrow
        // manipulator fires OnDirectionFlipped while it is still mid-update
        // on SW's render thread, so redraw work must be deferred to avoid
        // re-entering Manipulator.Remove() on the same manipulator.
        // We split the work in two:
        //   * Synchronously update the persisted flip state. SW has already
        //     visually flipped the arrow before this callback fires (with
        //     AllowFlip=true) so no redraw is necessary to match what the
        //     user sees.
        //   * Schedule a deferred redraw via DeferRefreshAxisPreview so any
        //     subsequent state changes (e.g. the user picks a different axis)
        //     start from a clean overlay. The deferred call runs after the
        //     current SW dispatch frame returns, so the manipulator that
        //     fired this callback has already been released by SolidWorks
        //     before we touch the COM API again.
        private void OnAxisOverlayDirectionFlipped()
        {
            FlipPersistedAxisDirection();
            DeferRefreshAxisPreview();
        }

        // Schedules RefreshAxisDirectionPreview to run AFTER the current SW
        // dispatch frame returns. Required from any SW-event handler that
        // would otherwise re-enter the SW selection / manipulator API
        // mid-event - notably OnSelectionboxListChanged for the joint
        // coord-sys / joint axis SelectionBoxes, where a synchronous
        // preview wipes the user's pick out of the SelectionBox via
        // EstimateAxis -> ClearSelection2 (and historically crashed SW
        // outright when the manipulator create+remove dance ran inside
        // a selection event).
        //
        // Uses the WinForms TreeView's message pump (the only WinForms
        // control we own that has a live HWND for the lifetime of the
        // PMP) so the deferred Action runs on the UI thread without us
        // having to spin up our own SyncContext.
        private void DeferRefreshAxisPreview()
        {
            int seq = ++axisPreviewLogSeq;

            // Re-entrancy guard: collapse multiple Defer calls between the
            // SW dispatch frame returning and the WinForms message pump
            // running our queued refresh into a single pending refresh.
            // See `axisPreviewRefreshPending` field doc for the loop this
            // breaks (Manipulator.Show -> OnSelectionboxListChanged ->
            // re-commit -> Defer -> Manipulator.Show -> ...).
            if (axisPreviewRefreshPending)
            {
                logger.Info("[#" + seq + "] DeferRefreshAxisPreview: SKIP (refresh already pending)");
                return;
            }

            try
            {
                System.Windows.Forms.TreeView tree = Tree;
                if (tree != null && tree.IsHandleCreated && !tree.IsDisposed)
                {
                    axisPreviewRefreshPending = true;
                    logger.Info("[#" + seq + "] DeferRefreshAxisPreview: queued via Tree.BeginInvoke");
                    tree.BeginInvoke((Action)RefreshAxisDirectionPreview);
                }
                else
                {
                    logger.Info("[#" + seq + "] DeferRefreshAxisPreview: SKIP (Tree handle unavailable)");
                }
            }
            catch (Exception ex)
            {
                // BeginInvoke can throw InvalidOperationException if the
                // window handle was destroyed between the IsHandleCreated
                // check and the call. Drop the redraw rather than crash;
                // any persisted state the caller wrote remains correct.
                axisPreviewRefreshPending = false;
                logger.Warn("[#" + seq + "] DeferRefreshAxisPreview: deferred redraw skipped: " + ex.Message);
            }
        }

        // Toggles currentAxisFlipped + Joint.AxisFlipped on the active
        // (non-base) link. No UI side effects.
        private void FlipPersistedAxisDirection()
        {
            currentAxisFlipped = !currentAxisFlipped;

            LinkNode active = (LinkNode)(Tree?.SelectedNode);
            if (active != null && !active.IsBaseNode && active.Link != null && active.Link.Joint != null)
            {
                active.Link.Joint.AxisFlipped = currentAxisFlipped;
            }
        }

        // Re-resolves the joint coord-sys + axis (with the current flip state)
        // and (re)draws the overlay arrow in the SW viewport. Called whenever
        // the user picks a coord-sys or axis in the SelectionBox, toggles
        // the flip button or auto-derive checkbox, or switches links in
        // the tree. Pure UI side effect: does NOT mutate any Joint state -
        // that lives on currentAxisFlipped / Joint.AxisFlipped /
        // Joint.AutoDeriveAxis and is persisted by the per-event handlers
        // and SaveActiveNode.
        //
        // Reads coord-sys / axis names directly off the active node.
        // With AutoDeriveAxis = true the axis name is
        // intentionally empty and PreviewAxisDirection short-circuits to
        // IsValid = false so no overlay is drawn (the export-time path
        // resolves the axis from the SW kinematic chain at that point).
        private void RefreshAxisDirectionPreview()
        {
            int seq = ++axisPreviewLogSeq;
            logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: enter");

            // Clear the re-entrancy guard on completion (try/finally), NOT
            // on entry. Clearing on entry would let any DeferRefreshAxisPreview
            // call fired DURING this refresh (notably from
            // OnSelectionboxListChanged events that DrawAxisOverlay's
            // Manipulator.Show provokes - see axisPreviewRefreshPending
            // field doc) re-queue, restarting the loop we are trying to
            // break. Clearing on completion drops every Defer call that
            // happens in the window from "this method started running"
            // to "this method returned"; the next legitimate Defer (e.g.
            // the user picks a different coord-sys after the refresh
            // finished) queues a fresh refresh as expected.
            try
            {
                LinkNode active = (LinkNode)(Tree?.SelectedNode);
                if (active == null || active.IsBaseNode ||
                    active.Link == null || active.Link.Joint == null)
                {
                    logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: clearing overlay (no active joint node)");
                    Exporter.ClearAxisOverlay();
                    return;
                }

                string axisName = active.Link.Joint.AxisName ?? "";
                string coordSysName = active.Link.Joint.CoordinateSystemName ?? "";
                logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: link='" + (active.Link.Name ?? "") +
                            "' coordSys='" + coordSysName + "' axis='" + axisName +
                            "' flipped=" + currentAxisFlipped);

                // Do not call Extension.SelectByID2 here to color the picked
                // axis line. With SingleEntityOnly feature pickers, a focused
                // SelectionBox can route append=true / mark=-1 SelectByID2
                // calls through its active filter and block waiting for a
                // compatible pick
                // routed through its own filter, then enters a nested
                // modal message-pump (SW's RootHwndWatch shows
                // GetMessageW + Dispatcher.PushFrameImpl) waiting on a
                // compatible selection that never arrives - SW hangs
                // indefinitely. Stack trace pinpoints SelectByID2 on the
                // managed/native boundary. The overlay arrow drawn by
                // DrawAxisOverlay below is the primary visual feedback;
                // SW's selection-color highlight was a nicety, not
                // load-bearing.

                logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: calling PreviewAxisDirection");
                ExportHelper.AxisPreview preview =
                    Exporter.PreviewAxisDirection(coordSysName, axisName, currentAxisFlipped);
                logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: PreviewAxisDirection returned IsValid=" + preview.IsValid);

                if (!preview.IsValid)
                {
                    logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: clearing overlay (preview invalid)");
                    Exporter.ClearAxisOverlay();
                    return;
                }

                logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: calling DrawAxisOverlay");
                Exporter.DrawAxisOverlay(preview.OriginGlobal, preview.AxisGlobal);
                logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: DrawAxisOverlay returned");
            }
            catch (Exception ex)
            {
                logger.Warn("[#" + seq + "] RefreshAxisDirectionPreview: threw " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                axisPreviewRefreshPending = false;
                logger.Info("[#" + seq + "] RefreshAxisDirectionPreview: exit (pending cleared)");
            }
        }
    }
}
