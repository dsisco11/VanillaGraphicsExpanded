using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Runs authored relief through the production shader include with physical surface derivatives.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainReliefNumericalTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainReliefNumericalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Physical relief
    /// <summary>Neutral or unavailable material data preserves UV; valid relief follows world scale and mirroring.</summary>
    [Theory]
    [InlineData(0, .04f, 0f, 1f, 1f, 2f, 0f)]
    [InlineData(2, .04f, 0f, 1f, 1f, 2f, 0f)]
    [InlineData(1, 0f, 0f, 1f, 1f, 2f, 0f)]
    [InlineData(1, .04f, .5f, 1f, 1f, 2f, 0f)]
    [InlineData(1, .04f, 1f, 1f, 1f, 2f, 0f)]
    [InlineData(1, .04f, 0f, 1f, 0f, 2f, 0f)]
    [InlineData(1, .04f, 0f, 1f, 1f, 20f, 0f)]
    [InlineData(1, .04f, 0f, 1f, 1f, .1f, 0f)]
    [InlineData(1, .02f, 0f, 1f, 1f, 2f, 1f)]
    [InlineData(1, .04f, 0f, 1f, 1f, 2f, 1f)]
    [InlineData(1, .04f, 0f, 2f, 1f, 2f, 1f)]
    [InlineData(1, .04f, 0f, -1f, 1f, 2f, -1f)]
    public void ReliefUsesAuthoredMetric(int mode,float amplitude,float height,float scale,float index,float distance,float direction)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string fragmentPath = mode == 1 ? "tests/relief-on.fsh" : "tests/relief-off.fsh";
        int vertex=shaders.Load(ShaderType.VertexShader,"tests/complete-state.vsh");
        int fs=shaders.Load(ShaderType.FragmentShader,fragmentPath);
        using var program=GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex,fs));
        using var vao=GpuVao.Create();
        using var draw=new ShaderTestFramework();
        using var heights=draw.CreateTexture(64,64,PixelInternalFormat.Rgba32f,Enumerable.Range(0,4096).SelectMany(_=>new[]{.5f,.5f,1f,height}).ToArray());
        using var indices=draw.CreateTexture(1,1,PixelInternalFormat.R32f,[index]);
        using var records=draw.CreateTexture(2,1,PixelInternalFormat.Rgba32f,[0,0,1,1,amplitude,0,0,0]);
        using var target=draw.CreateTestGBuffer(32,32,PixelInternalFormat.Rgba32f);
        int id=program.ProgramId;
        using var inputs = new PackedUniformBuffer(16);
        byte[] inputBytes = new byte[16];
        var layout=BuiltShaderFixture.Layout(id,fragmentPath);
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, id); OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, heights, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(((GpuTextureRef)heights).Target))]); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(1, indices, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(((GpuTextureRef)indices).Target))]); VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(2, records, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(((GpuTextureRef)records).Target))]);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_normalDepthTex"),0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementTex"),1);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementRecords"),2);
        UboPacking.WriteVec2(inputBytes, 8, scale, distance);
        using var frameCamera = TestFrameCamera.CreateIdentity(32, 32);
        Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Frame, frameCamera.Capture()));
        inputs.SetBytes(inputBytes);
        Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.ShaderInputs, inputs.Capture()));
        FramebufferTestSetup.BindForEngineRendering(target); GL.Disable(EnableCap.DepthTest);GL.Disable(EnableCap.Blend);GL.Disable(EnableCap.CullFace);
        GL.DrawArrays(PrimitiveType.Triangles,0,3);
        float[] pixels=target[0].ReadPixels(); int center=(16*32+16)*4;
        if(direction==0) Assert.InRange(pixels[center],-.000001f,.000001f);
        else
        {
            float expected=amplitude*(1f+scale/64f)/(Math.Abs(scale)*distance);
            Assert.InRange(pixels[center]*direction,expected*.92f,expected*1.02f);
        }
        for(int i=0;i<pixels.Length;i+=4) { Assert.True(float.IsFinite(pixels[i]));Assert.InRange(Math.Abs(pixels[i]),0,.031251f);Assert.InRange(pixels[i+2],0,1); }
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);OpenGLVertexSelection.Bind(OpenGlStateCache.Current, 0);
    }
    #endregion
}
