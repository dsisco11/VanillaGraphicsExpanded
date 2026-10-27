namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes one vertex attribute and its stream packing without retaining a buffer instance.</summary>
public readonly record struct RHIVertexAttribute(int Location, int Components, VertexComponentFormat Storage,
    VertexInterpretation Interpretation, int Binding, int Offset, int Stride, int Divisor = 0);
