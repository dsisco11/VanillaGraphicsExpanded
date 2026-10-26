using System;
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
        var metadata = destination.RequireMetadata();
        var owner = destination.Resource!;
        owner.RequireOriginalContext();
        ValidateTextureRegion(metadata, region);
        if (value.Length != OpenGLPixelValues.PixelBytes(encoding)) throw new ArgumentException("Clear requires one encoded pixel value.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, owner.ResourceId);
        bool array = metadata.Target == OpenTK.Graphics.OpenGL.TextureTarget.Texture1DArray;
        fixed (byte* bytes = value)
            GL.ClearTexSubImage((int)owner.ResourceId, region.Subresource.MipLevel, region.X, array ? region.Z + region.Subresource.Layer : region.Y,
                array ? 0 : region.Z + region.Subresource.Layer, region.Width, array ? region.Depth : region.Height, array ? 1 : region.Depth,
                OpenGLPixelValues.Channels(encoding.Channels), OpenGLPixelValues.Type(encoding.Type), (nint)bytes);
        RequireContext();
    }
    #endregion
}
