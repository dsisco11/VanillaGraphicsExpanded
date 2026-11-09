using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Native;

/// <summary>Reads retained fixture images through explicit staging and completion without acquiring their ownership.</summary>
internal static class TextureReferenceReadback
{
    #region Internal API
    /// <summary>Reads one selected texel from an owned fixture image without transferring the rest of its allocation.</summary>
    internal static float[] ReadTexel(this GpuTexture texture, int x = 0, int y = 0) => ReadTexel(new GpuTextureRef(texture), x, y);

    /// <summary>Reads only the requested texel through the semantic readback API.</summary>
    internal static float[] ReadTexel(this GpuTextureRef texture, int x = 0, int y = 0)
    {
        var image = texture.RequireImage(0);
        using var request = RHICommandContext.Current.EnqueueReadback(new(texture,
            new(new(0), x, y, 0, 1, 1, 1),
            new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(image.Format), PixelComponentType.Float)));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Fixture texel readback failed.", request.Error);
        using var mapping = request.MapReadyReadback();
        return MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }

    /// <summary>Reads a single-pixel published image; complete image domains require GPU assertions.</summary>
    internal static float[] ReadPixels(this GpuTextureRef texture)
    {
        var image = texture.RequireImage(0);
        if(image.Width!=1 || image.Height!=1)throw new ArgumentException("Whole-image CPU assertions require a single-pixel subject.",nameof(texture));
        using var request = RHICommandContext.Current.EnqueueReadback(new(texture,
            new(new(0), 0, 0, 0, image.Width, image.Height, 1),
            new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(image.Format), PixelComponentType.Float)));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Fixture image readback failed.", request.Error);
        using var mapping = request.MapReadyReadback();
        return MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }
    #endregion
}
