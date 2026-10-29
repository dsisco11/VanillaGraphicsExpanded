namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes feedback factory outputs while native arrays remain valid.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains generated reservations without claiming initialized activity.</summary>
    [InterceptOpenGL("glGenTransformFeedbacks")]
    internal static void ObserveGenTransformFeedbacks(int count, uint* names) => ObserveFeedbackNames(count, names, false);

    /// <summary>Publishes created empty schemas and guaranteed inactive defaults exactly once.</summary>
    [InterceptOpenGL("glCreateTransformFeedbacks")]
    internal static void ObserveCreateTransformFeedbacks(int count, uint* names) => ObserveFeedbackNames(count, names, true);
    #endregion

    #region Private
    /// <summary>Ignores invalid outputs and surviving names rather than replacing their identities.</summary>
    private static void ObserveFeedbackNames(int count, uint* names, bool realized)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var manager = GpuResourceManagerSystem.CurrentManager!;
        for (int index = 0; index < count; index++)
        {
            int id = unchecked((int)names[index]);
            if (manager.ObserveEmptyCreation<GpuTransformFeedbackMetadata>(GpuResourceKind.TransformFeedback, id, realized) && realized)
                cache.RecordFeedbackCreated(id);
        }
    }
    #endregion
}
