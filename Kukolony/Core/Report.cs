namespace Kukolony.Core
{
    /// <summary>
    ///     The one place the mod tells the player what just happened.
    /// </summary>
    /// <remarks>
    ///     Every action reports its outcome through here, including refusal and why. Silence is
    ///     the failure mode this exists to prevent: a player who cannot tell "that is not
    ///     registerable" from "you missed" learns nothing from trying again.
    ///
    ///     Where it appears depends on whether the player is looking at the screen. A centre
    ///     message over the world is right when they are standing in the settlement and wrong
    ///     when a panel is covering it, so the screen takes the line when it is open. Both are
    ///     logged either way, because the benchmark reads the log and nobody reads a message
    ///     that was drawn for two seconds during an automated run.
    /// </remarks>
    internal static class Report
    {
        /// <summary>
        ///     Set by the screen while it is open, so this file does not depend on the GUI.
        /// </summary>
        internal static System.Action<string> Listener { get; set; }

        /// <summary>The most recent line, for checks that assert what was said.</summary>
        internal static string Last { get; private set; } = string.Empty;

        internal static void Say(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            Last = message;
            Log.Info("[report] " + message);

            if (Listener != null)
            {
                Listener(message);
                return;
            }

            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, message);
            }
        }
    }
}
