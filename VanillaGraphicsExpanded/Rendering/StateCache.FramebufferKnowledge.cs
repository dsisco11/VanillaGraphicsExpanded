using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains framebuffer selector knowledge established by backend execution without issuing native commands.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Publishes or withdraws one selector direction; the combined query aliases the draw direction independently of the read selector.</summary>
    internal void SetFramebufferSelectionKnowledge(FramebufferTarget target, int? name)
    {
        if (target != FramebufferTarget.DrawFramebuffer) currentReadFramebuffer = name;
        if (target != FramebufferTarget.ReadFramebuffer) currentDrawFramebuffer = name;
        currentFramebuffer = currentDrawFramebuffer;
    }
    #endregion
}
