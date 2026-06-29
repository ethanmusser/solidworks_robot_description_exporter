using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;

namespace SW2RD.Utilities
{
    // Nesting-aware, exception-safe wrapper around SolidWorks' single shared
    // UserProgressBar (ISldWorks.GetUserProgressBar). Its only job is to give the
    // user a visible "I am busy" indication during the long, UI-thread-blocking
    // operations in the export PropertyManagerPage (config load, link switch,
    // accordion expand, flexible-subassembly coordinate-system resolve) so the
    // spinning cursor does not look like a crash.
    //
    // Why a single helper:
    //  - GetUserProgressBar returns ONE shared object per SW session; calling
    //    Start() on it while another scope already started it would reset / stack
    //    incorrectly. The PMP slow paths nest synchronously (a tree click runs
    //    SwitchActiveNodes -> FillPropertyManager -> RefreshAxisDirectionPreview),
    //    so the outermost Busy scope owns Start/End and inner scopes only swap the
    //    title and restore it on dispose.
    //  - The export pipeline already drives the same bar directly via its own
    //    ExportHelper.progressBar field. AttachExternal/DetachExternal let that
    //    path register itself so a SetTitle(...) emitted by a shared resolver
    //    (e.g. the in-context coord-sys slow fallback) updates the live export bar
    //    instead of trying to start a competing one.
    //
    // Reliability rule (mirrors the ShowBubbleTooltipAt2 handling in
    // ExportPropertyManager.Export.cs): a progress-bar failure must NEVER break
    // the feature it is decorating. Every SW call is wrapped in try/catch and a
    // failure is logged at Warn and otherwise swallowed.
    public static class SwProgress
    {
        private static readonly log4net.ILog logger = Logger.GetLogger();

        // The active bar, whether we started it (Busy) or it was registered by
        // the export path (AttachExternal). Null when nothing is active.
        private static UserProgressBar current;

        // True only when an outer Busy scope called Start() and is therefore
        // responsible for End(). False when the bar is owned externally (export).
        private static bool ownedByUs;

        // Title nesting stack so an inner scope can restore the outer scope's
        // title on dispose. Only meaningful while ownedByUs is true.
        private static readonly Stack<string> titles = new Stack<string>();

        // Opens a busy scope. Dispose to close it. If no bar is active yet this
        // starts SolidWorks' progress bar with the given title; if one is already
        // active (nested Busy, or an external/export bar) it only swaps the title
        // and restores the prior one on dispose. max is the determinate upper
        // bound for optional Step(...) calls (default: indeterminate single span).
        public static IDisposable Busy(ISldWorks swApp, string title, int max = 0)
        {
            string safeTitle = title ?? "Working...";
            try
            {
                if (current == null)
                {
                    if (swApp == null)
                    {
                        return new Scope(false, false);
                    }
                    swApp.GetUserProgressBar(out current);
                    if (current == null)
                    {
                        return new Scope(false, false);
                    }
                    current.Start(0, max > 0 ? max : 1, safeTitle);
                    ownedByUs = true;
                    titles.Clear();
                    titles.Push(safeTitle);
                    return new Scope(endsBar: true, pushedTitle: true);
                }

                // A bar is already active (nested or external): just retitle.
                titles.Push(safeTitle);
                current.UpdateTitle(safeTitle);
                return new Scope(endsBar: false, pushedTitle: true);
            }
            catch (Exception ex)
            {
                logger.Warn("SwProgress.Busy failed: " + ex.GetType().Name + ": " + ex.Message);
                // Reset state so a partial failure cannot wedge later scopes.
                current = null;
                ownedByUs = false;
                titles.Clear();
                return new Scope(false, false);
            }
        }

        // Replaces the current scope's title (e.g. phase changes during a loop, or
        // a slow-path notice surfaced from deep inside a resolver). No-op when no
        // bar is active.
        public static void SetTitle(string title)
        {
            if (current == null)
            {
                return;
            }
            try
            {
                string safeTitle = title ?? "Working...";
                if (titles.Count > 0)
                {
                    titles.Pop();
                    titles.Push(safeTitle);
                }
                current.UpdateTitle(safeTitle);
            }
            catch (Exception ex)
            {
                logger.Warn("SwProgress.SetTitle failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Advances the determinate progress position. No-op when no bar is active.
        public static void Step(int position)
        {
            if (current == null)
            {
                return;
            }
            try
            {
                current.UpdateProgress(position);
            }
            catch (Exception ex)
            {
                logger.Warn("SwProgress.Step failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Registers an already-started, externally-owned progress bar (the export
        // pipeline's ExportHelper.progressBar) so SetTitle/Step route to it. The
        // caller retains ownership of Start/End; SwProgress will not end it.
        public static void AttachExternal(UserProgressBar bar)
        {
            if (bar == null)
            {
                return;
            }
            current = bar;
            ownedByUs = false;
            titles.Clear();
        }

        // Unregisters the externally-owned bar. Safe to call unconditionally; only
        // clears state when the active bar is external (not one we started).
        public static void DetachExternal()
        {
            if (!ownedByUs)
            {
                current = null;
                titles.Clear();
            }
        }

        private sealed class Scope : IDisposable
        {
            private readonly bool endsBar;
            private readonly bool pushedTitle;
            private bool disposed;

            public Scope(bool endsBar, bool pushedTitle)
            {
                this.endsBar = endsBar;
                this.pushedTitle = pushedTitle;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;

                try
                {
                    if (pushedTitle && titles.Count > 0)
                    {
                        titles.Pop();
                    }

                    if (endsBar)
                    {
                        current?.End();
                        current = null;
                        ownedByUs = false;
                        titles.Clear();
                    }
                    else if (current != null && titles.Count > 0)
                    {
                        // Restore the enclosing scope's title.
                        current.UpdateTitle(titles.Peek());
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("SwProgress.Scope.Dispose failed: " +
                        ex.GetType().Name + ": " + ex.Message);
                    // Best-effort recovery: if we were the owner, drop state so the
                    // next Busy starts cleanly.
                    if (endsBar)
                    {
                        current = null;
                        ownedByUs = false;
                        titles.Clear();
                    }
                }
            }
        }
    }
}
