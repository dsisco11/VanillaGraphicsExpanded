using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>One attribute's structural layout, excluding the buffer instance and base offset.</summary>
internal readonly record struct VertexAttributeDesc(int Location, int Components, VertexAttribPointerType Storage,
    VertexInterpretation Interpretation, int Binding, int Offset, int Stride, int Divisor = 0);
