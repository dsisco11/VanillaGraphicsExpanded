using System;
using System.Linq;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Validates retained executable interfaces against immutable vertex and target contracts.</summary>
internal static class GraphicsInterfaceValidation
{
    #region Public API
    /// <summary>Rejects mismatched interfaces before publishing a realization.</summary>
    internal static void Validate(GraphicsPipelineDesc description, GraphicsExecutableInterface executable)
    {
        ValidateDeclarations(executable);
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
    }
    /// <summary>Checks compiler declarations before a linked candidate can replace an installed executable.</summary>
    internal static void ValidateDeclarations(GraphicsExecutableInterface executable)
    {
        ValidateVariables(executable.Inputs, executable.DeclaredInputs);
        ValidateVariables(executable.Outputs, executable.DeclaredOutputs);
    }
    #endregion

    #region Private
    /// <summary>Allows linker elimination while requiring every surviving numeric variable to match compiler declarations.</summary>
    private static void ValidateVariables(IReadOnlyList<GraphicsExecutableInterface.Variable> active,
        IReadOnlyList<PackagedInterfaceVariable> declarations)
    {
        foreach (var variable in active)
        {
            var shape = ShaderInterfaceShape.From(variable.Type);
            var scalar = shape.Interpretation == VertexInterpretation.Integer
                ? shape.Unsigned ? ShaderScalarType.UInt : ShaderScalarType.Int : ShaderScalarType.Float;
            uint bits = shape.Interpretation == VertexInterpretation.Double ? 64u : 32u;
            bool covered = declarations.Any(declaration =>
            {
                if (declaration.Scalar != scalar || declaration.BitWidth != bits ||
                    declaration.VectorSize != shape.Components || declaration.Columns != shape.Columns ||
                    declaration.Index != variable.LocationIndex || declaration.Component != 0) return false;
                long elements = 1;
                foreach (uint dimension in declaration.ArrayDimensions) elements = checked(elements * dimension);
                long offset = variable.Location - (long)declaration.Location;
                return offset >= 0 && offset % shape.Columns == 0 &&
                    offset / shape.Columns + variable.ArraySize <= elements;
            });
            if (!covered) throw new InvalidOperationException($"Linked shader location {variable.Location} disagrees with packaged compiler declarations.");
        }
    }
    #endregion
}
