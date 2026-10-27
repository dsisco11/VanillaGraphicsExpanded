using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks compute asset preparation independently of command selection and physical resource publication.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class ComputeShaderAssetTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Deferred preparation installs a complete shader/pipeline set without selecting its native executable.</summary>
    [Fact]
    public void PreparationPublishesOnlyReadyFixedReferences()
    {
        fixture.MakeCurrent();using var assets=new BinaryShaderApiFixture();
        var settings=new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var asset=ComputeShaderAsset.DeclareFromAssets(assets.Api,settings);
        Assert.True(asset.Shader.IsEmpty);Assert.True(asset.Pipeline.IsEmpty);
        OpenGLProgramSelection.Select(StateCache.Current,0);Assert.True(asset.EnsureReady());
        var shader=asset.Shader;var pipeline=asset.Pipeline;Assert.Equal(shader,pipeline.RequirePipeline().Description.Shader);
        Assert.True(asset.EnsureReady());Assert.Equal(shader,asset.Shader);Assert.Equal(pipeline,asset.Pipeline);
        Assert.Equal(0,GL.GetInteger(GetPName.CurrentProgram));asset.Dispose();
        Assert.Throws<ObjectDisposedException>(()=>shader.RequireShader());Assert.Throws<ObjectDisposedException>(()=>pipeline.RequirePipeline());
    }
    /// <summary>A failed independent preparation cannot replace or select a previously installed executable.</summary>
    [Fact]
    public void FailedPreparationPreservesUnrelatedSelection()
    {
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var selected=programs.Create<DebugTextureShaderProgram>();
        using var assets=new BinaryShaderApiFixture();
        var settings=new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        assets.BeforeRead=path=>{if(path.EndsWith(".spv",StringComparison.Ordinal))assets.Overrides[path]=new byte[20];};
        using var failed=ComputeShaderAsset.DeclareFromAssets(assets.Api,settings);
        OpenGLProgramSelection.Select(StateCache.Current,selected.ProgramId);
        try
        {
            bool ready=false;var error=Record.Exception(()=>ready=failed.EnsureReady());Assert.True(error is not null||!ready);
            Assert.True(failed.Shader.IsEmpty);Assert.True(failed.Pipeline.IsEmpty);Assert.Equal(selected.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            selected.Shader.RequireShader();
        }
        finally{OpenGLProgramSelection.Select(StateCache.Current,0);}
    }
    #endregion
}
