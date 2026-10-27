using System;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Coordinates target realization and specialized pass lifetime under admitted restoration authority.</summary>
internal sealed partial class OpenGLBackend
{
    private RenderPassSequence? renderPassSequence;

    #region Public API
    /// <inheritdoc />
    public bool HasRenderPass => activeRenderPass is not null;
    /// <inheritdoc />
    public RHIViewport PassViewport => (activeRenderPass ?? throw new InvalidOperationException("No render pass is active.")).Viewport;
    /// <inheritdoc />
    public void BeginRenderPass(RHIRenderPassDescription description, EngineBoundaryScope boundary)
    {
        RequireContext();
        cache.RequireEngineBoundary(boundary);
        if (activeRenderPass is not null) throw new InvalidOperationException("Render passes cannot nest.");
        var target = ResolvePassTarget(description);
        activeRenderPass = RenderPass.Begin(LowerPass(description, target), boundary, renderPassSequence??throw new InvalidOperationException("Pass effects were not admitted."));
        RequireContext();
    }
    /// <inheritdoc />
    public void EndRenderPass()
    {
        RequireContext();
        var pass = activeRenderPass ?? throw new InvalidOperationException("No render pass is active.");
        try { pass.Dispose(); }
        finally { activeRenderPass = null; }
    }
    /// <inheritdoc />
    public void ReleaseBoundaryPass()
    {
        activeRenderPass = null;
        renderPassSequence = null;
    }
    #endregion

    #region Private
    /// <summary>Translates semantic routing to the existing private pass validator after resource resolution.</summary>
    private static RenderPassDesc LowerPass(RHIRenderPassDescription description, GpuFramebuffer target)
    {
        var colors = new RenderPassColor[description.Colors.Length];
        for (int output = 0; output < colors.Length; output++)
        {
            var source = description.Colors[output];
            OpenTK.Graphics.OpenGL.DrawBuffersEnum? window = source.WindowBuffer switch
            {
                SurfaceColorBuffer.FrontLeft => OpenTK.Graphics.OpenGL.DrawBuffersEnum.FrontLeft,
                SurfaceColorBuffer.BackLeft => OpenTK.Graphics.OpenGL.DrawBuffersEnum.BackLeft,
                _ => null
            };
            int slot = window.HasValue ? -1 : source.Image.HasValue || source.AttachmentIndex.HasValue
                ? source.AttachmentIndex ?? output : -1;
            colors[output] = new(slot, source.Load, source.Store, source.Clear, source.DiscardOutput, window);
        }
        return new RenderPassDesc(target, colors, description.Actions, description.Area);
    }
    #endregion
}
