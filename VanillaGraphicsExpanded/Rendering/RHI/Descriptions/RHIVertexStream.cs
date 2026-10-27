namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Selects a bounded vertex stream and its immutable packing and instance rate.</summary>
public readonly record struct RHIVertexStream(int Binding, RHIBufferRange Range, int Stride, int Divisor = 0);
