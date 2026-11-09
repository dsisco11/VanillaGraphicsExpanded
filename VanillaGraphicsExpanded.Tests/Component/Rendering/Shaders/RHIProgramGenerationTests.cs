using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Checks coherent graphics asset publication while RHI references retain exact executable and pipeline generations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIProgramGenerationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A failed renderer candidate leaves both installed generations usable; success publishes a compatible pair.</summary>
    [Fact]
    public void FailedPipelineCandidatePreservesInstalledSet()
    {
        // Arrange: install one graphics asset whose candidate factory can fail deterministically.
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        var commands=RHICommandContext.Current;var layout=EngineFullscreenGeometry.Layout;
        var targets=new RenderTargetSignature([new(TextureFormat.Rgba32f)]);
        bool fail=false;
        using var asset=new GraphicsPipelineAsset(shader,new(shader.Shader,layout,targets,DynamicPipelineState.Viewport),
            candidate=>fail?throw new ArgumentException("Controlled renderer contract failure."):new(candidate,layout,targets,DynamicPipelineState.Viewport));
        var installed=shader.Shader;var previous=asset.Pipeline;var captured=shader.CaptureBindings();
        // Act: reject one candidate, then allow a second build to publish.
        fail=true;Assert.False(shader.CompileAndLink());
        // Assert: failure keeps the installed shader and pipeline usable.
        Assert.Equal(installed,shader.Shader);Assert.Same(previous,asset.Pipeline);
        Assert.False(previous.IsDisposed);Assert.Same(installed.RequireShader(),previous.RequireShader());Assert.Equal(installed,captured.Shader);
        // Act: compile again with the controlled failure disabled.
        fail=false;Assert.True(shader.CompileAndLink());
        // Assert: success publishes a new compatible pair and retires the previous generation.
        Assert.NotEqual(installed,shader.Shader);
        Assert.NotSame(previous,asset.Pipeline);Assert.Equal(shader.Shader,asset.Description.Shader);Assert.True(previous.IsDisposed);
        Assert.Throws<ObjectDisposedException>(()=>installed.RequireShader());
    }
    /// <summary>Changing asset admission during candidate construction cannot publish an older completed selection.</summary>
    [Fact]
    public void SupersededCandidateCannotInstall()
    {
        // Arrange: make asset admission change from inside candidate construction.
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        var layout=EngineFullscreenGeometry.Layout;var targets=new RenderTargetSignature([new(TextureFormat.Rgba32f)]);
        bool supersede=false;
        using var asset=new GraphicsPipelineAsset(shader,new(shader.Shader,layout,targets,DynamicPipelineState.Viewport),candidate=>
        {
            if(supersede)shader.InvalidateAssets();
            return new(candidate,layout,targets,DynamicPipelineState.Viewport);
        });
        var installed=shader.Shader;var previous=asset.Pipeline;supersede=true;
        // Act: supersede the in-flight candidate before the factory returns.
        Assert.False(shader.CompileAndLink());
        // Assert: the previously installed generation remains current and usable.
        Assert.Equal(installed,shader.Shader);Assert.Same(previous,asset.Pipeline);
        Assert.False(previous.IsDisposed);installed.RequireShader();
    }
    /// <summary>Reload cannot alter the pipeline set whose state footprint is already admitted by an active boundary.</summary>
    [Fact]
    public void ActiveBoundaryRejectsShaderReplacement()
    {
        // Arrange: capture the installed pipeline set in an active execution boundary.
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        using var asset=new GraphicsPipelineAsset(shader,new(shader.Shader,EngineFullscreenGeometry.Layout,
            new([new(TextureFormat.Rgba32f)]),DynamicPipelineState.Viewport));
        var commands=RHICommandContext.Current;using var boundary=commands.EnterBoundary(new("Fixture.ReloadAdmission",true,graphics:[asset.Pipeline]));
        // Act: attempt candidate replacement while the old pipeline set is admitted.
        boundary.Run(()=>Assert.Throws<InvalidOperationException>(()=>shader.CompileAndLink()));
        // Assert: the previously admitted pipeline still resolves to its shader.
        asset.Pipeline.RequireShader();
    }
    /// <summary>Fixed pipeline execution rejects a foreign thread before entering native graphics dispatch.</summary>
    [Fact]
    public void PipelineCannotExecuteOnForeignThread()
    {
        // Arrange: create one fixed graphics pipeline on the fixture's owning thread.
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        using var pipeline=RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader,EngineFullscreenGeometry.Layout,
            new([new(TextureFormat.Rgba32f)]),DynamicPipelineState.Viewport)).RequireResource();
        // Act: request the shader through that pipeline from a worker thread.
        Exception? caught=null;var thread=new Thread(()=>caught=Record.Exception(()=>pipeline.RequireShader()));thread.Start();thread.Join();
        // Assert: foreign access is rejected while the originating thread retains access.
        Assert.IsType<InvalidOperationException>(caught);pipeline.RequireShader();
    }
    #endregion
}
