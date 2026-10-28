using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates defined external dynamics effects into the existing executed-state authority.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Commits the defined Viewport effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalViewport(int x, int y, int width, int height)
    {
        dynamicState.X = x; dynamicState.Y = y; dynamicState.Width = Math.Min(width, GpuSupport.MaxViewportWidth); dynamicState.Height = Math.Min(height, GpuSupport.MaxViewportHeight); dynamicKnown |= DynamicDrawStateKnowledge.Viewport;
    }
    /// <summary>Commits the defined Scissor effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalScissor(int x, int y, int width, int height)
    {
        dynamicState.Scissor = (x, y, width, height); dynamicKnown |= DynamicDrawStateKnowledge.Scissor;
    }
    #endregion
}
