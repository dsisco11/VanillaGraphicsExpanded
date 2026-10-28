namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains executed capture-object selector knowledge independently of native execution.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Gets or withdraws the existing capture selector fact.</summary>
    internal int? TransformFeedbackKnowledge
    {
        get => currentTransformFeedback;
        set
        {
            if (currentTransformFeedback != value)
            {
                // Indexed feedback ranges belong to the selected object. The generic
                // TRANSFORM_FEEDBACK_BUFFER binding is independent and remains valid.
                // Dictionary removal preserves enumeration on the supported runtime.
                foreach (var key in indexedBufferBindings.Keys)
                    if (key.Target == OpenTK.Graphics.OpenGL.BufferRangeTarget.TransformFeedbackBuffer)
                        indexedBufferBindings.Remove(key);
            }
            currentTransformFeedback = value;
        }
    }
    #endregion
}
