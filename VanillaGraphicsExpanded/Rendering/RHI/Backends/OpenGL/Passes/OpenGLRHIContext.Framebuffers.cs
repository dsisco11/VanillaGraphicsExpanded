using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Provides explicit engine-owned framebuffer execution without exposing a backend instance.</summary>
internal static partial class OpenGLRHIContext
{
    #region Internal API
    /// <summary>Checks an externally acquired target without changing read or draw selectors.</summary>
    internal static bool IsExternalFramebufferComplete(int framebuffer)
    { _ = Current; return backend!.IsExternalFramebufferComplete(framebuffer); }
    /// <summary>Configures a supplied engine image set in one original-identity transaction.</summary>
    internal static bool ConfigureEngineFramebuffer(int framebuffer, IReadOnlyDictionary<FramebufferAttachment, GpuFramebufferAttachment?> attachments)
    { _ = Current; return backend!.ConfigureEngineFramebuffer(framebuffer, attachments); }
    /// <summary>Selects the engine output route and its explicit blend policy.</summary>
    internal static void RouteEngineFramebuffer(DrawBuffersEnum[] route, GlPipelineDesc? blend = null, bool forceDirty = false)
    { _ = Current; backend!.RouteEngineFramebuffer(route, blend, forceDirty); }
    /// <summary>Establishes explicit engine blend policy independently of target selection.</summary>
    internal static void ApplyEngineFramebufferBlend(GlPipelineDesc blend, bool forceDirty)
    { _ = Current; backend!.ApplyEngineFramebufferBlend(blend, forceDirty); }
    /// <summary>Applies engine blend policy only when the expected engine target is selected.</summary>
    internal static void ReapplyEngineFramebufferBlend(int framebuffer, GlPipelineDesc blend)
    { _ = Current; backend!.ReapplyEngineFramebufferBlend(framebuffer, blend); }
    /// <summary>Detaches engine integration images and leaves the default framebuffer selected as required by teardown.</summary>
    internal static void DetachEngineFramebuffer(int framebuffer, FramebufferAttachment[] attachments, DrawBuffersEnum[] route)
    { _ = Current; backend!.DetachEngineFramebuffer(framebuffer, attachments, route); }
    #endregion
}
