using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Routes builtin framebuffer output changes through the shared framebuffer-local state owner.</summary>
internal static partial class EngineStateCalls
{
    #region Public API
    /// <summary>Publishes the builtin caller's complete output mapping through its exact native signature.</summary>
    public static void DrawBuffers(int count, DrawBuffersEnum[] buffers) => OpenGLFramebufferRoutingCalls.ExecuteCoreDrawBuffers(count, buffers);
    /// <summary>Publishes a single builtin output selector, including grouped default-window selectors.</summary>
    public static void DrawBuffer(DrawBufferMode buffer) => OpenGLFramebufferRoutingCalls.ExecuteCoreDrawBuffer(buffer);
    #endregion
}
