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
    [InlineData(1f,4,.04f)]
    [InlineData(1f,5,0f)]
    [InlineData(1f,6,0f)]
    [InlineData(1f,7,0f)]
    [InlineData(1f,8,0f)]
    [InlineData(1f,9,0f)]
    [InlineData(1f,10,0f)]
    [InlineData(1f,11,0f)]
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
        if(mode==4) source=source.Replace("vge_uvBase=vec2(0); vge_uvExtent=vec2(1)", "vge_uvBase=vec2(-1); vge_uvExtent=vec2(0)");
        if(mode==5) source=source.Replace("uv=p[gl_VertexID]*.5+.5", "uv=p[gl_VertexID]+.5");
        if(mode==9) source=source.Replace("uv=p[gl_VertexID]*.5+.5", "uv=p[gl_VertexID]*.25+.5");
        if(mode>=10) source=source.Replace("p[gl_VertexID]", "p[gl_VertexID%6]")
            .Replace("camPos=worldPos", "worldPos.x=worldPos.x*.5+(gl_VertexID<6 ? -.5 : .5); camPos=worldPos")
            .Replace("vge_uvBase=", "uv.x=uv.x*.5+(gl_VertexID<6 ? 0.0 : .5); vge_uvBase=");
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
            using var amplitude=framework.CreateTexture(16,16,PixelInternalFormat.R32f,Enumerable.Range(0,256).Select(i => mode>=10 ? (i%16<8 ? 1f : 2f) : mode==6 ? 0f : mode==8 ? 2f : 1f).ToArray());
            using var records=framework.CreateTexture(mode>=10 ? 4 : 2,1,PixelInternalFormat.Rgba32f,mode>=10 ? new float[] {0,0,.5f,1,.04f,0,0,0, .5f,0,.5f,1,.02f,0,0,0} : new float[] {0,0,mode==7 ? .5f : 1f,1,.04f,0,0,0});
            var pixels=new float[256*4]; for(int i=0;i<256;i++) pixels[i*4+3]=mode>=10 && i%16>=8 ? 0 : height;
            using var atlas=framework.CreateTexture(16,16,PixelInternalFormat.Rgba32f,pixels);
            using var target=framework.CreateTestGBuffer(16,16,PixelInternalFormat.Rgba32f);
            var layout = GpuProgramLayout.TryBuild(program);
            target.BindWithViewport(); GlStateCache.Current.UseProgram(program); GlStateCache.Current.BindVertexArray(vao);
            amplitude.Bind(0); atlas.Bind(1); records.Bind(2);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_displacementTex"),0);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_normalDepthTex"),1);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_tessellationDistance"),10f,20f);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_tessellationPixels"),128f,128f,16f,8f);
            ShaderTestFramework.SetUniform(layout.GetUniformLocation(program,"vge_displacementRecords"),2);
            float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
            ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(program,"modelViewMatrix"), identity);
            ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(program,"projectionMatrix"), identity);
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GlStateCache.Current.SetPatchVertices(3);
            if(mode==11) {
                // Separate submissions represent a pool/chunk boundary sharing the same eye-relative edge.
                GL.DrawArrays(PrimitiveType.Patches,0,6);
                GL.DrawArrays(PrimitiveType.Patches,6,6);
            }
            else GL.DrawArrays(PrimitiveType.Patches,0,mode>=10 ? 12 : 6);
            float[] actual=target[0].ReadPixels(); int center=(8*16+8)*4;
            if(mode<10) Assert.InRange(actual[center],expected-.0001f,expected+.0001f);
            else {
                // Both opted-in tiles must really displace, in opposite directions; checking
                // coverage alone could pass if eligibility silently disabled both surfaces.
                Assert.True(actual[(8*16+3)*4] > .005f);
                Assert.True(actual[(8*16+12)*4] < -.005f);
            }
            for(int i=0;i<actual.Length;i+=4)
            {
                Assert.InRange(actual[i],-.04001f,.04001f);
                Assert.True(float.IsFinite(actual[i+1])&&float.IsFinite(actual[i+2])&&float.IsFinite(actual[i+3]));
                Assert.InRange(actual[i+3], .5f, 1.001f); // Every pixel, including the shared diagonal, must remain covered.
            }
            if(mode<10) Assert.InRange(actual[center+3],.999f,1.001f);
        }
        finally { GlStateCache.Current.UseProgram(0); GlStateCache.Current.BindVertexArray(0); GlStateCache.Current.SetPatchVertices(oldPatch);  if(program!=0)GpuProgramObject.Adopt(program).Dispose();   }
    }
    #endregion
}
