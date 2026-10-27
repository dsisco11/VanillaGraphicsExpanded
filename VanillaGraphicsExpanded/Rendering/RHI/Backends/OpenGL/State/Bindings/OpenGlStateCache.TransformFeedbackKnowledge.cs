namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains executed capture-object selector knowledge independently of native execution.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Gets or withdraws the existing capture selector fact.</summary>
    internal int? TransformFeedbackKnowledge { get => currentTransformFeedback; set => currentTransformFeedback = value; }
    #endregion
}
