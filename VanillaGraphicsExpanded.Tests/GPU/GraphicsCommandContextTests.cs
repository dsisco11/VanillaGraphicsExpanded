using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises authoritative immediate submission against production shader inputs and real target pixels.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GraphicsCommandContextTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Depth/stencil clear masks remain valid independently of disabled draw tests and restore disabled incoming masks.</summary>
    [Fact]
    public void PackedClearThenDisabledTestsPreservesClearedAspectsAndExactMasks()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, Layout(3),
            new([new(PixelInternalFormat.R32f)], PixelInternalFormat.Depth24Stencil8), DynamicPipelineState.Viewport), shader);
        using var geometry = Geometry();
        using var source = DynamicTexture2D.CreateWithData(8, 8, PixelInternalFormat.R32f, Enumerable.Repeat(.2f, 64).ToArray());
        using var color = DynamicTexture2D.Create(4, 4, PixelInternalFormat.R32f);
        using var depth = new DepthStencilTexture(4, 4);
        using var colorImage = GpuFramebufferAttachment.FromTexture(color);
        using var depthImage = GpuFramebufferAttachment.FromTexture(depth);
        using var target = DynamicGpuFramebuffer.CreateEmpty();
        target.SetAttachment(FramebufferAttachment.ColorAttachment0, colorImage);
        target.SetAttachment(FramebufferAttachment.DepthStencilAttachment, depthImage);
        shader.HzbDepth = source; shader.SrcMip = 0;
        GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.StencilTest);
        GL.DepthMask(false); GL.StencilMaskSeparate(StencilFace.Front, 0x35); GL.StencilMaskSeparate(StencilFace.Back, 0x53);
        StateCache.Current.InvalidateAll();
        Assert.True(GraphicsCommandContext.TryRun("PackedClearAndDraw", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)], new(DepthLoad: AttachmentLoad.Clear, ClearDepth: .75f,
                StencilLoad: AttachmentLoad.Clear, ClearStencil: 0x59)));
            commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
            Assert.False(GL.IsEnabled(EnableCap.DepthTest)); Assert.False(GL.IsEnabled(EnableCap.StencilTest));
            Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
            Assert.Equal(uint.MaxValue, unchecked((uint)GL.GetInteger(GetPName.StencilWritemask)));
            Assert.Equal(uint.MaxValue, unchecked((uint)GL.GetInteger(GetPName.StencilBackWritemask)));
        }));
        Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(0x35, GL.GetInteger(GetPName.StencilWritemask)); Assert.Equal(0x53, GL.GetInteger(GetPName.StencilBackWritemask));
        Assert.All(color.ReadPixels(), value => Assert.InRange(value, .1999f, .2001f));
        Assert.All(depth.ReadPixels(), value => Assert.InRange(value, .7499f, .7501f));
        Assert.All(depth.ReadStencilPixels(), value => Assert.Equal((byte)0x59, value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A region clear establishes its own scissor before an unscissored prepared draw covers the entire target.</summary>
    [Fact]
    public void RegionClearThenDrawUsesIndependentStateAndRestoresDormantRectangle()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry();
        using var source = DynamicTexture2D.CreateWithData(8, 8, PixelInternalFormat.R32f, Enumerable.Repeat(.2f, 64).ToArray());
        using var target = CreateRenderTarget(4, 4, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        GL.Disable(EnableCap.ScissorTest); GL.Scissor(2, 1, 1, 2); GL.DepthMask(false);
        StateCache.Current.InvalidateAll();
        Assert.True(GraphicsCommandContext.TryRun("RegionClearAndDraw", [pipeline], true, commands =>
        {
#if DEBUG
            long before = StateCache.Current.FixedFunctionCalls;
#endif
            Assert.Throws<InvalidOperationException>(() => StateCache.Current.SetLineWidth(1));
#if DEBUG
            Assert.Equal(before, StateCache.Current.FixedFunctionCalls);
#endif
            commands.BeginPass(new(target, [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(.9f, 0, 0, 1))],
                area: new(1, 1, 2, 2)));
            commands.EndPass();
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
            Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
            int[] rectangle = new int[4]; GL.GetInteger(GetPName.ScissorBox, rectangle);
            Assert.Equal(new[] { 1, 1, 2, 2 }, rectangle);
            Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        }));
        Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
        int[] restored = new int[4]; GL.GetInteger(GetPName.ScissorBox, restored);
        Assert.Equal(new[] { 2, 1, 1, 2 }, restored);
        Assert.All(target[0].ReadPixels(), value => Assert.InRange(value, .1999f, .2001f));
        Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Submission ownership rejects nesting and restores authority after a declared external operation.</summary>
    [Fact]
    public void PassNestingAndExternalBoundariesAreExplicit()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry();
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        GraphicsCommandContext? escaped = null;
        Assert.True(GraphicsCommandContext.TryRun("SubmissionBoundary", [pipeline], true, commands =>
        {
            escaped = commands;
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, 3)));
            commands.BeginPass(new(target, [new(0)]));
            Assert.ThrowsAny<Exception>(() => commands.BeginPass(new(target, [new(0)])));
            bool executed = false;
            Assert.ThrowsAny<Exception>(() => GraphicsCommandContext.ExecuteExternal(EPipelineState.All, () => executed = true));
            Assert.False(executed);
            commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3)); commands.EndPass();
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, 3)));
        }));
        Assert.Throws<ObjectDisposedException>(() => escaped!.BeginPass(new(target, [new(0)])));
        Assert.Throws<ArithmeticException>(() => GraphicsCommandContext.ExecuteExternal(EPipelineState.All,
            () => { GL.ColorMask(false, false, false, false); GL.Viewport(0, 0, 0, 0); throw new ArithmeticException(); }));
        Assert.True(GraphicsCommandContext.TryRun("SubmissionAfterExternal", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.Draw(geometry, new(0, 3));
            commands.EndPass(); commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, 3)));
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.Draw(geometry, new(0, 3));
        }));
        Assert.InRange(target[0].ReadPixels()[0], .1999f, .2001f);
        GL.ColorMask(true, true, true, true); StateCache.Current.InvalidateAll();
    }

    /// <summary>Unchanged pipeline identity still publishes changed texture and numeric inputs on each draw.</summary>
    [Fact]
    public void SamePipelinePublishesChangedInputsAndSuppressesStableState()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry();
        using var first = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var second = DynamicTexture2D.CreateMipmapped(4, 4, PixelInternalFormat.R32f, 2);
        second.Bind(0);
        GL.TexSubImage2D(TextureTarget.Texture2D, 1, 0, 0, 2, 2, PixelFormat.Red, PixelType.Float, new[] { .7f, .8f, .9f, 1f });
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        var cache = StateCache.Current;
#if DEBUG
        int graphicsQueries = shader.GraphicsInterface!.ReflectionQueries;
        using var acquisition = new ProgramAcquisitionCapture();
        int resourceQueries = acquisition.Reads;
#endif
        shader.HzbDepth = first; shader.SrcMip = 0;
        Assert.True(GraphicsCommandContext.TryRun("SubmissionInputs", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.Draw(geometry, new(0, 3));
#if DEBUG
            long calls = cache.FixedFunctionCalls, queries = cache.BoundaryQueries, draws = cache.DrawSubmissions;
#endif
            commands.Draw(geometry, new(0, 3, InstanceCount: 2));
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
            Assert.Equal(queries, cache.BoundaryQueries);
            Assert.Equal(draws + 1, cache.DrawSubmissions);
#endif
            shader.HzbDepth = second; shader.SrcMip = 1;
            commands.Draw(geometry, new(0, 3));
            commands.EndPass();
        }));
        Assert.InRange(target[0].ReadPixels()[0], .6999f, .7001f);
#if DEBUG
        Assert.Equal(graphicsQueries, shader.GraphicsInterface.ReflectionQueries);
        Assert.Equal(resourceQueries, acquisition.Reads);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Missing dynamics, missing resources, incompatible layouts and invalid ranges never submit a draw.</summary>
    [Theory]
    [InlineData("dynamics")]
    [InlineData("resources")]
    [InlineData("layout")]
    [InlineData("range")]
    [InlineData("retired")]
    [InlineData("executable")]
    [InlineData("topology")]
    [InlineData("instances")]
    [InlineData("target")]
    [InlineData("negative-base")]
    [InlineData("base-range")]
    public void InvalidDrawContractEmitsNoDraw(string failure)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry(failure == "layout" ? 2 : 3, failure == "topology" ? PrimitiveType.Lines : PrimitiveType.Triangles);
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var target = CreateRenderTarget(1, 1, failure == "target" ? PixelInternalFormat.Rgba16f : PixelInternalFormat.R32f);
        if (failure != "resources") shader.HzbDepth = source;
        shader.SrcMip = 0;
#if DEBUG
        long before = StateCache.Current.DrawSubmissions;
#endif
        Assert.True(GraphicsCommandContext.TryRun("SubmissionFailure", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            if (failure != "dynamics") commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            if (failure == "retired") geometry.Dispose();
            if (failure == "executable") shader.InvalidateAssets();
            int baseVertex = failure == "negative-base" ? -1 : failure == "base-range" ? 1 : 0;
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, failure == "range" ? 4 : 3,
                BaseVertex: baseVertex, InstanceCount: failure == "instances" ? 0 : 1)));
#if DEBUG
            Assert.Equal(before, StateCache.Current.DrawSubmissions);
#endif
        }));
#if DEBUG
        Assert.Equal(before, StateCache.Current.DrawSubmissions);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Scope disposal restores engine shader and drawing ownership when user work throws.</summary>
    [Fact]
    public void ExceptionsRestoreOwnershipAndAllowAnotherSubmission()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry();
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        var previous = programs.Create<RasterDepthReductionShaderProgram>();
        previous.HzbDepth = source; previous.SrcMip = 0;
        using var previousScope = previous.UseScope();
        var owner = StateCache.ActiveProgram;
        Assert.Same(previous, owner);
        int framebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
        Assert.Throws<ArithmeticException>(() => GraphicsCommandContext.TryRun("SubmissionThrow", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)]));
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
            throw new ArithmeticException("Fixture failure after submission.");
        }));
        Assert.Same(owner, StateCache.ActiveProgram);
        Assert.Equal(framebuffer, GL.GetInteger(GetPName.DrawFramebufferBinding));
        int[] restored = new int[4]; GL.GetInteger(GetPName.Viewport, restored); Assert.Equal(viewport, restored);
        Assert.True(GraphicsCommandContext.TryRun("SubmissionRetry", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)]));
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
        }));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declares the exact production HZB vertex and single floating-point output contract.</summary>
    private static GraphicsPipeline Pipeline(GraphicsPipelineLifetime lifetime, RasterDepthReductionShaderProgram shader) =>
        new(lifetime, new(shader.GraphicsIdentity!, Layout(3), new([new(PixelInternalFormat.R32f)]), DynamicPipelineState.Viewport), shader);

    /// <summary>Declares one tightly packed floating position stream.</summary>
    private static VertexLayoutDesc Layout(int components) =>
        new([new(0, components, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, components * sizeof(float))]);

    /// <summary>Owns a fullscreen triangle with retained CPU index bounds and explicit vertex metadata.</summary>
    private static ManagedGraphicsGeometry Geometry(int components = 3, PrimitiveType topology = PrimitiveType.Triangles)
    {
        float[] values = components == 3 ? [-1, -1, 0, 3, -1, 0, -1, 3, 0] : [-1, -1, 3, -1, -1, 3];
        return ManagedGraphicsGeometry.Create(Layout(components), topology,
            [new(0, MemoryMarshal.AsBytes(values.AsSpan()).ToArray())], [0, 1, 2]);
    }
    #endregion
}
