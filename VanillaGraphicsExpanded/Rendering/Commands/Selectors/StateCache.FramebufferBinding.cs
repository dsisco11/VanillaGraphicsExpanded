using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes external framebuffer selection without asserting native acceptance.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Forwards the original target and name, retaining unknown results through reentrant dispatch.</summary>
    internal void ExecuteFramebufferBinding(FramebufferTarget target, int framebuffer)
    {
        ForgetFramebufferSelection(target);
        try { GL.BindFramebuffer(target, framebuffer); }
        finally { ForgetFramebufferSelection(target); }
    }
    #endregion

    #region Private
    /// <summary>Withdraws only the affected binding directions; routing belongs to the framebuffer itself.</summary>
    private void ForgetFramebufferSelection(FramebufferTarget target)
    {
        if (target != FramebufferTarget.DrawFramebuffer) currentReadFramebuffer = null;
        if (target != FramebufferTarget.ReadFramebuffer)
        {
            currentDrawFramebuffer = null;
            currentFramebuffer = null;
        }
    }
    #endregion
}
