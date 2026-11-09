using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.LumOn;
using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Xunit;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Checks native shader binding for displaced-surface velocity history.</summary>
/// <remarks>Renders the production velocity shader and verifies the declared patch-identity sampler is assigned to the expected native unit.</remarks>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class LumOnVelocityFunctionalTests : LumOnShaderFunctionalTestBase
{
    #region Public API
    /// <summary>Uses the shared GPU context to verify native velocity-state interactions.</summary>
    public LumOnVelocityFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Displaced terrain rejects camera-only motion history while retaining its surface generation.</summary>
    [Fact]
    public void DisplacedSurfaceRejectsVelocityHistory()
    {
        // Arrange: prepare a displaced patch identity and depth input for the production shader.
        EnsureShaderTestAvailable();
        var program=CompileVelocityShader();
        using var identity=TestFramework.CreateTexture(ScreenWidth,ScreenHeight,TextureFormat.Rgba32ui);
        var ids=new uint[ScreenWidth*ScreenHeight*4];for(int i=3;i<ids.Length;i+=4)ids[i]=(1u<<16)|27u;
        identity.UploadDataImmediate(ids);
        using var depth=TestFramework.CreateTexture(ScreenWidth,ScreenHeight,TextureFormat.R32f,CreateUniformDepthData(ScreenWidth,ScreenHeight,.5f));
        using var target=TestFramework.CreateTestGBuffer(ScreenWidth,ScreenHeight,TextureFormat.Rgba32f);
        var matrix=LumOnTestInputFactory.CreateIdentityMatrix();
        UpdateAndBindLumOnFrameUbo(program, invCurrViewProjMatrix: matrix, prevViewProjMatrix: matrix, historyValid: 1);
        program.PrimaryDepth=depth;program.PatchIdentity=identity;
        // Act: compare the declared sampler unit with the uniform assigned in the live GL program.
        Assert.True(program.ProgramLayout.TryGetContractSamplerSpec("gBufferPatchId",out int contractUnit,out bool required));Assert.Equal(1,contractUnit);
        int identityLocation=program.ProgramLayout.GetUniformLocation(program.ProgramId,"gBufferPatchId");
        GL.GetUniform(program.ProgramId,identityLocation,out int identityUnit);Assert.Equal(1,identityUnit);
        TestFramework.RenderQuadTo(program,target);
        // Assert: the shader output still rejects this displaced surface history.
        ImageAssertions.Predicates.Predicate(target[0], SurfacePredicates.LumOnVelocityFunctionalTests_DisplacedSurfaceRejectsVelocityHistory).AssertEqual();
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }

    #endregion

    #region Private
    /// <summary>Compiles or retrieves the velocity shader used by this test.</summary>
    private LumOnVelocityShaderProgram CompileVelocityShader() => Programs.Create<LumOnVelocityShaderProgram>();

    #endregion

}
