using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes capture selection using the singular existing selector cache.</summary>
internal static class OpenGLFeedbackBindings
{
    #region Internal API
    /// <summary>Recovers unknown selection once without inventing a second cache.</summary>
    internal static int Get(StateCache cache)
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
    internal static void Bind(StateCache cache, int name)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        if (cache.TransformFeedbackKnowledge == name) return;
        cache.TransformFeedbackKnowledge = null;
        GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, name);
        backend.RequireContext();
        cache.TransformFeedbackKnowledge = name;
    }
    #endregion
}
