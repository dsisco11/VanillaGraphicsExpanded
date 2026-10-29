using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes framebuffer factory outputs without replacing original native identities.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Records GenFramebuffers results without replacing live original identities.</summary>
    [InterceptOpenGL("glGenFramebuffers")]
    [InterceptOpenGL("glGenFramebuffersEXT")]
    internal static void ObserveGenFramebuffers(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
            manager.ObserveImageCreation(GpuResourceKind.Framebuffer, unchecked((int)names[i]), false);
    }

    /// <summary>Records CreateFramebuffers results without replacing live original identities.</summary>
    [InterceptOpenGL("glCreateFramebuffers")]
    internal static void ObserveCreateFramebuffers(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
        {
            int id = unchecked((int)names[i]);
            bool fresh = id != 0 && manager.FindResource(GpuResourceKind.Framebuffer, id) is null;
            manager.ObserveImageCreation(GpuResourceKind.Framebuffer, id, true);
            if (fresh) Current.RememberFramebufferDefaults(id);
        }
    }
    #endregion
}
