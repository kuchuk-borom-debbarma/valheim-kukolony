namespace Kukolony.Jobs
{
    /// <summary>Outcome of one tick of a job step.</summary>
    internal enum StepStatus
    {
        /// <summary>Still working. Tick again next frame.</summary>
        Running,

        /// <summary>Done. Advance to the next step.</summary>
        Succeeded,

        /// <summary>Cannot proceed. Restart the cycle after a cooldown.</summary>
        Failed
    }
}
