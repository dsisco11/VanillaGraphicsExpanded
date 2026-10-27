using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates canonical attachment selections and pass intentions before native target setup.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Resolves dimensions, aspects and shape before allocating a framebuffer or issuing any pass side effects.</summary>
    private OpenGLPassTargetKey ResolvePassTargetKey(RHIRenderPassDescription description)
    {
        if (description.Colors.Length > NativeCapabilities.MaxDrawBuffers)
            throw new ArgumentException("The pass exceeds the cached draw-output limit.");
        var colors = ImmutableArray.CreateBuilder<(int, OpenGLAttachmentIdentity)>();
        var outputs = ImmutableArray.CreateBuilder<int>(description.Colors.Length);
        GpuImageMetadata? first = null;
        foreach (var color in description.Colors)
        {
            int slot = color.AttachmentIndex ?? outputs.Count;
            outputs.Add(color.Image.HasValue ? slot : -1);
            if (color.Image is not { } supplied) continue;
            if (slot >= NativeCapabilities.MaxColorAttachments) throw new ArgumentOutOfRangeException(nameof(description));
            var image = OpenGLAttachmentIdentity.Resolve(supplied);
            var format = image.Image.Format;
            if (!VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions.TargetFormatPolicy.IsColor(format))
                throw new ArgumentException("A color route requires a supported color image format.");
            color.Clear?.Validate(format);
            ValidatePassImageExtent(ref first, image.Image);
            colors.Add((slot, image));
        }
        OpenGLAttachmentIdentity? depth = description.DepthStencil is { } selected
            ? OpenGLAttachmentIdentity.Resolve(selected) : null;
        if (depth is { } depthImage)
        {
            if (!TextureFormatHelper.IsDepthFormat(depthImage.Image.Format)
                && !TextureFormatHelper.IsStencilFormat(depthImage.Image.Format))
                throw new ArgumentException("The depth/stencil route requires a matching image aspect.");
            ValidatePassImageExtent(ref first, depthImage.Image);
        }
        if (!first.HasValue) throw new ArgumentException("An attachment-based pass requires allocated attachment storage.");
        return new(colors.ToImmutable(), depth, outputs.MoveToImmutable());
    }
    /// <summary>Checks load/store aspects and pixel bounds before allocating or selecting a target.</summary>
    private static void ValidatePassIntentions(RHIRenderPassDescription description, OpenGLPassTargetKey key)
    {
        var first = key.DepthStencil?.Image ?? key.Colors[0].Image.Image;
        bool hasDepth = key.DepthStencil is { } depth && TextureFormatHelper.IsDepthFormat(depth.Image.Format);
        bool hasStencil = key.DepthStencil is { } stencil && (TextureFormatHelper.IsStencilFormat(stencil.Image.Format)
            || stencil.Image.Format is TextureFormat.Depth24Stencil8 or TextureFormat.Depth32fStencil8);
        if ((!hasDepth && (description.Actions.DepthLoad != AttachmentLoad.Preserve || description.Actions.DepthStore != AttachmentStore.Preserve))
            || (!hasStencil && (description.Actions.StencilLoad != AttachmentLoad.Preserve || description.Actions.StencilStore != AttachmentStore.Preserve)))
            throw new ArgumentException("The pass intentions require a missing depth/stencil aspect.");
        if (description.Area is { } area && ((long)area.X + area.Width > first.Width || (long)area.Y + area.Height > first.Height))
            throw new ArgumentOutOfRangeException(nameof(description), "The render area exceeds its published images.");
        int imageIndex = 0;
        foreach (var color in description.Colors)
            if (color.Image.HasValue)
            {
                color.Clear?.Validate(key.Colors[imageIndex].Image.Image.Format);
                imageIndex++;
            }
    }
    /// <summary>Preserves the strict equal-size and sample contract before target configuration.</summary>
    private static void ValidatePassImageExtent(ref GpuImageMetadata? first, in GpuImageMetadata image)
    {
        if (image.Width <= 0 || image.Height <= 0) throw new InvalidOperationException("Pass storage has no allocated extent.");
        if (first is { } original && (original.Width != image.Width || original.Height != image.Height
            || Math.Max(1, original.Samples) != Math.Max(1, image.Samples)))
            throw new ArgumentException("Pass images require equal dimensions and sample counts.");
        first ??= image;
    }
    #endregion
}
