using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes explicit GPU buffer-image transfers while preserving pixel selectors and layouts.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void TransferBufferToTexture(GpuTextureRef destination, in RHITextureTransfer transfer)
    {
        var request = transfer;
        RequireContext();
        var data = destination.RequireMetadata();
        var image = destination.Resource!;
        image.RequireOriginalContext();
        var buffer = request.Buffer.RequireResource();
        var storage = GpuResourceManager.RequireBufferReferenceMetadata(buffer);
        if (request.Offset < 0 || request.Offset > storage.Capacity) throw new ArgumentOutOfRangeException(nameof(transfer));
        ValidateTextureRegion(data, request.Region);
        var layout = PixelLayoutForTarget(ValidatePixelLayout(request.Region, request.Encoding, storage.Capacity - request.Offset, request.RowPitch, request.SlicePitch), data.Target);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, image.ResourceId);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, buffer.ResourceId);
        using var unpack = cache.BindBufferScope(BufferTarget.PixelUnpackBuffer, (int)buffer.ResourceId);
        using var pixels = OpenGLPixelLayouts.UnpackScope(cache, layout);
        UploadImagePixels((int)image.ResourceId, data.Target, request.Region, request.Encoding, request.Offset);
        RequireContext();
    }

    /// <inheritdoc />
    public void TransferTextureToBuffer(GpuTextureRef source, in RHITextureTransfer transfer)
    {
        var request = transfer;
        RequireContext();
        var data = source.RequireMetadata();
        var image = source.Resource!;
        image.RequireOriginalContext();
        var buffer = request.Buffer.RequireResource();
        var storage = GpuResourceManager.RequireBufferReferenceMetadata(buffer);
        if (request.Offset < 0 || request.Offset > storage.Capacity) throw new ArgumentOutOfRangeException(nameof(transfer));
        ValidateTextureRegion(data, request.Region);
        var layout = PixelLayoutForTarget(ValidatePixelLayout(request.Region, request.Encoding, storage.Capacity - request.Offset, request.RowPitch, request.SlicePitch), data.Target);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, image.ResourceId);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, buffer.ResourceId);
        using var pack = cache.BindBufferScope(BufferTarget.PixelPackBuffer, (int)buffer.ResourceId);
        using var pixels = OpenGLPixelLayouts.PackScope(cache, new(1, layout.RowLength, ImageHeight: layout.ImageHeight));
        var region = request.Region;
        int y = data.Target == TextureDimension.OneArray ? region.Subresource.Layer + region.Z : region.Y;
        int z = data.Target == TextureDimension.OneArray ? 0 : region.Subresource.Layer + region.Z;
        int height = data.Target == TextureDimension.OneArray ? region.Depth : region.Height;
        int depth = data.Target == TextureDimension.OneArray ? 1 : region.Depth;
        GL.GetTextureSubImage((int)image.ResourceId, region.Subresource.MipLevel, region.X, y, z, region.Width, height, depth,
            OpenGLPixelValues.Channels(request.Encoding.Channels), OpenGLPixelValues.Type(request.Encoding.Type),
            checked((int)Math.Min(int.MaxValue, storage.Capacity - request.Offset)), request.Offset);
        RequireContext();
    }

    /// <inheritdoc />
    public void CopyTexture(GpuTextureRef source, in RHITextureRegion sourceRegion, GpuTextureRef destination, in RHITextureRegion destinationRegion)
    {
        RequireContext();
        var from = source.RequireMetadata();
        var to = destination.RequireMetadata();
        source.Resource!.RequireOriginalContext(); destination.Resource!.RequireOriginalContext();
        ValidateTextureRegion(from, sourceRegion, allowMultisample: true);
        ValidateTextureRegion(to, destinationRegion, allowMultisample: true);
        if (sourceRegion.Width != destinationRegion.Width || sourceRegion.Height != destinationRegion.Height || sourceRegion.Depth != destinationRegion.Depth)
            throw new ArgumentException("Copy extents must match.");
        if (!TextureViewFormatCompatibility.AreCompatible(source.RequireImage(sourceRegion.Subresource.MipLevel).Format, destination.RequireImage(destinationRegion.Subresource.MipLevel).Format))
            throw new ArgumentException("Copy image representations are incompatible.");
        if (source.RequireImage(sourceRegion.Subresource.MipLevel).Samples != destination.RequireImage(destinationRegion.Subresource.MipLevel).Samples)
            throw new ArgumentException("Image copies require matching sample counts; resolving samples is a pass operation.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, source.Resource.ResourceId);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, destination.Resource.ResourceId);
        bool sourceArray = from.Target == TextureDimension.OneArray;
        bool destinationArray = to.Target == TextureDimension.OneArray;
        if (sourceArray != destinationArray) throw new ArgumentException("One-dimensional array copies require matching dimensionality.");
        int sourceLayer = sourceRegion.Subresource.Layer + sourceRegion.Z;
        int destinationLayer = destinationRegion.Subresource.Layer + destinationRegion.Z;
        if (source.Resource.ResourceId == destination.Resource.ResourceId && sourceRegion.Subresource.MipLevel == destinationRegion.Subresource.MipLevel
            && sourceRegion.X < destinationRegion.X + destinationRegion.Width && destinationRegion.X < sourceRegion.X + sourceRegion.Width
            && sourceRegion.Y < destinationRegion.Y + destinationRegion.Height && destinationRegion.Y < sourceRegion.Y + sourceRegion.Height
            && sourceLayer < destinationLayer + destinationRegion.Depth && destinationLayer < sourceLayer + sourceRegion.Depth)
            throw new ArgumentException("Copy regions within the same image must not overlap.");
        GL.CopyImageSubData((int)source.Resource.ResourceId, (Native.ImageTarget)OpenGLImageValues.Target(from.Target), sourceRegion.Subresource.MipLevel,
            sourceRegion.X, sourceArray ? sourceLayer : sourceRegion.Y, sourceArray ? 0 : sourceLayer,
            (int)destination.Resource.ResourceId, (Native.ImageTarget)OpenGLImageValues.Target(to.Target), destinationRegion.Subresource.MipLevel,
            destinationRegion.X, destinationArray ? destinationLayer : destinationRegion.Y, destinationArray ? 0 : destinationLayer,
            sourceRegion.Width, sourceArray ? sourceRegion.Depth : sourceRegion.Height, sourceArray ? 1 : sourceRegion.Depth);
        RequireContext();
    }
    #endregion
}
