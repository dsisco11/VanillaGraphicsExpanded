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
        if (!description.Surface.IsEmpty)
        {
            using var facts = new RenderPassTargets(LowerPass(description, description.Surface.RequireSurface()), retain: false);
            return facts.Signature;
        }
        var key = ResolvePassTargetKey(description);
        ValidatePassIntentions(description, key);
        var colors = new Pipeline.Descriptions.ColorTargetSlot[description.Colors.Length];
        for (int output = 0; output < colors.Length; output++)
        {
            var source = description.Colors[output];
            TextureFormat? format = source.Image is { } image
                ? OpenGLImageValues.Format(OpenGLAttachmentIdentity.Resolve(image).Image.Format) : null;
            colors[output] = new(format, source.DiscardOutput);
        }
        var first = key.DepthStencil?.Image ?? key.Colors[0].Image.Image;
        var depth = key.DepthStencil?.Image;
        var signature = new Pipeline.Descriptions.RenderTargetSignature(colors,
            depth is { } imageDepth ? OpenGLImageValues.Format(imageDepth.Format) : null,
            Math.Max(1, first.Samples), depth is { } d && TextureFormatHelper.IsDepthFormat(d.Format),
            depth is { } s && (TextureFormatHelper.IsStencilFormat(s.Format)
                || s.Format is OpenTK.Graphics.OpenGL.PixelInternalFormat.Depth24Stencil8 or OpenTK.Graphics.OpenGL.PixelInternalFormat.Depth32fStencil8));
        return signature;
    }
    #endregion
}
