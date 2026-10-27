using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes validated client uploads without changing image metadata or content certificates.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public unsafe void UploadTexture(GpuTextureRef destination, in RHITextureRegion region, RHIPixelEncoding encoding,
        ReadOnlySpan<byte> bytes, int rowPitch, int slicePitch)
    {
        RequireContext();
        var data = destination.RequireMetadata();
        var owner = destination.Resource!;
        owner.RequireOriginalContext();
        ValidateTextureRegion(data, region);
        var layout = PixelLayoutForTarget(ValidatePixelLayout(region, encoding, bytes.Length, rowPitch, slicePitch), data.Target);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, owner.ResourceId);
        using var unpack = cache.BindBufferScope(BufferTarget.PixelUnpackBuffer, 0);
        using var pixels = OpenGLPixelLayouts.UnpackScope(cache, layout);
        fixed (byte* source = bytes)
            UploadImagePixels((int)owner.ResourceId, data.Target, region, encoding, (nint)source);
        RequireContext();
    }
    #endregion

    #region Private
    /// <summary>Maps logical array slices to native rows for the one-dimensional array target.</summary>
    private static StateCache.PixelUnpackState PixelLayoutForTarget(StateCache.PixelUnpackState layout, TextureTarget target)
        => target == TextureTarget.Texture1DArray ? layout with { RowLength = checked(layout.RowLength * layout.ImageHeight), ImageHeight = 0 } : layout;

    /// <summary>Checks all initial subresources and byte ranges before creating any native allocation.</summary>
    private static void ValidateTextureInitialData(in GpuTextureInitialization request, ReadOnlySpan<RHITextureInitialData> initialData)
    {
        if (request.Target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray && !initialData.IsEmpty)
            throw new ArgumentException("Multisample storage cannot be initialized from client pixels.");
        foreach (var item in initialData)
        {
            var region = item.Region;
            if (region.Subresource.MipLevel < 0 || region.Subresource.MipLevel >= request.Levels) throw new ArgumentOutOfRangeException(nameof(initialData));
            int level = region.Subresource.MipLevel;
            int width = Math.Max(1, request.Width >> level);
            int height = request.Target == TextureTarget.Texture1DArray ? 1 : Math.Max(1, request.Height >> level);
            int depth = request.Target == TextureTarget.Texture3D ? Math.Max(1, request.Depth >> level)
                : request.Target == TextureTarget.TextureCubeMap ? 6 : request.Target == TextureTarget.Texture1DArray ? request.Height : request.Depth;
            ValidateRegionBounds(region, width, height, depth);
            _ = ValidatePixelLayout(region, item.Encoding, item.Bytes.Length, item.RowPitch, item.SlicePitch);
        }
    }

    /// <summary>Checks selected view-relative mip bounds without native discovery.</summary>
    private static void ValidateTextureRegion(GpuTextureMetadata metadata, in RHITextureRegion region, bool allowMultisample = false)
    {
        if (metadata.Target == TextureTarget.TextureBuffer || !allowMultisample && metadata.Target is TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray)
            throw new ArgumentException("This operation requires a non-multisample image.");
        foreach (var image in metadata.Levels)
            if (image.Level == region.Subresource.MipLevel && image.Allocated)
            {
                int height = metadata.Target == TextureTarget.Texture1DArray ? 1 : image.Height;
                int depth = metadata.Target == TextureTarget.TextureCubeMap ? 6
                    : metadata.Target == TextureTarget.Texture1DArray ? image.Height : image.Depth;
                ValidateRegionBounds(region, image.Width, height, depth);
                if (metadata.Target == TextureTarget.TextureCubeMap)
                {
                    // External mutable cubes may have independently defined or absent faces.
                    for (int face = region.Subresource.Layer + region.Z; face < region.Subresource.Layer + region.Z + region.Depth; face++)
                    {
                        bool found = false;
                        foreach (var selected in metadata.Levels)
                            if (selected.Level == region.Subresource.MipLevel && selected.Face == TextureTarget.TextureCubeMapPositiveX + face && selected.Allocated)
                            {
                                ValidateRegionBounds(region, selected.Width, selected.Height, 6);
                                found = true;
                                break;
                            }
                        if (!found) throw new ArgumentException("A selected cube face is not allocated.");
                    }
                }
                return;
            }
        throw new ArgumentException("The selected mip is not allocated.");
    }

    /// <summary>Uses subtraction-based range checks to avoid overflowing supplied origins and extents.</summary>
    private static void ValidateRegionBounds(in RHITextureRegion region, int width, int height, int depth)
    {
        if (region.Subresource.MipLevel < 0 || region.Subresource.Layer < 0 || region.X < 0 || region.Y < 0 || region.Z < 0
            || region.Width <= 0 || region.Height <= 0 || region.Depth <= 0 || region.X > width || region.Width > width - region.X
            || region.Y > height || region.Height > height - region.Y || region.Subresource.Layer > depth
            || region.Z > depth - region.Subresource.Layer || region.Depth > depth - region.Subresource.Layer - region.Z)
            throw new ArgumentOutOfRangeException(nameof(region), "Region exceeds the selected image.");
    }

    /// <summary>Builds an exact unpack layout and rejects bytes insufficient for its last accessed texel.</summary>
    private static StateCache.PixelUnpackState ValidatePixelLayout(in RHITextureRegion region, RHIPixelEncoding encoding, nint byteCount, int rowPitch, int slicePitch)
    {
        int pixelBytes = OpenGLPixelValues.PixelBytes(encoding);
        int tightRow = checked(region.Width * pixelBytes);
        int row = rowPitch == 0 ? tightRow : rowPitch;
        int slice = slicePitch == 0 ? checked(row * region.Height) : slicePitch;
        if (row < tightRow || slice < checked(row * region.Height) || slice % row != 0)
            throw new ArgumentException("Pitches do not represent a complete pixel/row layout.");
        long needed = checked((long)(region.Depth - 1) * slice + (long)(region.Height - 1) * row + tightRow);
        if (byteCount < needed) throw new ArgumentException("Pixel payload is shorter than its region and pitches.");
        // Pixel rows can contain alignment padding that is not a whole texel (for example RGB8).
        int rowLength = row / pixelBytes;
        int alignment = 1;
        while (alignment <= 8 && ((long)rowLength * pixelBytes + alignment - 1) / alignment * alignment != row)
            alignment *= 2;
        if (alignment > 8) throw new ArgumentException("Row pitch cannot be represented by an OpenGL unpack layout.");
        return new(alignment, rowLength, ImageHeight: slice / row);
    }

    /// <summary>Dispatches one named upload with cube faces and array layers expressed in native coordinates.</summary>
    private static void UploadImagePixels(int id, TextureTarget target, in RHITextureRegion region, RHIPixelEncoding encoding, nint pixels)
    {
        var format = OpenGLPixelValues.Channels(encoding.Channels);
        var type = OpenGLPixelValues.Type(encoding.Type);
        int z = checked(region.Subresource.Layer + region.Z);
        if (target == TextureTarget.Texture1D)
            GL.TextureSubImage1D(id, region.Subresource.MipLevel, region.X, region.Width, format, type, pixels);
        else if (target == TextureTarget.Texture1DArray)
            GL.TextureSubImage2D(id, region.Subresource.MipLevel, region.X, z, region.Width, region.Depth, format, type, pixels);
        else if (target is TextureTarget.Texture2D or TextureTarget.TextureRectangle)
            GL.TextureSubImage2D(id, region.Subresource.MipLevel, region.X, region.Y, region.Width, region.Height, format, type, pixels);
        else GL.TextureSubImage3D(id, region.Subresource.MipLevel, region.X, region.Y, z, region.Width, region.Height, region.Depth, format, type, pixels);
    }
    #endregion
}
