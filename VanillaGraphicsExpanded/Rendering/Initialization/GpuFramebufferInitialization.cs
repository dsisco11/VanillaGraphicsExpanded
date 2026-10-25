using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes optional initial image selections and attachmentless framebuffer defaults.</summary>
internal struct GpuFramebufferInitialization
{
    private byte assigned;
    private ImmutableDictionary<FramebufferAttachment, GpuFramebufferAttachment>? attachments;
    private int width, height, layers, samples;
    private bool fixedSamples;

    #region Public API
    /// <summary>Supplies immutable slot selections without transferring image ownership.</summary>
    public ImmutableDictionary<FramebufferAttachment, GpuFramebufferAttachment> Attachments
    { readonly get => attachments ?? ImmutableDictionary<FramebufferAttachment, GpuFramebufferAttachment>.Empty; set { attachments = value; assigned |= 1; } }
    /// <summary>Specifies the attachmentless width, otherwise zero.</summary>
    public int DefaultWidth { readonly get => width; set { width = value; assigned |= 2; } }
    /// <summary>Specifies the attachmentless height, otherwise zero.</summary>
    public int DefaultHeight { readonly get => height; set { height = value; assigned |= 4; } }
    /// <summary>Specifies the attachmentless layer count, otherwise zero.</summary>
    public int DefaultLayers { readonly get => layers; set { layers = value; assigned |= 8; } }
    /// <summary>Specifies the attachmentless sample count, otherwise zero.</summary>
    public int DefaultSamples { readonly get => samples; set { samples = value; assigned |= 16; } }
    /// <summary>Specifies attachmentless fixed sample locations, otherwise false.</summary>
    public bool DefaultFixedSampleLocations { readonly get => fixedSamples; set { fixedSamples = value; assigned |= 32; } }
    #endregion

    #region Internal API
    /// <summary>Reports explicit attachmentless intent, including zero and false.</summary>
    internal readonly bool HasDefaults => (assigned & 62) != 0;

    /// <summary>Validates all selections and capability-dependent defaults before allocation.</summary>
    internal readonly void Validate()
    {
        var caps = GpuSupport.Graphics;
        if (HasDefaults && !caps.SupportsFramebufferNoAttachments)
            throw new NotSupportedException("Attachmentless framebuffer defaults are unavailable.");
        if (width < 0 || height < 0 || layers < 0 || samples < 0
            || width > caps.MaxFramebufferWidth || height > caps.MaxFramebufferHeight
            || layers > caps.MaxFramebufferLayers || samples > caps.MaxFramebufferSamples)
            throw new ArgumentOutOfRangeException(nameof(DefaultWidth), "Framebuffer defaults exceed cached limits.");
        if ((assigned & 1) != 0 && attachments is null) throw new ArgumentNullException(nameof(Attachments));
        if (Attachments.ContainsKey(FramebufferAttachment.DepthStencilAttachment)
            && (Attachments.ContainsKey(FramebufferAttachment.DepthAttachment) || Attachments.ContainsKey(FramebufferAttachment.StencilAttachment)))
            throw new ArgumentException("Packed depth/stencil selections overlap explicit aspect slots.");
        foreach (var pair in Attachments)
        {
            ArgumentNullException.ThrowIfNull(pair.Value);
            pair.Value.ValidateSlot(pair.Key);
            if (pair.Value.ImageTarget == TextureTarget.Texture2DArray
                && pair.Value.AttachmentMode == GpuFramebufferAttachmentMode.Unknown && caps.SupportsOvrMultiview)
                throw new NotSupportedException("Native attachment queries do not establish ordinary versus multiview replay semantics.");
        }
    }
    #endregion
}
