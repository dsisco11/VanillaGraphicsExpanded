using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks compute asset preparation independently of command selection and physical resource publication.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ComputeShaderAssetTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Deferred preparation installs a complete shader/pipeline set without selecting its native executable.</summary>
    [Fact]
    public void PreparationPublishesOnlyReadyFixedReferences()
    {
        // Arrange: Declare an asset without reading its packaged binaries or selecting a native executable.
        fixture.MakeCurrent();using var assets=new BinaryShaderApiFixture();
        var settings=new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        using var asset=ComputeShaderAsset.DeclareFromAssets(assets.Api,settings);
        Assert.True(asset.Shader.IsEmpty);Assert.True(asset.Pipeline.IsEmpty);
        // Act: Prepare twice to qualify both initial publication and stable ready-state reuse.
        OpenGLProgramSelection.Select(OpenGlStateCache.Current,0);Assert.True(asset.EnsureReady());
        // Assert: Preparation publishes matching fixed shader/pipeline references without changing selection.
        var shader=asset.Shader;var pipeline=asset.Pipeline;Assert.Equal(shader,pipeline.RequirePipeline().Description.Shader);
        Assert.True(asset.EnsureReady());Assert.Equal(shader,asset.Shader);Assert.Equal(pipeline,asset.Pipeline);
        Assert.Equal(0,GL.GetInteger(GetPName.CurrentProgram));asset.Dispose();
        Assert.Throws<ObjectDisposedException>(()=>shader.RequireShader());
#if DEBUG
        // Retained pipeline misuse is a development diagnostic under the owner lifetime contract.
        Assert.Throws<ObjectDisposedException>(()=>pipeline.RequirePipeline());
#endif
    }
    /// <summary>A failed independent preparation cannot replace or select a previously installed executable.</summary>
    [Fact]
    public void FailedPreparationPreservesUnrelatedSelection()
    {
        // Arrange: Keep a valid executable selected while corrupting the candidate's packaged SPIR-V bytes.
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var selected=programs.Create<DebugTextureShaderProgram>();
        using var assets=new BinaryShaderApiFixture();
        var settings=new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1"));
        assets.BeforeRead=path=>{if(path.EndsWith(".spv",StringComparison.Ordinal))assets.Overrides[path]=new byte[20];};
        using var failed=ComputeShaderAsset.DeclareFromAssets(assets.Api,settings);
        OpenGLProgramSelection.Select(OpenGlStateCache.Current,selected.ProgramId);
        try
        {
            // Act: Attempt candidate preparation while the unrelated executable remains selected.
            bool ready=false;var error=Record.Exception(()=>ready=failed.EnsureReady());Assert.True(error is not null||!ready);
            // Assert: Failure publishes no candidate references and does not replace the caller's selection.
            Assert.True(failed.Shader.IsEmpty);Assert.True(failed.Pipeline.IsEmpty);Assert.Equal(selected.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            selected.Shader.RequireShader();
        }
        finally{OpenGLProgramSelection.Select(OpenGlStateCache.Current,0);}
    }
    #endregion
}
