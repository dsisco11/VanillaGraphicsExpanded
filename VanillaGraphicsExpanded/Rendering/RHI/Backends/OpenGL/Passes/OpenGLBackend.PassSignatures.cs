using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Projects published attachment compatibility without native target realization or pass guards.</summary>
internal sealed partial class OpenGLBackend
{

    #region Public API
    /// <inheritdoc />
    public Pipeline.Descriptions.RenderTargetSignature DescribeRenderTargets(RHIRenderPassDescription description)
    {
        RequireContext();
        return PreparePass(description, realize: false).Signature!;
    }
    #endregion

    #region Private
    /// <summary>Derives semantic output compatibility from selected facts without allocating native targets.</summary>
    private static Pipeline.Descriptions.RenderTargetSignature PreparePassSignature(RHIRenderPassDescription description, OpenGLPassTargetKey key)
    {
        var colors = new Pipeline.Descriptions.ColorTargetSlot[description.Colors.Length];
        int imageIndex = 0;
        for (int output = 0; output < colors.Length; output++)
        {
            var source = description.Colors[output];
            TextureFormat? format = source.Image.HasValue ? key.Colors[imageIndex++].Image.Image.Format : null;
            colors[output] = new(format, source.DiscardOutput);
        }
        var first = key.DepthStencil?.Image ?? key.Colors[0].Image.Image;
        var depth = key.DepthStencil?.Image;
        return new(colors, depth?.Format, Math.Max(1, first.Samples),
            depth is { } d && TextureFormatHelper.IsDepthFormat(d.Format),
            depth is { } s && (TextureFormatHelper.IsStencilFormat(s.Format)
                || s.Format is TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8));
    }
    #endregion
}
