using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects one referenced renderable texture subresource or renderbuffer without transferring storage ownership.</summary>
public readonly record struct RHIAttachment
{
    #region Public API
    /// <summary>Gets optional texture storage; exactly one storage kind is present in a nonempty selection.</summary>
    public GpuTextureRef Texture { get; }
    /// <summary>Gets optional renderbuffer storage.</summary>
    public GpuRenderbufferRef Renderbuffer { get; }
    /// <summary>Gets view-relative mip and face/layer selection.</summary>
    public RHITextureSubresource Subresource { get; }
    /// <summary>Reports a whole layered image rather than the single selected face/layer.</summary>
    public bool Layered { get; }
    /// <summary>Reports absence without inspecting resource metadata or native validity.</summary>
    public bool IsEmpty => Texture.IsEmpty && Renderbuffer.IsEmpty;
    /// <summary>Freezes a texture selection without querying its dimensions, target or allocation.</summary>
    public static RHIAttachment FromTexture(GpuTextureRef texture, RHITextureSubresource subresource = default, bool layered = false)
    {
        if (texture.IsEmpty) throw new ArgumentException("A texture reference is required.", nameof(texture));
        if (subresource.MipLevel < 0 || subresource.Layer < 0 || layered && subresource.Layer != 0)
            throw new ArgumentOutOfRangeException(nameof(subresource));
        return new(texture, default, subresource, layered);
    }
    /// <summary>Freezes a renderbuffer reference without acquiring metadata or creating another native object.</summary>
    public static RHIAttachment FromRenderbuffer(GpuRenderbufferRef renderbuffer)
    {
        if (renderbuffer.IsEmpty) throw new ArgumentException("A renderbuffer reference is required.", nameof(renderbuffer));
        return new(default, renderbuffer, default, false);
    }
    #endregion

    #region Private
    /// <summary>Stores only a single explicitly selected resource kind.</summary>
    private RHIAttachment(GpuTextureRef texture, GpuRenderbufferRef renderbuffer, RHITextureSubresource subresource, bool layered)
    { Texture = texture; Renderbuffer = renderbuffer; Subresource = subresource; Layered = layered; }
    #endregion
}
