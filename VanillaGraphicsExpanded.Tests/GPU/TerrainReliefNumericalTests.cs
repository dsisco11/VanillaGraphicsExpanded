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
        string includes = Path.Combine(AppContext.BaseDirectory,"assets","shaders","includes");
        string fragment = "#version 430\n#define VGE_PBR_ENABLE_POM " + (mode == 1 ? "1\n" : "0\n")
            + "uniform sampler2D vge_normalDepthTex; uniform vec2 metric; out vec4 result;\n"
            + TerrainEyeRelativeShadingTests.Expand(Path.Combine(includes,"vge_normaldepth.glsl"))
            + TerrainEyeRelativeShadingTests.Expand(Path.Combine(includes,"vge_parallax.glsl")) + """

            void main() {
                vec2 uv=gl_FragCoord.xy/32.0;
                vec3 position=vec3((uv.x-.5)*metric.x+1.0,(uv.y-.5)*abs(metric.x),-metric.y);
                vec2 shifted=VgeApplyPomUv_WithTbn(uv,mat3(1),1,position,vec2(0),vec2(1));
                result=vec4(shifted-uv,shifted);
            }
            """;
        int vertex=shaders.Compile(ShaderType.VertexShader,"#version 430\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2-1,0,1);}");
        int fs=shaders.Compile(ShaderType.FragmentShader,fragment);
        using var program=GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vertex,fs));
        using var vao=GpuVao.Create();
        using var draw=new ShaderTestFramework();
        using var heights=draw.CreateTexture(64,64,PixelInternalFormat.Rgba32f,Enumerable.Range(0,4096).SelectMany(_=>new[]{.5f,.5f,1f,height}).ToArray());
        using var indices=draw.CreateTexture(1,1,PixelInternalFormat.R32f,[index]);
        using var records=draw.CreateTexture(2,1,PixelInternalFormat.Rgba32f,[0,0,1,1,amplitude,0,0,0]);
        using var target=draw.CreateTestGBuffer(32,32,PixelInternalFormat.Rgba32f);
        int id=program.ProgramId;
        var layout=GpuProgramLayout.TryBuild(id);
        StateCache.Current.UseProgram(id); StateCache.Current.BindVertexArray(vao.VertexArrayId);
        heights.Bind(0); indices.Bind(1); records.Bind(2);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_normalDepthTex"),0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementTex"),1);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementRecords"),2);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"metric"),scale,distance);
        ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(id,"modelViewMatrix"),[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]);
        target.BindWithViewport(); GL.Disable(EnableCap.DepthTest);GL.Disable(EnableCap.Blend);GL.Disable(EnableCap.CullFace);
        GL.DrawArrays(PrimitiveType.Triangles,0,3);
        float[] pixels=target[0].ReadPixels(); int center=(16*32+16)*4;
        if(direction==0) Assert.InRange(pixels[center],-.000001f,.000001f);
        else
        {
            float expected=amplitude*(1f+scale/64f)/(Math.Abs(scale)*distance);
            Assert.InRange(pixels[center]*direction,expected*.92f,expected*1.02f);
        }
        for(int i=0;i<pixels.Length;i+=4) { Assert.True(float.IsFinite(pixels[i]));Assert.InRange(Math.Abs(pixels[i]),0,.031251f);Assert.InRange(pixels[i+2],0,1); }
        StateCache.Current.UseProgram(0);StateCache.Current.BindVertexArray(0);
    }
    #endregion
}
