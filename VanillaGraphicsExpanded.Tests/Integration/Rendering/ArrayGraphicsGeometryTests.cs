using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.RHI;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises final geometry packets, published allocation bounds and independently owned stream lifetimes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ArrayGraphicsGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Frozen packets preserve declared extents, new packets see growth, and expired or shrunk allocations reject before drawing.</summary>
    [Fact]
    public void StreamingStorageChangesAreVisibleAndBounded()
    {
        // Arrange: Capture one packet before the mutable vertex allocation grows; later packets should see its new extent.
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12)]);
        using var buffer = commands.CreateVertexBuffer(new() { Role=BufferRole.Vertex, Capacity = 36, Mode = ResourceStorageMode.Mutable, Usage=BufferUsage.StaticDraw }).RequireResource();
        buffer.UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo> { [0] = buffer });
        using var source = CreateSource();
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
        var pass = FramebufferTestSetup.DescribeAttachments(target);
        using var pipeline = commands.CreateGraphicsPipeline(new(shader.Shader, layout, commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        shader.HzbDepth = source; shader.SrcMip = 0;
        // Capture the original packet before growth so it keeps its declared extent while new packets see more storage.
        var original = geometry.Inputs;
        using var boundary = commands.EnterBoundary(new("Tests.Geometry.Streams", true, graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        // Act: Draw across growth, shrink, and owner disposal while the original packet remains retained.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.BindResources(shader.CaptureBindings());
            commands.Draw(original, new(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(original, new(1, 3)));
            buffer.UploadData(new float[12]);
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(original, new(0, 4)));
            commands.Draw(geometry.Inputs, new(0, 4, InstanceCount: 0));
            buffer.UploadData(new float[3]);
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(original, new(0, 3)));
            var expired = geometry.Inputs;
            geometry.Dispose();
#if DEBUG
            Assert.Throws<ObjectDisposedException>(() => commands.Draw(expired, new(0, 0)));
#endif
            Assert.True(buffer.IsValid);
            commands.EndRenderPass();
        });
        // Assert: The surviving in-range draw still produces the known minimum depth.
        Assert.InRange(target[0].ReadTexel()[0], .1999f, .2001f);
    }

    /// <summary>Procedural and divisor-based instance fetches enforce declared bounds through the actual draw executor.</summary>
    [Fact]
    public void ProceduralAndInstancedRangesRejectOverflow()
    {
        // Arrange: Give procedural and divisor-based inputs distinct bounds so each draw path is checked independently.
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        var commands = RHICommandContext.Current;
        using var source = CreateSource();
        shader.HzbDepth = source; shader.SrcMip = 0;
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
        var pass = FramebufferTestSetup.DescribeAttachments(target);
        using var buffer = commands.CreateVertexBuffer(new() { Role=BufferRole.Vertex, Capacity = 12, Mode = ResourceStorageMode.Mutable, Usage=BufferUsage.StaticDraw }).RequireResource();
        var layout = new RHIVertexLayout([new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12, 2)]);
        using var procedural = new ArrayGraphicsGeometry(new([]), PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo>(), 3);
        using var instanced = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo> { [0] = buffer });
        using var direct = commands.CreateGraphicsPipeline(new(shader.Shader, procedural.Inputs.Layout, commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var instance = commands.CreateGraphicsPipeline(new(shader.Shader, layout, commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.Geometry.Ranges", true, graphics: [direct, instance], operations: BoundaryOperations.RenderPasses));
        // Act: Submit valid ranges and then overflow each path through the real draw executor.
        // Assert: Procedural and instanced overflows throw while their in-range draws complete through the pass.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass); commands.SetPipeline(direct);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.BindResources(shader.CaptureBindings());
            commands.Draw(procedural.Inputs, new(0, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(procedural.Inputs, new(int.MaxValue, int.MaxValue)));
            commands.SetPipeline(instance); commands.BindResources(shader.CaptureBindings());
            commands.Draw(instanced.Inputs, new(0, 3, InstanceCount: 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.Draw(instanced.Inputs, new(0, 3, InstanceCount: 3)));
            commands.EndRenderPass();
        });
    }

    /// <summary>Indexed and instanced draws honor explicit byte intervals and retirement without reading index contents.</summary>
    [Fact]
    public void IndexedPacketsValidateIntervalsAndOriginalAllocation()
    {
        // Arrange: Place index data inside a larger allocation so packet-relative and allocation-relative bounds differ.
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        var commands = RHICommandContext.Current;
        using var source = CreateSource();
        shader.HzbDepth = source; shader.SrcMip = 0;
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
        var pass = FramebufferTestSetup.DescribeAttachments(target);
        var layout = new RHIVertexLayout([]);
        using var indices = commands.CreateIndexBuffer(new() { Role=BufferRole.Index, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage=BufferUsage.StaticDraw }).RequireResource();
        indices.UploadIndices(new uint[] { 99, 0, 1, 2 });
        var geometry = new RHIGeometryInputs(layout, [], new(new(indices, 4, 12), IndexFormat.UnsignedInt));
        using var pipeline = commands.CreateGraphicsPipeline(new(shader.Shader, layout, commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.Geometry.Indices", true, graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        // Act: Draw valid packet intervals, reject invalid ranges, then retire the original index allocation.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.BindResources(shader.CaptureBindings());
            commands.DrawIndexed(geometry, new(0, 3, InstanceCount: 2));
            commands.DrawIndexed(geometry, new(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.DrawIndexed(geometry, new(-1, 3)));
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.DrawIndexed(geometry, new(1, 3)));
            indices.Dispose();
#if DEBUG
            Assert.Throws<ObjectDisposedException>(() => commands.DrawIndexed(geometry, new(0, 3)));
#endif
            commands.EndRenderPass();
        });
        // Assert: Valid indexed work rendered the deterministic depth before retirement.
        Assert.InRange(target[0].ReadTexel()[0], .1999f, .2001f);
    }
    #endregion

    #region Private
    /// <summary>Supplies deterministic minimum-depth texels through normal resource creation.</summary>
    private static DynamicTexture2D CreateSource() => RHICommandContext.Current.CreateDynamicTexture2D(new()
    {
        Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f,
        MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest
    }, [new(new(new(), 0, 0, 0, 2, 2), new(PixelChannelLayout.Red, PixelComponentType.Float),
        MemoryMarshal.AsBytes(new float[] { .2f, .4f, .6f, .8f }.AsSpan()).ToArray())]).RequireResource();
    #endregion
}
