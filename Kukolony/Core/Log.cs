namespace Kukolony.Core
{
    /// <summary>
    ///     Logging facade. Everything goes through here rather than calling a logger
    ///     directly, so output is consistently prefixed and the backing logger can be
    ///     swapped in one place.
    ///
    ///     Never use UnityEngine.Debug.Log or ZLog - their output is indistinguishable
    ///     from vanilla's in LogOutput.log, which makes bug reports useless.
    ///     See docs/modding-basics.md for which level to use when.
    /// </summary>
    internal static class Log
    {
        internal static void Info(string message) => Jotunn.Logger.LogInfo(message);

        internal static void Debug(string message) => Jotunn.Logger.LogDebug(message);

        internal static void Warning(string message) => Jotunn.Logger.LogWarning(message);

        internal static void Error(string message) => Jotunn.Logger.LogError(message);
    }
}
