using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Reads retained fixture images through explicit staging and completion without acquiring their ownership.</summary>
internal static class TextureReferenceReadback
{
    #region Internal API
    /// <summary>Reads one published base image as floats through the same semantic readback path used by rendering resources.</summary>
    internal static float[] ReadPixels(this GpuTextureRef texture)
    {
        var image = texture.RequireImage(0);
        using var request = RHICommandContext.Current.EnqueueReadback(new(texture,
            new(new(0), 0, 0, 0, image.Width, image.Height, 1),
            OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(image.Format), PixelType.Float)));
        if (request.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Fixture image readback failed.", request.Error);
        using var mapping = request.MapReadyReadback();
        return MemoryMarshal.Cast<byte, float>(mapping.Bytes).ToArray();
    }
    #endregion
}
