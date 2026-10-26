namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes a GPU buffer-image transfer without client pointers or raw pixel-buffer scopes.</summary>
public readonly record struct RHITextureTransfer(GpuBufferRef Buffer, nint Offset, RHITextureRegion Region, RHIPixelEncoding Encoding, int RowPitch = 0, int SlicePitch = 0);
