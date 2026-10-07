using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks array submission against borrowed streaming storage and exact vertex ranges.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ArrayGraphicsGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Streaming replacements change draw pixels while bounds follow the current storage and geometry borrows buffers.</summary>
    [Fact]
    public void StreamingStorageChangesAreVisibleAndBounded()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        var layout = new VertexLayoutDesc([new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12)]);
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, layout,
            new([new(PixelInternalFormat.R32f)]), DynamicPipelineState.Viewport), shader);
        using var buffer = GpuVbo.Create();
        buffer.UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo> { [0] = buffer });
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        Assert.True(GraphicsCommandContext.TryRun("ArrayGeometry", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.Draw(geometry, new(0, 3));
            long draws = StateCache.Current.DrawSubmissions;
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(geometry, new(1, 3)));
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(geometry, new(0, 3, BaseVertex: 1)));
            buffer.UploadData(new float[] { -1, -1, 0 });
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(geometry, new(0, 3)));
            Assert.Equal(draws, StateCache.Current.DrawSubmissions);
        }));
        Assert.InRange(target[0].ReadPixels()[0], .1999f, .2001f);
        geometry.Dispose(); Assert.True(buffer.IsValid);
        Assert.Throws<ObjectDisposedException>(() => geometry.Validate(pipeline.Description, new(0, 1)));
        using var borrowed = new ArrayGraphicsGeometry(layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo> { [0] = buffer });
        buffer.Dispose(); Assert.Throws<InvalidOperationException>(() => borrowed.Validate(pipeline.Description, new(0, 1)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Empty procedural layouts need bounded ranges and instanced streams account for their divisor.</summary>
    [Fact]
    public void ProceduralAndInstancedRangesRejectOverflow()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms(); var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
        using var procedural = new ArrayGraphicsGeometry(new([]), PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), 3);
        var description = new GraphicsPipelineDesc(shader.GraphicsIdentity!, new([]), new([new(PixelInternalFormat.R32f)]), DynamicPipelineState.Viewport);
        procedural.Validate(description, new(0, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => procedural.Validate(description, new(int.MaxValue, int.MaxValue)));
        using var buffer = GpuVbo.Create(); buffer.UploadData(new float[] { 0, 0, 0 });
        var layout = new VertexLayoutDesc([new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12, 2)]);
        using var instanced = new ArrayGraphicsGeometry(layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo> { [0] = buffer });
        description = new(shader.GraphicsIdentity!, layout, new([new(PixelInternalFormat.R32f)]), DynamicPipelineState.Viewport);
        instanced.Validate(description, new(0, 3, InstanceCount: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => instanced.Validate(description, new(0, 3, InstanceCount: 3)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
