using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Routes builtin framebuffer output changes through the shared framebuffer-local state owner.</summary>
internal static partial class EngineStateCalls
{
    #region Public API
    /// <summary>Preserves the exact native DrawBuffers overload and reconciles affected knowledge.</summary>
    public static void DrawBuffers(int count, DrawBuffersEnum[] buffers) => OpenGLFramebufferRoutingCalls.ExecuteCoreDrawBuffers(count, buffers);
    /// <summary>Preserves the exact native DrawBuffer overload and reconciles affected knowledge.</summary>
    public static void DrawBuffer(DrawBufferMode buffer) => OpenGLFramebufferRoutingCalls.ExecuteCoreDrawBuffer(buffer);
    #endregion
}
