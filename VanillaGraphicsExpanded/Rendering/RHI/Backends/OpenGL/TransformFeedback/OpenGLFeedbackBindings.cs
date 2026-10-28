using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes capture selection using the singular existing selector cache.</summary>
internal static class OpenGLFeedbackBindings
{
    #region Internal API
    /// <summary>Acquires initially unknown external selection without entering managed RHI command admission.</summary>
    internal static int GetExternalSelection(OpenGlStateCache cache)
    {
        if (cache.TransformFeedbackKnowledge is { } known) return known;
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        int selected = GL.GetInteger(GetPName.TransformFeedbackBinding);
        if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new System.InvalidOperationException("Feedback discovery returned in a foreign context.");
        cache.TransformFeedbackKnowledge = selected;
        return selected;
    }

    /// <summary>Acquires native activity once when history cannot establish the selected object's outcome.</summary>
    internal static (bool Active, bool Paused) GetExternalActivity(OpenGlStateCache cache)
    {
        if (cache.TryGetFeedbackActivity(out var known)) return known;
        _ = GetExternalSelection(cache);
        var origin = Integration.RenderContextRegistry.CaptureResourceContext();
        bool active = GL.GetBoolean((GetPName)All.TransformFeedbackActive);
        bool paused = GL.GetBoolean((GetPName)All.TransformFeedbackPaused);
        if (!Integration.RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new System.InvalidOperationException("Feedback discovery returned in a foreign context.");
        cache.RecordFeedbackActivity(active, paused);
        return (active, paused);
    }

    /// <summary>Recovers unknown selection once without inventing a second cache.</summary>
    internal static int Get(OpenGlStateCache cache)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        if (cache.TransformFeedbackKnowledge is { } known) return known;
        int selected = GL.GetInteger(GetPName.TransformFeedbackBinding);
        backend.RequireContext();
        cache.TransformFeedbackKnowledge = selected;
        return selected;
    }
    /// <summary>Changes selection only when the known executed selection differs.</summary>
    internal static void Bind(OpenGlStateCache cache, int name)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        if (cache.TransformFeedbackKnowledge == name) return;
        cache.TransformFeedbackKnowledge = null;
        OpenGLDriverCalls.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, name);
        backend.RequireContext();
        cache.TransformFeedbackKnowledge = name;
    }
    #endregion
}
