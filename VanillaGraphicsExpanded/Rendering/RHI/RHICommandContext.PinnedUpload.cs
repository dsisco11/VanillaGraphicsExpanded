using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Consumes caller-owned pinned pixels during an immediate upload without retaining client memory.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Adapts stable pinned bytes to the same complete semantic upload used by managed spans.</summary>
    internal unsafe void UploadTexturePointer(GpuTextureRef destination, in RHITextureRegion region, RHIPixelEncoding encoding,
        nint address, int byteCount, int rowPitch, int slicePitch)
    {
        if (address == 0 || byteCount <= 0) throw new ArgumentException("Pinned upload requires stable nonempty client storage.");
        backend.UploadTexture(destination, region, encoding, new ReadOnlySpan<byte>((void*)address, byteCount), rowPitch, slicePitch);
    }
    #endregion
}
