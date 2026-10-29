using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes renderbuffer factory outputs without replacing original native identities.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Records GenRenderbuffers results without replacing live original identities.</summary>
    [InterceptOpenGL("glGenRenderbuffers")]
    [InterceptOpenGL("glGenRenderbuffersEXT")]
    internal static void ObserveGenRenderbuffers(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
            manager.ObserveImageCreation(GpuResourceKind.Renderbuffer, unchecked((int)names[i]), false);
    }

    /// <summary>Records CreateRenderbuffers results without replacing live original identities.</summary>
    [InterceptOpenGL("glCreateRenderbuffers")]
    internal static void ObserveCreateRenderbuffers(int count, uint* names)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
            manager.ObserveImageCreation(GpuResourceKind.Renderbuffer, unchecked((int)names[i]), true);
    }
    #endregion
}
