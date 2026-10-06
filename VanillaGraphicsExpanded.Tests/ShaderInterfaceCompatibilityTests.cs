using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies scalar interpretation and target component contracts independent of hardware.</summary>
public sealed class ShaderInterfaceCompatibilityTests
{
    #region Public API
    /// <summary>Normalized integer storage feeds floating inputs, while integer and double inputs retain their interpretation.</summary>
    [Theory]
    [InlineData(ActiveAttribType.FloatVec4, VertexAttribPointerType.UnsignedByte, (int)VertexInterpretation.Normalized, true)]
    [InlineData(ActiveAttribType.IntVec4, VertexAttribPointerType.Int, (int)VertexInterpretation.Integer, true)]
    [InlineData(ActiveAttribType.IntVec4, VertexAttribPointerType.UnsignedInt, (int)VertexInterpretation.Integer, false)]
    [InlineData(ActiveAttribType.UnsignedIntVec4, VertexAttribPointerType.UnsignedInt, (int)VertexInterpretation.Integer, true)]
    [InlineData(ActiveAttribType.UnsignedIntVec4, VertexAttribPointerType.Int, (int)VertexInterpretation.Integer, false)]
    [InlineData(ActiveAttribType.DoubleVec4, VertexAttribPointerType.Double, (int)VertexInterpretation.Double, true)]
    [InlineData(ActiveAttribType.DoubleVec4, VertexAttribPointerType.Float, (int)VertexInterpretation.Floating, false)]
    public void VertexScalarCompatibility(ActiveAttribType type, VertexAttribPointerType storage, int interpretation, bool accepted)
    {
        var shape = ShaderInterfaceShape.From(type);
        var attribute = new VertexAttributeDesc(0, 4, storage, (VertexInterpretation)interpretation, 0, 0, 32, 2);
        Assert.Equal(accepted, shape.Accepts(attribute));
        Assert.Equal(2, new VertexLayoutDesc([attribute]).Attributes[0].Divisor);
    }

    /// <summary>Rectangular matrices retain their independent column count and row component count.</summary>
    [Theory]
    [InlineData(ActiveAttribType.FloatMat2x3, 2, 3)]
    [InlineData(ActiveAttribType.DoubleMat4x2, 4, 2)]
    public void MatrixShapePreservesOccupiedLocations(ActiveAttribType type, int columns, int components)
    {
        var shape = ShaderInterfaceShape.From(type);
        Assert.Equal(columns, shape.Columns);
        Assert.Equal(components, shape.Components);
    }

    /// <summary>Output formats retain signedness and sufficient stored component coverage.</summary>
    [Theory]
    [InlineData(PixelInternalFormat.Rgba8, ActiveAttribType.FloatVec4, true)]
    [InlineData(PixelInternalFormat.Rg16f, ActiveAttribType.FloatVec4, false)]
    [InlineData(PixelInternalFormat.Rgba8ui, ActiveAttribType.IntVec4, false)]
    [InlineData(PixelInternalFormat.Rgba8ui, ActiveAttribType.UnsignedIntVec4, true)]
    [InlineData(PixelInternalFormat.Rgba8i, ActiveAttribType.IntVec4, true)]
    [InlineData(PixelInternalFormat.Rgba16f, ActiveAttribType.UnsignedIntVec4, false)]
    public void FragmentScalarAndComponents(PixelInternalFormat format, ActiveAttribType type, bool accepted) =>
        Assert.Equal(accepted, ShaderTargetCompatibility.Accepts(format, ShaderInterfaceShape.From(type)));

    /// <summary>Renderer lifetimes reject foreign-thread use without making graphics calls there.</summary>
    [Fact]
    public void RendererLifetimeRejectsCrossThreadUse()
    {
        using var lifetime = new GraphicsPipelineLifetime();
        Exception? caught = null;
        var worker = new Thread(() => caught = Record.Exception(lifetime.Validate));
        worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(caught);
        lifetime.Validate();
    }
    #endregion
}
