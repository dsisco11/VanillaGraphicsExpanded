namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves program selectors for subsequent bound metadata observation.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Withdraws exact-overload selector knowledge without publishing resource metadata.</summary>
    internal static NativeProgramSelection BeginNativeProgramSelection(bool pipeline)
    {
        var cache = Current;
        if (pipeline) cache.currentProgramPipeline = null;
        else
        {
            ProgramScopeTracker.ForgetCurrent();
            cache.currentProgram = null;
            cache.currentProgramStagesKnown = false;
        }
        return new(cache, pipeline, Integration.RenderContextRegistry.Current());
    }
    #endregion

    /// <summary>Retains the original cache across native dispatch without inventing accepted selections.</summary>
    internal readonly struct NativeProgramSelection(StateCache cache, bool pipeline, (nint Handle, long Generation) context)
    {
        #region Internal API
        /// <summary>Leaves the affected selector unknown after successful or failed native execution.</summary>
        internal void Complete(bool returned)
        {
            if (pipeline) cache.currentProgramPipeline = null;
            else
            {
                // A native dispatch can reenter and admit an intermediate managed owner.
                // Never withdraw the owner of a different context selected during dispatch.
                if (context == Integration.RenderContextRegistry.Current()) ProgramScopeTracker.ForgetCurrent();
                cache.currentProgram = null;
                cache.currentProgramStagesKnown = false;
            }
        }
        #endregion
    }
}
