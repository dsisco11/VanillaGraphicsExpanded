namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established VAO selection without treating selection as a configuration mutation.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Captures existing realization proof before withholding the selected namespace.</summary>
    internal static NativeVertexSelection BeginNativeVertexSelection(int array)
    {
        var cache = EngineStateCalls.CurrentCache;
        bool established = cache.HasEstablishedNativeIdentity(GpuResourceKind.VertexArray, array);
        cache.currentVao = null;
        return new(cache, array, established, Integration.RenderContextRegistry.CaptureResourceContext());
    }
    #endregion

    /// <summary>Retains original context and realization proof across exact native dispatch.</summary>
    internal readonly struct NativeVertexSelection(OpenGlStateCache cache, int array, bool established,
        (nint Handle, long Generation) origin)
    {
        #region Internal API
        /// <summary>Commits only established names on original-context return; genuinely new names need initial acquisition.</summary>
        internal void Complete(bool returned)
        {
            if (!cache.OwnsKnowledgeFor(origin)) return;
            cache.currentVao = returned && established
                && Integration.RenderContextRegistry.IsCurrentResourceContext(origin) ? array : null;
        }
        #endregion
    }
}
