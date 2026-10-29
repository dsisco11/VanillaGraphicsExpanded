using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Restricts the EXT feedback-only binding alias to its native target contract.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Reconciles the established feedback alias without accepting core-only target namespaces.</summary>
    [InterceptOpenGL("glBindBufferRangeEXT")]
    internal static void ObserveBindBufferRangeEXT(int target, uint index, uint name, nint offset, nint size)
    {
        if ((BufferRangeTarget)target == BufferRangeTarget.TransformFeedbackBuffer)
            ObserveBindBufferRange(target, index, name, offset, size);
    }
    #endregion
}
