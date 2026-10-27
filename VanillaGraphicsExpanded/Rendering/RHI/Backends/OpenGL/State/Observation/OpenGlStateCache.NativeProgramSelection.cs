namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Preserves program selectors for subsequent bound metadata observation.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Withdraws exact-overload selector knowledge without publishing resource metadata.</summary>
    internal static NativeProgramSelection BeginNativeProgramSelection(bool pipeline)
    {
        var cache = Current;
        if (pipeline) cache.currentProgramPipeline = null;
        else
        {
            cache.currentProgram = null;
            cache.currentProgramStagesKnown = false;
        }
        return new(cache, pipeline, Integration.RenderContextRegistry.Current());
    }
    #endregion

    /// <summary>Retains the original cache across native dispatch without inventing accepted selections.</summary>
    internal readonly struct NativeProgramSelection(OpenGlStateCache cache, bool pipeline, (nint Handle, long Generation) context)
    {
        #region Internal API
        /// <summary>Leaves the affected selector unknown after successful or failed native execution.</summary>
        internal void Complete(bool returned)
        {
            if (pipeline) cache.currentProgramPipeline = null;
            else
            {
                cache.currentProgram = null;
                cache.currentProgramStagesKnown = false;
            }
        }
        #endregion
    }
}
