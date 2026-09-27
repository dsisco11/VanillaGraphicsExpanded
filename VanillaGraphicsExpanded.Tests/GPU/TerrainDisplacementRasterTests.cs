using VanillaGraphicsExpanded.Rendering;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Runs adaptive production TCS/TES against controlled terrain receivers.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class TerrainDisplacementRasterTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainDisplacementRasterTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Displaced receiver
    /// <summary>Actual tessellation respects neutral height, rotated UVs, degenerate tangents and excluded wind faces.</summary>
    [Theory]
    [InlineData(.5f,0,0f)]
    [InlineData(1f,0,.04f)]
    [InlineData(0f,0,-.04f)]
    [InlineData(1f,1,.04f)]
    [InlineData(1f,2,0f)]
    [InlineData(1f,3,0f)]
    public void AdaptiveStagesPublishBoundedPositionAndNormal(float height,int mode,float expected)
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string source="""
            #version 430 core
            out vec4 worldPos;
            out vec4 camPos;
            out vec3 normal;
            out vec2 uv;
            flat out vec2 vge_uvBase;
            flat out vec2 vge_uvExtent;
            flat out int vge_faceId;
            flat out int renderFlags;
            void main(){
                vec2 p[6]=vec2[6](vec2(-1,-1),vec2(1,-1),vec2(-1,1),vec2(-1,1),vec2(1,-1),vec2(1,1));
                worldPos=vec4(p[gl_VertexID],0,1); camPos=worldPos; gl_Position=worldPos;
                normal=vec3(0,0,1); uv=p[gl_VertexID]*.5+.5;
                vge_uvBase=vec2(0); vge_uvExtent=vec2(1); vge_faceId=0; renderFlags=0;
            }
            """;
        if(mode==1) source=source.Replace("vge_uvBase=", "uv=vec2(1-uv.y,uv.x); vge_uvBase=");
        if(mode==2) source=source.Replace("vge_uvBase=", "uv=vec2(.5); vge_uvBase=");
        if(mode==3) source=source.Replace("renderFlags=0", "renderFlags=1<<25");
        int vertex=shaders.Compile(ShaderType.VertexShader,source);
        int fragment=shaders.Compile(ShaderType.FragmentShader,"""
            #version 430 core
            in vec4 worldPos; in vec3 normal;
            layout(location=0) out vec4 result;
            void main(){result=vec4(worldPos.z,normal);}
            """);
        using var vertexArray = GpuVao.Create();
        int vao = vertexArray.VertexArrayId, program = 0;
        int oldPatch=GlStateCache.Current.PatchVertices;
        try
        {
            Assert.True(TerrainTessellationLinker.TryCreate(vertex,fragment,TerrainTessellationTestAssets.Generate(source,true),TerrainTessellationPatches.EnabledDefine,8,out program,out string error),error);
            using var framework=new ShaderTestFramework();
            using var amplitude=framework.CreateTexture(16,16,PixelInternalFormat.R32f,Enumerable.Repeat(.04f,256).ToArray());
            var pixels=new float[256*4]; for(int i=0;i<256;i++) pixels[i*4+3]=height;
            using var atlas=framework.CreateTexture(16,16,PixelInternalFormat.Rgba32f,pixels);
            using var target=framework.CreateTestGBuffer(16,16,PixelInternalFormat.Rgba32f);
            var layout = GpuProgramLayout.TryBuild(program);
            target.BindWithViewport(); GlStateCache.Current.UseProgram(program); GlStateCache.Current.BindVertexArray(vao);
            amplitude.Bind(0); atlas.Bind(1);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_displacementTex"),0);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_normalDepthTex"),1);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_tessellationDistance"),10f,20f);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_tessellationPixels"),128f,128f,16f,8f);
            float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
            ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(program,"modelViewMatrix"), identity);
            ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(program,"projectionMatrix"), identity);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GlStateCache.Current.SetPatchVertices(3); GL.DrawArrays(PrimitiveType.Patches,0,6);
            float[] actual=target[0].ReadPixels(); int center=(8*16+8)*4;
            Assert.InRange(actual[center],expected-.0001f,expected+.0001f);
            for(int i=0;i<actual.Length;i+=4)
            {
                Assert.InRange(actual[i],-.04001f,.04001f);
                Assert.True(float.IsFinite(actual[i+1])&&float.IsFinite(actual[i+2])&&float.IsFinite(actual[i+3]));
                Assert.InRange(actual[i+3], .5f, 1.001f); // Every pixel, including the shared diagonal, must remain covered.
            }
            Assert.InRange(actual[center+3],.999f,1.001f);
        }
        finally { GlStateCache.Current.UseProgram(0); GlStateCache.Current.BindVertexArray(0); GlStateCache.Current.SetPatchVertices(oldPatch);  if(program!=0)GpuProgramObject.Adopt(program).Dispose();   }
    }
    #endregion
}
