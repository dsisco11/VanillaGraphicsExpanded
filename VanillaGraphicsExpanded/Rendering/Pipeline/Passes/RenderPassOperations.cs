using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Executes attachment intentions through the shared cache under the caller's restoration boundary.</summary>
internal static class RenderPassOperations
{
    /// <summary>Covers every native write made by a typed attachment clear, independently of draw state.</summary>
    internal static PipelineStateCoverage ClearCoverage { get; } = new(
        depth: DepthStateKnowledge.WriteEnabled,
        rasterizer: RasterizerStateKnowledge.RasterizerDiscard | RasterizerStateKnowledge.ScissorEnabled,
        dynamic: DynamicDrawStateKnowledge.Scissor, globalBlend: BlendStateKnowledge.WriteMask,
        completeGraphics: true, stencil: StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask,
        output: OutputStateKnowledge.FramebufferSrgb | OutputStateKnowledge.Dither);

    #region Public API
    /// <summary>Establishes clear-only state independently of preceding draw write masks and scissor.</summary>
    internal static void Load(RenderPassDesc description, RenderArea area)
    {
        var cache = StateCache.Current;
        bool clears = description.DepthStencil.DepthLoad == AttachmentLoad.Clear
            || description.DepthStencil.StencilLoad == AttachmentLoad.Clear;
        foreach (var color in description.Colors) clears |= color.Load == AttachmentLoad.Clear;
        if (!clears) return;
        // Clear operations do not inherit draw output interpretation, discard, or scissor policy.
        cache.SetRasterizerDiscardEnabled(false);
        cache.SetFramebufferSrgbEnabled(false);
        cache.SetDitherEnabled(false);
        cache.SetCapability(EnableCap.ScissorTest, true);
        cache.SetScissor(area.X, area.Y, area.Width, area.Height);
        for (int i = 0; i < description.Colors.Count; i++)
        {
            var color = description.Colors[i];
            if (color.Load != AttachmentLoad.Clear) continue;
            cache.SetColorMaskIndexed(i, GlColorMask.All);
            color.Clear!.Execute(i);
        }
        var aspects = description.DepthStencil;
        if (aspects.DepthLoad == AttachmentLoad.Clear)
        {
            cache.SetDepthWriteMask(true);
            GL.ClearBuffer(ClearBuffer.Depth, 0, new[] { aspects.ClearDepth });
        }
        if (aspects.StencilLoad == AttachmentLoad.Clear)
        {
            cache.SetStencilWriteMask(StencilFace.Front, uint.MaxValue);
            cache.SetStencilWriteMask(StencilFace.Back, uint.MaxValue);
            GL.ClearBuffer(ClearBuffer.Stencil, 0, new[] { aspects.ClearStencil });
        }
        // Discard is permission to abandon contents, not a required clear or invalidation call.
        // Conservatively retain discarded regions, preserving unrelated packed aspects and pixels.
    }
    #endregion
}
