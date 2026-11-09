using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Verifies scalar interpretation and target component contracts independent of hardware.</summary>
public sealed class ShaderInterfaceCompatibilityTests
{
    #region Public API
    /// <summary>Normalized integer storage feeds floating inputs, while integer and double inputs retain their interpretation.</summary>
    [Theory]
    [InlineData(ShaderValueType.FloatVec4, VertexComponentFormat.UnsignedByte, (int)VertexInterpretation.Normalized, true)]
    [InlineData(ShaderValueType.IntVec4, VertexComponentFormat.SignedInt, (int)VertexInterpretation.Integer, true)]
    [InlineData(ShaderValueType.IntVec4, VertexComponentFormat.UnsignedInt, (int)VertexInterpretation.Integer, false)]
    [InlineData(ShaderValueType.UnsignedIntVec4, VertexComponentFormat.UnsignedInt, (int)VertexInterpretation.Integer, true)]
    [InlineData(ShaderValueType.UnsignedIntVec4, VertexComponentFormat.SignedInt, (int)VertexInterpretation.Integer, false)]
    [InlineData(ShaderValueType.DoubleVec4, VertexComponentFormat.Double, (int)VertexInterpretation.Double, true)]
    [InlineData(ShaderValueType.DoubleVec4, VertexComponentFormat.Float, (int)VertexInterpretation.Floating, false)]
    public void VertexScalarCompatibility(ShaderValueType type, VertexComponentFormat storage, int interpretation, bool accepted)
    {
        // Arrange
        var shape = ShaderInterfaceShape.From(type);
        var attribute = new RHIVertexAttribute(0, 4, storage, (VertexInterpretation)interpretation, 0, 0, 32, 2);

        // Act
        bool compatible = shape.Accepts(attribute);

        // Assert
        Assert.Equal(accepted, compatible);
        Assert.Equal(2, new RHIVertexLayout([attribute]).Attributes[0].Divisor);
    }

    /// <summary>Rectangular matrices retain their independent column count and row component count.</summary>
    [Theory]
    [InlineData(ShaderValueType.FloatMat2x3, 2, 3)]
    [InlineData(ShaderValueType.DoubleMat4x2, 4, 2)]
    public void MatrixShapePreservesOccupiedLocations(ShaderValueType type, int columns, int components)
    {
        // Arrange each matrix shape and expected column/component counts from the theory rows.
        // Act by resolving its interface shape, then assert the reported dimensions.
        var shape = ShaderInterfaceShape.From(type);

        // Assert
        Assert.Equal(columns, shape.Columns);
        Assert.Equal(components, shape.Components);
    }

    /// <summary>Output formats retain signedness and sufficient stored component coverage.</summary>
    [Theory]
    [InlineData(TextureFormat.Rgba8, ShaderValueType.FloatVec4, true)]
    [InlineData(TextureFormat.Rg16f, ShaderValueType.FloatVec4, false)]
    [InlineData(TextureFormat.Rgba8ui, ShaderValueType.IntVec4, false)]
    [InlineData(TextureFormat.Rgba8ui, ShaderValueType.UnsignedIntVec4, true)]
    [InlineData(TextureFormat.Rgba8i, ShaderValueType.IntVec4, true)]
    [InlineData(TextureFormat.Rgba16f, ShaderValueType.UnsignedIntVec4, false)]
    public void FragmentScalarAndComponents(TextureFormat format, ShaderValueType type, bool accepted)
    {
        // Arrange the render-target format and fragment scalar type from each compatibility row.
        // Act by checking that pairing, then assert the declared acceptance value.
        bool compatible = ShaderTargetCompatibility.Accepts(format, ShaderInterfaceShape.From(type));

        // Assert
        Assert.Equal(accepted, compatible);
    }

    #endregion
}
