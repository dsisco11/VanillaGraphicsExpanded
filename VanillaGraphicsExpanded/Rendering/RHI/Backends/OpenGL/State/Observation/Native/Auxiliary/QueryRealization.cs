namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Feeds native query intervals into existing execution knowledge after original dispatch.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Observes equivalent core and ARB unindexed query starts.</summary>
    [InterceptOpenGL("glBeginQuery")]
    [InterceptOpenGL("glBeginQueryARB")]
    internal static void ObserveBeginQuery(int target, uint id)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.ObserveQueryBegin(target, 0, unchecked((int)id));
    }

    /// <summary>Observes target-specific indexed query realization.</summary>
    [InterceptOpenGL("glBeginQueryIndexed")]
    internal static void ObserveBeginQueryIndexed(int target, uint index, uint id)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.ObserveQueryBegin(target, index, unchecked((int)id));
    }

    /// <summary>Releases an unindexed interval without publishing result data.</summary>
    [InterceptOpenGL("glEndQuery")]
    [InterceptOpenGL("glEndQueryARB")]
    internal static void ObserveEndQuery(int target)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.ObserveQueryEnd(target, 0);
    }

    /// <summary>Releases the specified indexed interval without reading its result.</summary>
    [InterceptOpenGL("glEndQueryIndexed")]
    internal static void ObserveEndQueryIndexed(int target, uint index)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.ObserveQueryEnd(target, index);
    }

    /// <summary>Establishes timestamp query identity independently of result availability.</summary>
    [InterceptOpenGL("glQueryCounter")]
    internal static void ObserveQueryCounter(uint id, int target)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.ObserveQueryTimestamp(unchecked((int)id), target);
    }

    /// <summary>Retains an established conditional query for rejection classification.</summary>
    [InterceptOpenGL("glBeginConditionalRender")]
    internal static void ObserveBeginConditionalRender(uint id, int mode)
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.ObserveQueryCondition(unchecked((int)id), mode);
    }

    /// <summary>Ends the conditional query borrow without affecting canonical metadata.</summary>
    [InterceptOpenGL("glEndConditionalRender")]
    internal static void ObserveEndConditionalRender()
    {
        if (OpenGLObservationContext.TryGet(out var cache, out _, false)) cache.EndQueryCondition();
    }
    #endregion
}
