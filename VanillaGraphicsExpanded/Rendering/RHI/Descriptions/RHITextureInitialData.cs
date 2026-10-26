namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains stable initial bytes and their explicit region/layout until immediate creation returns.</summary>
public readonly record struct RHITextureInitialData(RHITextureRegion Region, RHIPixelEncoding Encoding, System.ReadOnlyMemory<byte> Bytes, int RowPitch = 0, int SlicePitch = 0);
