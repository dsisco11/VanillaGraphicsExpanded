using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks coherent graphics asset publication while RHI references retain exact executable and pipeline generations.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class RHIProgramGenerationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A failed renderer candidate leaves both installed generations usable; success publishes a compatible pair.</summary>
    [Fact]
    public void FailedPipelineCandidatePreservesInstalledSet()
    {
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        var commands=RHICommandContext.Current;var layout=EngineFullscreenGeometry.Layout;
        var targets=new RenderTargetSignature([new(TextureFormat.Rgba32f)]);
        bool fail=false;
        using var asset=new GraphicsPipelineAsset(shader,new(shader.Shader,layout,targets,DynamicPipelineState.Viewport),
            candidate=>fail?throw new ArgumentException("Controlled renderer contract failure."):new(candidate,layout,targets,DynamicPipelineState.Viewport));
        var installed=shader.Shader;var previous=asset.Pipeline;var captured=shader.CaptureBindings();
        fail=true;Assert.False(shader.CompileAndLink());Assert.Equal(installed,shader.Shader);Assert.Same(previous,asset.Pipeline);
        Assert.False(previous.IsDisposed);Assert.Same(installed.RequireShader(),previous.RequireShader());Assert.Equal(installed,captured.Shader);
        fail=false;Assert.True(shader.CompileAndLink());Assert.NotEqual(installed,shader.Shader);
        Assert.NotSame(previous,asset.Pipeline);Assert.Equal(shader.Shader,asset.Description.Shader);Assert.True(previous.IsDisposed);
        Assert.Throws<ObjectDisposedException>(()=>installed.RequireShader());
    }
    /// <summary>Changing asset admission during candidate construction cannot publish an older completed selection.</summary>
    [Fact]
    public void SupersededCandidateCannotInstall()
    {
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        var layout=EngineFullscreenGeometry.Layout;var targets=new RenderTargetSignature([new(TextureFormat.Rgba32f)]);
        bool supersede=false;
        using var asset=new GraphicsPipelineAsset(shader,new(shader.Shader,layout,targets,DynamicPipelineState.Viewport),candidate=>
        {
            if(supersede)shader.InvalidateAssets();
            return new(candidate,layout,targets,DynamicPipelineState.Viewport);
        });
        var installed=shader.Shader;var previous=asset.Pipeline;supersede=true;
        Assert.False(shader.CompileAndLink());Assert.Equal(installed,shader.Shader);Assert.Same(previous,asset.Pipeline);
        Assert.False(previous.IsDisposed);installed.RequireShader();
    }
    /// <summary>Reload cannot alter the pipeline set whose state footprint is already admitted by an active boundary.</summary>
    [Fact]
    public void ActiveBoundaryRejectsShaderReplacement()
    {
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        using var asset=new GraphicsPipelineAsset(shader,new(shader.Shader,EngineFullscreenGeometry.Layout,
            new([new(TextureFormat.Rgba32f)]),DynamicPipelineState.Viewport));
        var commands=RHICommandContext.Current;using var boundary=commands.EnterBoundary(new("Fixture.ReloadAdmission",true,graphics:[asset.Pipeline]));
        boundary.Run(()=>Assert.Throws<InvalidOperationException>(()=>shader.CompileAndLink()));
        asset.Pipeline.RequireShader();
    }
    /// <summary>Fixed pipeline execution rejects a foreign thread before entering native graphics dispatch.</summary>
    [Fact]
    public void PipelineCannotExecuteOnForeignThread()
    {
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<DebugTextureShaderProgram>();
        using var pipeline=RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader,EngineFullscreenGeometry.Layout,
            new([new(TextureFormat.Rgba32f)]),DynamicPipelineState.Viewport)).RequireResource();
        Exception? caught=null;var thread=new Thread(()=>caught=Record.Exception(()=>pipeline.RequireShader()));thread.Start();thread.Join();
        Assert.IsType<InvalidOperationException>(caught);pipeline.RequireShader();
    }
    #endregion
}
