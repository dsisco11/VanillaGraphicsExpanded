using VanillaGraphicsExpanded.Rendering;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes production displacement functions with controlled atlas samples and projection inputs.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class TerrainDisplacementNumericalTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainDisplacementNumericalTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Bounded shader functions
    /// <summary>Height mapping is signed and bounded; neutral, boundary and invalid samples remain undisplaced.</summary>
    [Theory]
    [InlineData(.5f,.25f,.5f,.04f,0f)]
    [InlineData(1f,.25f,.5f,.04f,.04f)]
    [InlineData(0f,.25f,.5f,.04f,-.04f)]
    [InlineData(1f,0f,.5f,.04f,0f)]
    [InlineData(1f,.5f,.5f,.04f,0f)]
    [InlineData(1f,.25f,.5f,.5f,.05f)]
    [InlineData(float.NaN,.25f,.5f,.04f,0f)]
    [InlineData(float.PositiveInfinity,.25f,.5f,.04f,0f)]
    [InlineData(1f,.49f,.5f,.04f,0f)]
    [InlineData(1f,.4f,.5f,.04f,.00864f)]
    [InlineData(1f,float.NaN,.5f,.04f,0f)]
    [InlineData(1f,.25f,.5f,float.NaN,0f)]
    public void HeightAndSharedEdgeRespectBounds(float height,float u,float v,float amplitude,float expected)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex=shaders.Compile(ShaderType.VertexShader,"""
            #version 430 core
            void main(){ vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3)); gl_Position=vec4(p[gl_VertexID],0,1); }
            """);
        int fragment=shaders.Compile(ShaderType.FragmentShader,"#version 430 core\n"+TerrainTessellationTestAssets.Common()+"""

            uniform vec3 sampleInput;
            layout(location=0) out vec4 result;
            void main(){
                float h=VgeHeight(sampleInput.xy,vec2(0),vec2(.5,1),sampleInput.z,vec3(0));
                result=vec4(h,VgeEdgeLevel(vec3(-.5,0,0),vec3(.5,0,0)),VgeEdgeLevel(vec3(.5,0,0),vec3(-.5,0,0)),VgeEdgeLevel(vec3(0),vec3(0)));
            }
            """);
        int program=TerrainShaderTestFixture.Link(vertex,fragment);
        using var vertexArray = GpuVao.Create();
        int vao = vertexArray.VertexArrayId;
        try
        {
            using var framework=new ShaderTestFramework();
            var data=new float[16*16*4];
            for(int y=0;y<16;y++) for(int x=0;x<16;x++) data[(y*16+x)*4+3]=x<8?height:1-height;
            using var atlas=framework.CreateTexture(16,16,PixelInternalFormat.Rgba32f,data);
            using var target=framework.CreateTestGBuffer(1,1,PixelInternalFormat.Rgba32f);
            var layout = GpuProgramLayout.TryBuild(program);
            target.BindWithViewport(); GlStateCache.Current.UseProgram(program); GlStateCache.Current.BindVertexArray(vao);
            atlas.Bind(0); ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_normalDepthTex"),0);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"sampleInput"),u,v,amplitude);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_tessellationDistance"),10f,20f);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_tessellationPixels"),128f,128f,16f,8f);
            float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
            ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(program,"modelViewMatrix"), identity);
            ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(program,"projectionMatrix"), identity);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.DrawArrays(PrimitiveType.Triangles,0,3);
            var actual=target[0].ReadPixels();
            Assert.InRange(actual[0],expected-.00001f,expected+.00001f);
            Assert.Equal(4f,actual[1]); Assert.Equal(actual[1],actual[2]); Assert.Equal(1f,actual[3]);
        }
        finally { GlStateCache.Current.UseProgram(0); GlStateCache.Current.BindVertexArray(0);  GpuProgramObject.Adopt(program).Dispose();   }
    }
    #endregion
}
