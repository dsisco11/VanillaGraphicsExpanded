using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Lighting;

/// <summary>Checks the actual compute publication's immutable frame scale and generation identity.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class FrameLightingStorageTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API


    /// <summary>Automatic exposure is sampled once; later metering cannot modify the frozen publication.</summary>
    [Fact]
    public void FrozenScaleRetainsExposureAndSourceIdentity()
    {
        // Arrange: Seed exposure and identity fields, then freeze a lighting publication into an owned output buffer.
        // Act and assert: Change source exposure and frame identity and verify each freeze updates only its publication while prior bytes stay stable.
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var previous = ExposureTestStorage.Create(2);
        using var output = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.FrozenLighting" }).RequireResource();
        output.Allocate(32);
        program.PreviousExposure = previous; program.Output = output;
        program.Capture(0, .5f, true, 42, 7);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        float[] first = ExposureTestStorage.Read<float>(output);
        uint[] identity = ExposureTestStorage.Read<uint>(output);
        Assert.Equal(4, first[0]); Assert.Equal(.25f, first[1]); Assert.Equal(1, first[3]);
        Assert.Equal(42u, identity[4]); Assert.Equal(7u, identity[5]);
        previous.UploadData(new[] { -4f });
        Assert.Equal(first, ExposureTestStorage.Read<float>(output));
        program.Capture(0, .5f, true, 43, 7);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(.0625f, ExposureTestStorage.Read<float>(output)[0]);
        foreach(float invalidHistory in new[]{127f,128f,float.NaN})
        {
            previous.UploadData(new[]{invalidHistory});
            program.Capture(0,.5f,true,44,7);
            Assert.True(CameraExposureDispatch.TryFreeze(program));
            Assert.Equal(.5f,ExposureTestStorage.Read<float>(output)[0]);
        }
        program.Capture(0, 2, false, 44, 7);
        program.PreviousExposure = null;
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(2, ExposureTestStorage.Read<float>(output)[0]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Invalid automatic history uses its seed; large finite metadata cannot darken the camera-driven storage scale.</summary>
    [Fact]
    public void BoundMetadataAndResetSeedDoNotSuppressFaintLighting()
    {
        // Arrange: Use invalid exposure history, maximum finite source-bound metadata, and a later faint-light reset seed.
        // Act and assert: Freeze each publication and verify fallback gain and faint source scale are preserved under bounds.
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var previous = ExposureTestStorage.Create(float.NaN);
        using var output = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.BoundedLighting" }).RequireResource();
        output.Allocate(32);
        program.PreviousExposure = previous; program.Output = output;
        float bound = LightingStorage.UpperBound(945821327.847);
        program.Capture(bound, 1, true, 1, 1);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        float[] values = ExposureTestStorage.Read<float>(output);
        Assert.Equal(1, values[3]);
        Assert.Equal(1,values[0]);
        Assert.Equal(bound, values[2]);
        program.Capture(0, .5f, true, 2, 1);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(.5f, ExposureTestStorage.Read<float>(output)[0]);
        float[] retained = ExposureTestStorage.Read<float>(output);
        program.Capture(float.MaxValue,1,false,3,1);
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(1,ExposureTestStorage.Read<float>(output)[0]);
        retained=ExposureTestStorage.Read<float>(output);
        foreach(float invalid in new[]{-1f,float.NaN,float.PositiveInfinity})
            Assert.Throws<ArgumentOutOfRangeException>(() => program.Capture(invalid,1,false,4,1));
        Assert.Throws<ArgumentOutOfRangeException>(() => program.Capture(0, 1, false, 3, 0));
        Assert.True(CameraExposureDispatch.TryFreeze(program));
        Assert.Equal(retained, ExposureTestStorage.Read<float>(output));
    }

    /// <summary>Camera scales retain their actual low values and floor independently of the finite source-envelope magnitude.</summary>
    [Fact]
    public void CameraScaleFloorIsIndependentOfSourceEnvelope()
    {
        // Arrange: Sweep tiny, ordinary, and unit camera scales against zero, large, and maximum finite source envelopes.
        // Act and assert: Freeze each pair and verify only the minimum camera scale floor controls published storage scale.
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var output = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.RetentionBoundary" }).RequireResource();
        output.Allocate(32); program.Output = output;
        foreach(float camera in new[]{float.Epsilon,.001f,1f})
        foreach (float bound in new[] { 0f,945821327f,float.MaxValue })
        {
            program.Capture(bound,camera,false,1,1);
            Assert.True(CameraExposureDispatch.TryFreeze(program));
            float[] values = ExposureTestStorage.Read<float>(output);
            Assert.Equal(1, values[3]);
            Assert.Equal(Math.Max(camera,LightingStorage.MinimumFrameScale),values[0]);
            Assert.Equal(bound,values[2]);
        }
    }

    /// <summary>A clipped smooth grazing highlight cannot force another pixel's faint radiance below its camera-selected storage scale.</summary>
    [Fact]
    public void GrazingHighlightIsContainedWithoutSuppressingSeparateFaintPixel()
    {
        EnsureShaderTestAvailable();
        var freeze=Programs.CreateCompute(FrameLightingShaderProgram.Create);
        var probe=Programs.Create<FrameLightingGrazingProbeProgram>();
        using var output=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = "Tests.HighlightContainment" }).RequireResource();
        output.Allocate(32);freeze.Output=output;
        using var target=TestFramework.CreateTestGBuffer(2,1,TextureFormat.Rgba16f);
        float? retainedFaint=null;
        foreach(float bound in new[]{0f,float.MaxValue})
        foreach(int highlight in new[]{0,1})
        {
            // Arrange: preserve each source-envelope, frame identity, and highlight input from the original matrix.
            freeze.Capture(bound,.001f,false,42,7);
            Assert.True(CameraExposureDispatch.TryFreeze(freeze));
            Assert.Equal(.001f,ExposureTestStorage.Read<float>(output)[0]);
            probe.Capture(highlight,output,MicrofacetEnergyLookup.Prepare(),GpuSamplers.LinearClamp);

            // Act: the typed owner publishes the real frame storage and lookup through an RHI graphics pass.
            TestFramework.RenderQuadTo(probe,target);

            // Assert: contain the grazing highlight and retain the independent faint pixel across both bounds.
            float[] pixels=target[0].ReadPixels();
            Assert.All(pixels,value=>Assert.True(float.IsFinite(value)));
            Assert.True(pixels[4]>0);
            retainedFaint??=pixels[4];
            Assert.Equal(retainedFaint.Value,pixels[4]);
            if(highlight!=0)Assert.Equal(65504,pixels[0]);
        }
    }
    #endregion

}
