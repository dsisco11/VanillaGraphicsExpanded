using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns matched terrain detail programs and controlled synthetic receiver resources.</summary>
internal sealed class TerrainDetailWorkload : IDisposable
{
    private const string VertexSource = """
        #version 430 core
        out vec4 worldPos;
        out vec4 camPos;
        out vec3 normal;
        out vec2 uv;
        flat out vec2 vge_uvBase;
        flat out vec2 vge_uvExtent;
        flat out int renderFlags;
        out float vge_surfaceDisplaced;
        void main() {
            vec2 corners[6]=vec2[6](vec2(-1,-1),vec2(1,-1),vec2(-1,1),vec2(-1,1),vec2(1,-1),vec2(1,1));
            vec2 p=corners[gl_VertexID];
            worldPos=vec4(p,0,1); camPos=worldPos; gl_Position=worldPos;
            normal=vec3(0,0,1); uv=p*.5+.5;
            vge_uvBase=vec2(0);vge_uvExtent=vec2(1);renderFlags=0;vge_surfaceDisplaced=0;
        }
        """;
    private const string FragmentSource = """
        #version 430 core
        in vec4 worldPos;
        in float vge_surfaceDisplaced;
        out vec4 result;
        void main(){result=vec4(worldPos.z,vge_surfaceDisplaced,0,1);}
        """;


    private readonly TerrainShaderTestFixture shaders=new();
    private readonly ShaderTestFramework framework=new();
    private readonly Dictionary<string,GpuProgramObject> programs=new();
    private readonly GpuVao vao=GpuVao.Create();
    private readonly VgeFrameUniformBuffer camera;
    private readonly Action bindTarget;
    private readonly Func<float[]> readTarget;
    private readonly List<IDisposable> resources=new();
    private readonly Action<string> bindHeight;
    private PrimitiveType topology;
    private readonly int oldPatch=StateCache.Current.PatchVertices;

    #region Resource ownership and draws
    /// <summary>Creates static stage variants and identical render inputs before warmup begins.</summary>
    internal TerrainDetailWorkload(bool depthBias = false, bool observeClipDelta = false)
    {
        camera = TestFrameCamera.CreateIdentity(256,256);
        resources.Add(camera);
        string source=depthBias ? VertexSource.Replace("vec4(p,0,1)","vec4(p,p.y*.4,1)")
            .Replace("renderFlags=0;", "renderFlags=2<<8;gl_Position.w+=2*.00025/((gl_Position.z+3)*.05);") : VertexSource;
        string fragment=depthBias ? FragmentSource.Replace("worldPos.z,vge_surfaceDisplaced","gl_FragCoord.z,vge_surfaceDisplaced") : FragmentSource;
        if (observeClipDelta)
        {
            // Observe the stage result before fixed-point triangle rasterization can
            // introduce interpolation differences between subdivision levels.
            source = source.Replace("out vec4 worldPos;", "out vec4 worldPos; out vec4 testClipDelta;")
                .Replace("normal=vec3", "testClipDelta=vec4(0);normal=vec3");
            fragment = """
                #version 430 core
                in vec4 testClipDelta;
                out vec4 result;
                void main() { result = testClipDelta; }
                """;
        }
        int vs=shaders.Compile(ShaderType.VertexShader,source),fs=shaders.Compile(ShaderType.FragmentShader,fragment);
        programs.Add("triangles",GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vs,fs)));
        {
            var stages = TerrainTessellationTestAssets.Generate(source,depthBias);
            if (observeClipDelta)
            {
                var tree = SyntaxTree.Parse(stages.Evaluation, GlslSchema.Instance);
                tree.CreateEditor().InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """
                    testClipDelta = gl_Position - (gl_in[0].gl_Position * gl_TessCoord.x
                        + gl_in[1].gl_Position * gl_TessCoord.y + gl_in[2].gl_Position * gl_TessCoord.z);
                    """).Commit();
                stages = stages with { Evaluation = tree.ToText() };
            }
            Assert.True(TerrainTessellationLinker.TryCreate(vs,fs,stages,TerrainTessellationPatches.EnabledDefine,out int id,out string error),error);
            programs.Add("adaptive",GpuProgramObject.Adopt(id));
        }
        string includes=Path.Combine(AppContext.BaseDirectory,"assets","shaders","includes");
        foreach(bool relief in new[]{false,true})
        {
            string reliefFragment="#version 430\n#define VGE_PBR_ENABLE_POM "+(relief?"1\n":"0\n")+"uniform sampler2D vge_normalDepthTex;out vec4 result;\n"
                +TerrainEyeRelativeShadingTests.Expand(Path.Combine(includes,"vge_normaldepth.glsl"))
                +TerrainEyeRelativeShadingTests.Expand(Path.Combine(includes,"vge_parallax.glsl"))+"""

                void main(){vec2 uv=gl_FragCoord.xy/256.0;vec3 p=vec3(uv-vec2(.5)+vec2(1,0),-2);result=vec4(VgeApplyPomUv_WithTbn(uv,mat3(1),1,p,vec2(0),vec2(1)),0,1);}
                """;
            programs.Add(relief?"relief":"reliefOff",GpuProgramObject.Adopt(TerrainShaderTestFixture.Link(vs,shaders.Compile(ShaderType.FragmentShader,reliefFragment))));
        }
        var indices=framework.CreateTexture(64,64,PixelInternalFormat.R32f,Enumerable.Repeat(1f,4096).ToArray());resources.Add(indices);
        var records=framework.CreateTexture(2,1,PixelInternalFormat.Rgba32f,[0,0,1,1,.04f,0,0,0]);resources.Add(records);
        var raised=framework.CreateTexture(64,64,PixelInternalFormat.Rgba32f,Enumerable.Range(0,4096).SelectMany(_=>new[]{.5f,.5f,1f,0f}).ToArray());resources.Add(raised);
        var neutral=framework.CreateTexture(64,64,PixelInternalFormat.Rgba32f,Enumerable.Range(0,4096).SelectMany(_=>new[]{.5f,.5f,1f,.5f}).ToArray());resources.Add(neutral);
        var missing=framework.CreateTexture(64,64,PixelInternalFormat.R32f,new float[4096]);resources.Add(missing);
        var zeroAmplitude=framework.CreateTexture(2,1,PixelInternalFormat.Rgba32f,[0,0,1,1,0,0,0,0]);resources.Add(zeroAmplitude);
        bindHeight=mode=>{if(mode=="adaptiveMissing")missing.Bind(0);else indices.Bind(0);if(mode=="adaptiveZeroAmplitude")zeroAmplitude.Bind(2);else records.Bind(2);if(mode=="adaptiveNeutral")neutral.Bind(1);else raised.Bind(1);};
        var target=framework.CreateTestGBuffer(256,256,PixelInternalFormat.Rgba32f);resources.Add(target);bindTarget=()=>target.BindWithViewport();readTarget=()=>target[0].ReadPixels();
    }

    /// <summary>Selects one immutable shader variant and publishes identical inputs outside timing.</summary>
    internal void Select(string mode, bool reactive = true, bool eligible = true)
    {
        int id=programs[mode.StartsWith("adaptive",StringComparison.Ordinal)?"adaptive":mode].ProgramId;
        topology=mode is "triangles" or "relief" or "reliefOff" ? PrimitiveType.Triangles : PrimitiveType.Patches;
        StateCache.Current.UseProgram(id);StateCache.Current.BindVertexArray(vao.VertexArrayId);StateCache.Current.SetPatchVertices(3);
        bindTarget();bindHeight(mode);
        // Publish this fixture's actual view instead of inheriting a prior test's camera range.
        Assert.True(camera.TryBindToSlot(GpuBindingRegistry.Ubo.Frame));
        GL.Disable(EnableCap.DepthTest);GL.Disable(EnableCap.CullFace);GL.Disable(EnableCap.Blend);
        var layout=GpuProgramLayout.TryBuild(id);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementTex"),0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_normalDepthTex"),1);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementRecords"),2);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementEnabled"),eligible && mode != "adaptiveDisabled"?1:0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_displacementReactive"),reactive?1:0);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_tessellationPixels"),8f,8f);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_tessellationFocalPixels"),256f);
        ShaderTestFramework.SetUniform(layout.GetUniformLocation(id,"vge_tessellationDistance"),mode=="adaptiveFaded"?0f:10f,mode=="adaptiveFaded"?.1f:20f);
        float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        ShaderTestFramework.SetUniformMatrix4(layout.GetUniformLocation(id,"modelViewMatrix"),identity);
    }

    /// <summary>Submits sixteen identical faces without readback or per-draw resource mutation.</summary>
    internal void Draw()=>GL.DrawArraysInstanced(topology,0,6,16);

    /// <summary>Observes the complete receiver outside every measured interval.</summary>
    internal float[] Read()=>readTarget();

    /// <summary>Restores topology state and releases all owned rendering resources.</summary>
    public void Dispose()
    {
        StateCache.Current.UseProgram(0);StateCache.Current.BindVertexArray(0);StateCache.Current.SetPatchVertices(oldPatch);
        foreach(var resource in resources)resource.Dispose();foreach(var program in programs.Values)program.Dispose();vao.Dispose();shaders.Dispose();framework.Dispose();
    }
    #endregion
}
