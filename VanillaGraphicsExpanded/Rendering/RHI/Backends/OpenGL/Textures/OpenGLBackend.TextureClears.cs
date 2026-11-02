using System;
using System.Runtime.InteropServices;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Clears selected image content without changing canonical allocation facts.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public unsafe void ClearTexture(GpuTextureRef destination, in RHITextureRegion region, RHIPixelEncoding encoding, ReadOnlySpan<byte> value)
    {
        RequireContext();
        var owner = SelectTexture(destination);
        var metadata = owner.PublishedMetadata;
        owner.RequireOriginalContext();
        ValidateTextureRegion(metadata, region);
        if (value.Length != encoding.BytesPerPixel) throw new ArgumentException("Clear requires one encoded pixel value.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, owner.ResourceId);
        bool array = metadata.Target == TextureDimension.OneArray;
        var channels = OpenGLPixelValues.Channels(encoding.Channels);
        scoped ReadOnlySpan<byte> clearValue = value;
        Span<float> rgba = stackalloc float[4];
        if (encoding.Channels == PixelChannelLayout.Rg && encoding.Type == PixelComponentType.Float)
        {
            // NVIDIA's threaded RG-float clear path was observed consuming caller memory
            // after return. RGBA input avoids that path without changing texture storage
            // or synchronizing the GPU; missing color components retain GL's defaults.
            rgba[0] = MemoryMarshal.Read<float>(value);
            rgba[1] = MemoryMarshal.Read<float>(value[sizeof(float)..]);
            rgba[2] = 0f;
            rgba[3] = 1f;
            clearValue = MemoryMarshal.AsBytes(rgba);
            channels = OpenTK.Graphics.OpenGL4.PixelFormat.Rgba;
        }
        fixed (byte* bytes = clearValue)
            GL.ClearTexSubImage((int)owner.ResourceId, region.Subresource.MipLevel, region.X, array ? region.Z + region.Subresource.Layer : region.Y,
                array ? 0 : region.Z + region.Subresource.Layer, region.Width, array ? region.Depth : region.Height, array ? 1 : region.Depth,
                channels, OpenGLPixelValues.Type(encoding.Type), (nint)bytes);
        RequireContext();
    }
    #endregion
}
