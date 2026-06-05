using log4net;
using SW2RD.Utilities;
using System.Windows.Forms;

namespace SW2RD.UI
{
    /// <summary>
    /// Process-wide gate for user-facing modal dialogs raised from the export /
    /// configuration code paths.
    ///
    /// In the interactive add-in <see cref="SuppressInteractivePrompts"/> is
    /// <c>false</c>, so every call behaves exactly like a direct
    /// <see cref="MessageBox"/> call - same message, caption, buttons, and
    /// return value. In an UNATTENDED context (the SW-attached xUnit suite under
    /// TestRunner) a modal MessageBox has no one to dismiss it and deadlocks the
    /// test thread while SOLIDWORKS keeps pumping messages (the classic
    /// "Responding = True but hung" symptom). The fixture flips this flag so the
    /// same calls log-and-return their non-blocking default instead of blocking.
    ///
    /// Keep all export/config-path popups routed through here rather than calling
    /// <see cref="MessageBox"/> directly, or a new error path will silently
    /// re-introduce the unattended-test hang.
    /// </summary>
    public static class UserNotifier
    {
        private static readonly ILog logger = Logger.GetLogger();

        /// <summary>
        /// When true, informational dialogs are skipped and prompts return their
        /// caller-supplied default. Set by the SW-attached test fixture so the
        /// suite runs headless. Defaults to false (interactive add-in).
        /// </summary>
        public static bool SuppressInteractivePrompts = false;

        /// <summary>Informational popup (no meaningful return value).</summary>
        public static void Show(string message)
        {
            if (SuppressInteractivePrompts)
            {
                logger.Warn("[dialog suppressed] " + message);
                return;
            }
            MessageBox.Show(message);
        }

        /// <summary>
        /// Modal prompt whose result drives control flow. When suppressed, logs
        /// and returns <paramref name="suppressedDefault"/> so the unattended
        /// path takes a deterministic, non-blocking branch.
        /// </summary>
        public static DialogResult Ask(
            string message,
            string caption,
            MessageBoxButtons buttons,
            DialogResult suppressedDefault)
        {
            if (SuppressInteractivePrompts)
            {
                logger.Warn("[prompt suppressed -> " + suppressedDefault + "] " + message);
                return suppressedDefault;
            }
            return MessageBox.Show(message, caption, buttons);
        }
    }
}
