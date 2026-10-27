using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns mutable attachment configuration and cached, restorable framebuffer routing.</summary>
public sealed partial class DynamicGpuFramebuffer : GpuFramebuffer
{
    #region Public API
    /// <inheritdoc />
    public override void SetAttachment(FramebufferSlot slot, GpuFramebufferAttachment attachment) => SetAttachmentCore(slot, attachment);
    /// <inheritdoc />
    public override bool RemoveAttachment(FramebufferSlot slot) => RemoveAttachmentCore(slot);
    /// <inheritdoc />
    public override bool Resize(int width, int height) => ResizeCore(width, height);
    #endregion

    #region Internal API
    /// <summary>Allocates an owned identity or wraps a borrowed engine/window framebuffer.</summary>
    internal DynamicGpuFramebuffer(bool owns, int id = 0, string? name = null, int width = 0, int height = 0)
        : base(owns, id, name, width, height) { }
    #endregion
}
