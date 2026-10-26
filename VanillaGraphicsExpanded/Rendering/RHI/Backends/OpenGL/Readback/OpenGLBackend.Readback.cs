using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Enqueues owned staging copies and transfers completion ownership before returning.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHIReadbackTicket EnqueueReadback(in RHIReadbackDescription description)
    {
        RequireContext();
        var request = description;
        int bytes;
        if (request.IsTexture)
        {
            var metadata = request.Texture.RequireMetadata();
            ValidateTextureRegion(metadata, request.Region);
            int pixelBytes = OpenGLPixelValues.PixelBytes(request.Encoding);
            int row = request.RowPitch == 0 ? checked(request.Region.Width * pixelBytes) : request.RowPitch;
            int slice = request.SlicePitch == 0 ? checked(row * request.Region.Height) : request.SlicePitch;
            bytes = checked((request.Region.Depth - 1) * slice + (request.Region.Height - 1) * row + request.Region.Width * pixelBytes);
            ValidatePixelLayout(request.Region, request.Encoding, bytes, request.RowPitch, request.SlicePitch);
        }
        else
        {
            RequireRange(request.Buffer.Buffer, request.Buffer.Offset, request.Buffer.Length);
            bytes = checked((int)request.Buffer.Length);
            if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(description));
        }
        var staging = CreateBuffer(new RHIBufferDescription
        { Role = RHIBufferRole.PixelPack, Mode = RHIStorageMode.Mutable, Capacity = bytes, Usage = RHIBufferUsage.StreamRead }, default).RequireResource();
        GpuFence? fence = null;
        try
        {
            if (request.IsTexture)
                TransferTextureToBuffer(request.Texture, new(staging, 0, request.Region, request.Encoding, request.RowPitch, request.SlicePitch));
            else CopyBuffer(request.Buffer.Buffer, request.Buffer.Offset, staging, 0, bytes);
            fence = InsertFence();
            return new(this, staging, fence, bytes);
        }
        catch
        {
            // Native object destruction retains in-flight storage; never return it to a CPU staging pool.
            try { fence?.Dispose(); } catch { }
            try { staging.Dispose(); } catch { }
            throw;
        }
    }
    #endregion
}
