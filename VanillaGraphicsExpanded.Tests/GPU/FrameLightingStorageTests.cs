using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the actual compute publication's immutable frame scale and generation identity.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FrameLightingStorageTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Physical lighting admits the frozen frame slot and simultaneous fragment consumer limits.</summary>
    [Fact]
    public void LightingCapabilityAdmissionRequiresCompleteConsumerContract()
    {
        EnsureShaderTestAvailable();
        var supported = GpuSupport.Graphics with { MaxShaderStorageBufferBindings = 5,
            MaxFragmentShaderStorageBlocks = 2, MaxUniformBlockSize = VgeFrameUniformBuffer.PackedSize };
        Assert.True(CameraExposureDispatch.SupportsLighting(supported));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxShaderStorageBufferBindings = 4 }));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxFragmentShaderStorageBlocks = 1 }));
        Assert.False(CameraExposureDispatch.SupportsLighting(supported with { MaxUniformBlockSize = VgeFrameUniformBuffer.PackedSize - 1 }));
    }

    /// <summary>Automatic exposure is sampled once; later metering cannot modify the frozen publication.</summary>
    [Fact]
    public void FrozenScaleRetainsExposureAndSourceIdentity()
    {
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var previous = ExposureTestStorage.Create(2);
        using var output = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.FrozenLighting" }).RequireResource();
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
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var previous = ExposureTestStorage.Create(float.NaN);
        using var output = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.BoundedLighting" }).RequireResource();
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
        EnsureShaderTestAvailable();
        var program = Programs.CreateCompute(FrameLightingShaderProgram.Create);
        using var output = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.RetentionBoundary" }).RequireResource();
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
        using var output=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = "Tests.HighlightContainment" }).RequireResource();
        output.Allocate(32);freeze.Output=output;
        string directory=Path.Combine(AppContext.BaseDirectory,"assets/shaders/includes");
        using var modules=new TerrainShaderTestFixture();
        int vertex=modules.Compile(ShaderType.VertexShader,
            "#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        string source="#version 330 core\n"+Expand(Path.Combine(directory,"pbr_scene_storage.glsl"))
            +Expand(Path.Combine(directory,"pbr_common.glsl"))+Expand(Path.Combine(directory,"pbr_microfacet_energy.glsl"))+"""
            uniform int highlight;
            layout(location=0)out vec4 result;
            void main(){
                vec3 radiance=vec3(.0003);
                if(int(gl_FragCoord.x)==0 && highlight!=0){
                    vec3 V=vec3(1,0,1e-7),L=vec3(-1,0,1e-7);
                    VgeLobeEnergy energy=VgeEvaluateMicrofacetEnergy(vec3(1),.04,1e-7);
                    radiance=cookTorranceBRDF(vec3(0,0,1),V,L,vec3(1),.04)
                        *energy.compensation*1e-7*1e7;
                }
                result=vec4(VgeStoreSceneLighting(radiance),1);
            }
            """;
        int program=TerrainShaderTestFixture.Link(vertex,modules.Compile(ShaderType.FragmentShader,source));
        try
        {
            using var target=TestFramework.CreateTestGBuffer(2,1,PixelInternalFormat.Rgba16f);
            float? retainedFaint=null;
            foreach(float bound in new[]{0f,float.MaxValue})
            foreach(int highlight in new[]{0,1})
            {
                freeze.Capture(bound,.001f,false,42,7);
                Assert.True(CameraExposureDispatch.TryFreeze(freeze));
                Assert.Equal(.001f,ExposureTestStorage.Read<float>(output)[0]);
                output.BindBase(GpuBindingRegistry.Ssbo.FrameLighting);
                Helpers.MicrofacetTextureReference.Bind();
                GL.UseProgram(program);GL.Uniform1(GL.GetUniformLocation(program,"highlight"),highlight);
                TestFramework.RenderQuadTo(program,target);
                float[] pixels=target[0].ReadPixels();
                Assert.All(pixels,value=>Assert.True(float.IsFinite(value)));
                Assert.True(pixels[4]>0);
                retainedFaint??=pixels[4];
                Assert.Equal(retainedFaint.Value,pixels[4]);
                if(highlight!=0)Assert.Equal(65504,pixels[0]);
            }
        }
        finally { GL.DeleteProgram(program);StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Expands the actual production storage and BRDF helpers without replacing their arithmetic.</summary>
    private static string Expand(string path)=>Regex.Replace(File.ReadAllText(path),"@import \\\"([^\\\"]+)\\\"",
        match=>Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,match.Groups[1].Value))));
    #endregion
}
