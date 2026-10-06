using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Validates retained executable interfaces against immutable vertex and target contracts.</summary>
internal static class GraphicsInterfaceValidation
{
    #region Public API
    /// <summary>Rejects mismatched interfaces and unverifiable enabled clipping before publishing a realization.</summary>
    internal static void Validate(GraphicsPipelineDesc description, GraphicsExecutableInterface executable)
    {
        if (executable.GeometryInput is { } expected)
        {
            var produced = executable.TessellationOutput ?? description.Assembly.Topology switch
            {
                PrimitiveType.LineStrip or PrimitiveType.LineLoop => PrimitiveType.Lines,
                PrimitiveType.TriangleStrip or PrimitiveType.TriangleFan => PrimitiveType.Triangles,
                PrimitiveType.LineStripAdjacency => PrimitiveType.LinesAdjacency,
                PrimitiveType.TriangleStripAdjacency => PrimitiveType.TrianglesAdjacency,
                var topology => topology
            };
            if (produced != expected) throw new InvalidOperationException("Geometry input is incompatible with the preceding primitive topology.");
        }
        foreach (var input in executable.Inputs)
        {
            var shape = ShaderInterfaceShape.From(input.Type);
            for (int element = 0; element < input.ArraySize; element++)
                for (int column = 0; column < shape.Columns; column++)
                {
                    // OpenGL vertex attributes (including dvec3/dvec4) occupy one index per column.
                    int location = checked(input.Location + element * shape.Columns + column);
                    var matches = description.VertexLayout.Attributes.Where(a => a.Location == location).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException($"Missing shader attribute at location {location}.");
                    var attribute = matches[0];
                    if (attribute.Components != shape.Components || !shape.Accepts(attribute))
                        throw new InvalidOperationException($"Incompatible shader attribute at location {location}.");
                    // Divisors are geometry requirements, not shader reflection: VertexLayoutDesc validates
                    // shared binding rates, and draw adapters must match this exact immutable layout.
                }
        }
        if (!description.Rasterizer.Discard)
            foreach (var output in executable.Outputs)
            {
                var shape = ShaderInterfaceShape.From(output.Type);
                if (shape.Columns != 1 || output.LocationIndex != 0 || output.ArraySize < 1)
                    throw new NotSupportedException("Unsupported fragment output shape or dual-source index.");
                for (int element = 0; element < output.ArraySize; element++)
                {
                    int location = checked(output.Location + element);
                    if (location >= description.Targets.Colors.Count)
                        throw new InvalidOperationException($"Unrouted fragment output {location}.");
                    var slot = description.Targets.Colors[location];
                    if (slot.DiscardOutput) continue;
                    if (slot.Format is not { } format || !ShaderTargetCompatibility.Accepts(format, shape))
                        throw new InvalidOperationException($"Incompatible target for fragment output {location}.");
                }
            }
        uint mask = description.Rasterizer.ClipDistances;
        if (mask != 0 && (executable.ClipDistanceExtent is not { } extent
            || extent < 32 && (mask >> extent) != 0))
            throw new InvalidOperationException("Enabled clip distances exceed verified final-stage compiled outputs.");
    }
    #endregion
}
