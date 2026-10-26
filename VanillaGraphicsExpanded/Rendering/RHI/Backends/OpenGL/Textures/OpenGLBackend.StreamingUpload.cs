using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes admitted streaming payloads without exposing raw pixel-buffer execution to the queue.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Consumes already pinned client bytes or an owned staging-buffer offset on the original context.</summary>
    internal void ExecuteStreamingUpload(in TextureUploadRequest request, nint pixels, StateCache.PixelUnpackState layout, int unpackBuffer = 0)
    {
        RequireContext();
        if (request.TextureOwner is { } owner)
        {
            owner.RequireOriginalContext();
            _ = GpuResourceManager.RequireTextureReferenceMetadata(owner);
            if (owner.TextureId != request.TextureId || !ReferenceEquals(owner.UntrackedStorageIdentity, request.StorageIdentity)
                || GpuResourceManager.TextureStorageRevision(owner) != request.StorageRevision)
                throw new InvalidOperationException("The staged destination allocation has been replaced.");
        }
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, request.TextureId);
        if (unpackBuffer != 0) cache.RequireResourceOperationAvailable(GpuResourceKind.Buffer, unpackBuffer);
        var input = request.Region;
        var target = request.Target.UploadTarget;
        bool face = target is >= TextureTarget.TextureCubeMapPositiveX and <= TextureTarget.TextureCubeMapNegativeZ;
        var region = new RHITextureRegion(new(input.MipLevel, face ? target - TextureTarget.TextureCubeMapPositiveX : 0),
            input.X, target == TextureTarget.Texture1DArray ? 0 : input.Y,
            target == TextureTarget.Texture1DArray ? input.Y : input.Z, input.Width,
            target == TextureTarget.Texture1DArray ? 1 : input.Height, target == TextureTarget.Texture1DArray ? input.Height : input.Depth);
        using var unpack = cache.BindBufferScope(BufferTarget.PixelUnpackBuffer, unpackBuffer);
        using var state = OpenGLPixelLayouts.UnpackScope(cache, layout);
        UploadImagePixels(request.TextureId, request.Target.BindTarget, region, OpenGLPixelValues.Encoding(request.PixelFormat, request.PixelType), pixels);
        RequireContext();
    }
    #endregion
}
