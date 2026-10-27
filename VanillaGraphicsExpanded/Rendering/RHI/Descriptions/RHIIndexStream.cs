namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects an explicit index byte interval and its unsigned scalar interpretation.</summary>
/// <remarks>Direct FirstIndex is relative to this interval. Indirect FirstIndex is buffer-relative;
/// its producer must keep every generated access inside the declared interval.</remarks>
public readonly record struct RHIIndexStream(RHIBufferRange Range, IndexFormat Format = IndexFormat.UnsignedInt);
