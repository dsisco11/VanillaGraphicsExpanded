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
    /// <summary>Submission ownership rejects nesting and restores authority after a declared external operation.</summary>
    [Fact]
    public void PassNestingAndExternalBoundariesAreExplicit()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
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
        var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry();
        using var first = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var second = DynamicTexture2D.CreateMipmapped(4, 4, PixelInternalFormat.R32f, 2);
        second.Bind(0);
        GL.TexSubImage2D(TextureTarget.Texture2D, 1, 0, 0, 2, 2, PixelFormat.Red, PixelType.Float, new[] { .7f, .8f, .9f, 1f });
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        var cache = StateCache.Current;
        int graphicsQueries = shader.GraphicsInterface!.ReflectionQueries;
        int resourceQueries = pipeline.Bindings.ReflectionQueries;
        shader.HzbDepth = first; shader.SrcMip = 0;
        Assert.True(GraphicsCommandContext.TryRun("SubmissionInputs", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)]));
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
            long calls = cache.FixedFunctionCalls, queries = cache.BoundaryQueries, draws = cache.DrawSubmissions;
            commands.Draw(geometry, new(0, 3, InstanceCount: 2));
            Assert.Equal(calls, cache.FixedFunctionCalls);
            Assert.Equal(queries, cache.BoundaryQueries);
            Assert.Equal(draws + 1, cache.DrawSubmissions);
            shader.HzbDepth = second; shader.SrcMip = 1;
            commands.Draw(geometry, new(0, 3));
            commands.EndPass();
        }));
        Assert.InRange(target[0].ReadPixels()[0], .6999f, .7001f);
        Assert.Equal(graphicsQueries, shader.GraphicsInterface.ReflectionQueries);
        Assert.Equal(resourceQueries, pipeline.Bindings.ReflectionQueries);
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
        var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry(failure == "layout" ? 2 : 3, failure == "topology" ? PrimitiveType.Lines : PrimitiveType.Triangles);
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var target = CreateRenderTarget(1, 1, failure == "target" ? PixelInternalFormat.Rgba16f : PixelInternalFormat.R32f);
        if (failure != "resources") shader.HzbDepth = source;
        shader.SrcMip = 0;
        long before = StateCache.Current.DrawSubmissions;
        Assert.True(GraphicsCommandContext.TryRun("SubmissionFailure", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            if (failure != "dynamics") commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            if (failure == "retired") geometry.Dispose();
            if (failure == "executable") shader.InvalidateAssets();
            int baseVertex = failure == "negative-base" ? -1 : failure == "base-range" ? 1 : 0;
            Assert.ThrowsAny<Exception>(() => commands.Draw(geometry, new(0, failure == "range" ? 4 : 3,
                BaseVertex: baseVertex, InstanceCount: failure == "instances" ? 0 : 1)));
            Assert.Equal(before, StateCache.Current.DrawSubmissions);
        }));
        Assert.Equal(before, StateCache.Current.DrawSubmissions);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Scope disposal restores engine shader and drawing ownership when user work throws.</summary>
    [Fact]
    public void ExceptionsRestoreOwnershipAndAllowAnotherSubmission()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnHzbDownsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = Pipeline(lifetime, shader);
        using var geometry = Geometry();
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.2f, .4f, .6f, .8f]);
        using var target = CreateRenderTarget(1, 1, PixelInternalFormat.R32f);
        shader.HzbDepth = source; shader.SrcMip = 0;
        var previous = programs.Create<LumOnHzbDownsampleShaderProgram>();
        previous.HzbDepth = source; previous.SrcMip = 0;
        using var previousScope = previous.UseScope();
        var owner = ShaderProgramBase.CurrentShaderProgram;
        Assert.Same(previous, owner);
        int framebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport);
        Assert.Throws<ArithmeticException>(() => GraphicsCommandContext.TryRun("SubmissionThrow", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
            throw new ArithmeticException("Fixture failure after submission.");
        }));
        Assert.Same(owner, ShaderProgramBase.CurrentShaderProgram);
        Assert.Equal(framebuffer, GL.GetInteger(GetPName.DrawFramebufferBinding));
        int[] restored = new int[4]; GL.GetInteger(GetPName.Viewport, restored); Assert.Equal(viewport, restored);
        Assert.True(GraphicsCommandContext.TryRun("SubmissionRetry", [pipeline], true, commands =>
        {
            commands.BeginPass(new(target, [new(0)])); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport }); commands.Draw(geometry, new(0, 3));
        }));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Declares the exact production HZB vertex and single floating-point output contract.</summary>
    private static GraphicsPipeline Pipeline(GraphicsPipelineLifetime lifetime, LumOnHzbDownsampleShaderProgram shader) =>
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
