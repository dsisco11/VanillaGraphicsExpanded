namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Associates the one thread-local knowledge store with its currently admitted native namespace.</summary>
internal sealed partial class OpenGlStateCache
{
    private nint rhiNativeContext;
    private (nint Handle, long Generation) rhiContextLifetime;

    #region Internal API
    /// <summary>Returns the existing knowledge owner for recovery if context selection itself fails.</summary>
    internal (nint Handle, long Generation) KnowledgeContext => (rhiNativeContext, rhiContextLifetime.Generation);

    /// <summary>Checks which namespace currently owns this thread-local store without consulting a foreign native context.</summary>
    internal bool OwnsKnowledgeFor((nint Handle, long Generation) origin) => rhiNativeContext == origin.Handle
        && (origin.Generation == 0 || rhiContextLifetime == origin);

    /// <summary>Withdraws executed knowledge when switching contexts instead of reusing another namespace's bindings.</summary>
    internal void SelectRHIContext(nint nativeContext, (nint Handle, long Generation) lifetime)
    {
        if (rhiNativeContext == nativeContext && rhiContextLifetime == lifetime) return;
        InvalidateAll();
        feedbackActivity.Clear();
        pipelineUniformPrograms.Clear();
        // Scope tokens retain their own restoration values. Abandon the old namespace's
        // stack frames here so stale tokens need not mutate a newly admitted stack.
        VaoScopeTracker.ClearThreadStack();
        ProgramPipelineScopeTracker.ClearThreadStack();
        RenderbufferScopeTracker.ClearThreadStack();
        rhiNativeContext = nativeContext;
        rhiContextLifetime = lifetime;
    }
    #endregion
}
