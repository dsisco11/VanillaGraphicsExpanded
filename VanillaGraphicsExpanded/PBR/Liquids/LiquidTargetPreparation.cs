using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Certifies the published target facts used by a liquid pipeline description.</summary>
internal sealed class LiquidTargetPreparation
{
    private readonly RHIRenderPassDescription pass;
    private readonly ulong surfaceRevision;
    private readonly (GpuTextureMetadata Texture, GpuRenderbufferMetadata Renderbuffer)[] images;

    #region Public API
    /// <summary>Captures facts only after the RHI has successfully described the pass.</summary>
    internal LiquidTargetPreparation(RHIRenderPassDescription pass)
    {
        this.pass = pass;
        if (!pass.Surface.IsEmpty)
        {
            surfaceRevision = pass.Surface.RequireSurface().AttachmentRevision;
            images = [];
            return;
        }
        images = new (GpuTextureMetadata, GpuRenderbufferMetadata)[pass.Colors.Length + 1];
        for (int i = 0; i < pass.Colors.Length; i++) images[i] = Read(pass.Colors[i].Image);
        images[^1] = Read(pass.DepthStencil);
    }

    /// <summary>Checks current availability and authored routing without describing or realizing native targets again.</summary>
    internal bool Matches(RHIRenderPassDescription candidate)
    {
        if (candidate.Surface != pass.Surface || candidate.DepthStencil != pass.DepthStencil
            || candidate.Actions != pass.Actions || candidate.Colors.Length != pass.Colors.Length) return false;
        for (int i = 0; i < pass.Colors.Length; i++)
            if (candidate.Colors[i] != pass.Colors[i]) return false;
        if (!candidate.Surface.IsEmpty)
        {
            var surface = candidate.Surface.RequireSurface();
            return surface.IsValid && surface.HasRenderPassMetadata && surface.AttachmentRevision == surfaceRevision;
        }
        for (int i = 0; i < pass.Colors.Length; i++)
            if (Read(candidate.Colors[i].Image) != images[i]) return false;
        return Read(candidate.DepthStencil) == images[^1];
        // Area and label do not affect pipeline compatibility. BeginRenderPass still validates and
        // executes every current intention, original resource identity and selected render area.
    }
    #endregion

    #region Private
    /// <summary>Reads supplied snapshots only; unavailable or retired attachments cannot reuse preparation.</summary>
    private static (GpuTextureMetadata, GpuRenderbufferMetadata) Read(RHIAttachment? attachment)
    {
        if (attachment is not { } image) return default;
        if (!image.Texture.IsEmpty && image.Texture.TryGetMetadata(out var texture)) return (texture, default);
        if (!image.Renderbuffer.IsEmpty && image.Renderbuffer.TryGetMetadata(out var renderbuffer)) return (default, renderbuffer);
        throw new InvalidOperationException("Liquid target metadata is unavailable.");
    }
    #endregion
}
