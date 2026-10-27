using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.ModSystems;
using System.Globalization;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures bounded opt-in shader arithmetic and lookup overhead independently of game rendering.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class MicrofacetEnergyMeasurementTests(HeadlessGLFixture fixture, ITestOutputHelper log) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Compares warm single scattering and compensated material evaluations with typical and repeated workloads.</summary>
    [Fact]
    public void WarmLookupCost()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_MICROFACET_MEASUREMENTS")=="1",
            "Explicit microfacet measurement run required.");
        EnsureContextValid();
        Helpers.MicrofacetTextureReference.Bind();
        using var modules=new TerrainShaderTestFixture();
        string directory=Path.Combine(AppContext.BaseDirectory,"assets/shaders/includes");
        string common=File.ReadAllText(Path.Combine(directory,"pbr_common.glsl"))
            .Replace("@import \"./common_constants.glsl\"",File.ReadAllText(Path.Combine(directory,"common_constants.glsl")));
        string energy=File.ReadAllText(Path.Combine(directory,"pbr_microfacet_energy.glsl"));
        int vertex=modules.Compile(ShaderType.VertexShader,"#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
        var programs=new List<int>();
        var records=new List<object>();
        try
        {
            foreach(bool compensated in new[]{false,true})
            {
                string source="#version 330 core\n"+energy+common+"""
                    uniform int evaluations;
                    layout(location=0)out vec4 result;
                    void main(){
                        vec2 uv=gl_FragCoord.xy/256.0;
                        vec3 N=vec3(0,0,1),V=normalize(vec3(uv*1.8-.9,1));
                        vec3 total=vec3(0);
                        for(int i=0;i<evaluations;i++){
                            float index=float(i);
                            vec3 L=normalize(vec3(sin(index*.31),cos(index*.19),1));
                            float roughness=clamp(.04+.96*uv.x+.001*sin(index),.04,1.0);
                            vec3 value=cookTorranceBRDF(N,V,L,vec3(.04,.5,1),roughness);
                    """+(compensated?"value*=VgeEvaluateMicrofacetEnergy(vec3(.04,.5,1),roughness,V.z).compensation;":"")+"""
                            total+=value*L.z;
                        }
                        result=vec4(total,1);
                    }
                    """;
                programs.Add(TerrainShaderTestFixture.Link(vertex,modules.Compile(ShaderType.FragmentShader,source)));
            }
            using var target=CreateRenderTarget(256,256,TextureFormat.Rgba32f);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer,target.FboId);GL.Viewport(0,0,256,256);
            GL.Disable(EnableCap.DepthTest);GL.Disable(EnableCap.CullFace);GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
            foreach(int evaluations in new[]{1,100})
            foreach(int program in programs)
            {
                GL.UseProgram(program);GL.Uniform1(GL.GetUniformLocation(program,"evaluations"),evaluations);
                for(int warmup=0;warmup<3;warmup++)RenderFullscreenQuad(program);
                var elapsed=new List<double>();
                for(int sample=0;sample<8;sample++)
                {
                    using var timer=GpuTimerQuery.Create();timer.Begin();RenderFullscreenQuad(program);timer.End();
                    elapsed.Add(timer.GetResultNanoseconds()/1e6);
                }
                double median=elapsed.Order().Skip(3).Take(2).Average();
                bool compensated=program==programs[1];
                log.WriteLine($"Microfacet lookup: compensated={compensated} pixels=65536 evaluations={evaluations} medianGPUms={median:F6}; GPU={GL.GetString(StringName.Renderer)}");
                records.Add(new{compensated,pixels=65536,evaluations,medianGpuMilliseconds=median,samplesMilliseconds=elapsed});
            }
            string? path=Environment.GetEnvironmentVariable("VGE_MICROFACET_MEASUREMENT_OUTPUT");
            if(!string.IsNullOrEmpty(path))File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new{
                renderer=GL.GetString(StringName.Renderer),lookupBytes=VanillaGraphicsExpanded.PBR.Materials.Energy.MicrofacetEnergyLookup.Prepare().Width
                    *VanillaGraphicsExpanded.PBR.Materials.Energy.MicrofacetEnergyLookup.Prepare().Height*4,records,
                limitation="Warm synthetic shader cost, varying roughness per evaluation. Not live frame cost or measured physical-memory bandwidth."
            },new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { foreach(int program in programs)GL.DeleteProgram(program);OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Qualifies one batched default-quality hardware lookup against retained independently integrated physical references.</summary>
    [Fact]
    public void DefaultTextureMatchesRetainedIndependentReferences()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_MICROFACET_QUALIFICATION")=="1",
            "Explicit retained-reference qualification required.");
        EnsureContextValid();
        string root=Environment.GetEnvironmentVariable("VGE_MICROFACET_REFERENCE_ROOT")!;
        var references=new[]{"generator-warped-corners.csv","generator-offgrid.csv"}
            .SelectMany(name=>File.ReadLines(Path.Combine(root,name)).Skip(1))
            .Select(line=>line.Split(',').Select(value=>double.Parse(value,CultureInfo.InvariantCulture)).ToArray()).ToArray();
        Assert.Equal(360,references.Length);
        int oldQuality=ConfigModSystem.Config.ShadingQuality;
        ConfigModSystem.Config.ShadingQuality=3;
        using var modules=new TerrainShaderTestFixture();
        int program=0;
        try
        {
            string helper=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"assets/shaders/includes/pbr_microfacet_energy.glsl"));
            int vertex=modules.Compile(ShaderType.VertexShader,"#version 330 core\nlayout(location=0)in vec2 position;void main(){gl_Position=vec4(position,0,1);}");
            int fragment=modules.Compile(ShaderType.FragmentShader,"#version 330 core\n"+helper+"\nlayout(binding=0)uniform sampler2D coordinates;layout(location=0)out vec4 result;void main(){vec2 p=texelFetch(coordinates,ivec2(int(gl_FragCoord.x),0),0).rg;result=vec4(VgeDirectionalAlbedo(p.x,p.y),0,1);}");
            program=TerrainShaderTestFixture.Link(vertex,fragment);
            var inputs=references.SelectMany(row=>new[]{(float)row[0],(float)row[1]}).ToArray();
            using var coordinates=RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = references.Length, Height = 1, Format = TextureFormat.Rg32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, references.Length, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rg32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((inputs).AsSpan()).ToArray())]).RequireResource();
            using var target=CreateRenderTarget(references.Length,1,TextureFormat.Rgba32f);
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, coordinates, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)coordinates).Target)))]);VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, 0, GpuSamplers.NearestClamp.SamplerId);Helpers.MicrofacetTextureReference.Bind();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer,target.FboId);GL.Viewport(0,0,references.Length,1);
            GL.Disable(EnableCap.DepthTest);GL.Disable(EnableCap.CullFace);GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
            RenderFullscreenQuad(program);
            var pixels=ReadPixelsFloat(target);
            double maxE=0,maxEf=0,maxW=0,maxR=0,idealMaxE=0,idealMaxEf=0;
            var cases=new List<object>();
            for(int i=0;i<references.Length;i++)
            {
                double e=references[i][2],ef=references[i][3],actualE=pixels[i*4],actualEf=pixels[i*4+1];
                var model=Helpers.MicrofacetTextureReference.Sample(128,(float)references[i][0],(float)references[i][1]);
                idealMaxE=Math.Max(idealMaxE,Math.Abs(model.Value.X/e-1));
                idealMaxEf=Math.Max(idealMaxEf,Math.Abs(model.Value.Y-ef));
                // Keep mathematical resampling and hardware-filter precision separate.
                // Actual physical error may add only this coordinate's derived filter envelope.
                Assert.InRange(Math.Abs(actualE-model.Value.X),0,model.Error.X);
                Assert.InRange(Math.Abs(actualEf-model.Value.Y),0,model.Error.Y);
                Assert.InRange(Math.Abs(actualE/e-1),0,.005+model.Error.X/e);
                Assert.InRange(Math.Abs(actualEf-ef),0,.0005+model.Error.Y);
                maxE=Math.Max(maxE,Math.Abs(actualE/e-1));maxEf=Math.Max(maxEf,Math.Abs(actualEf-ef));
                foreach(double f0 in new[]{0,.02037,.04,.5,1})
                {
                    double w=1+f0*(1-e)/e,actualW=1+f0*(1-actualE)/actualE;
                    double r=w*(e*f0+ef*(1-f0)),actualR=actualW*(actualE*f0+actualEf*(1-f0));
                    maxW=Math.Max(maxW,Math.Abs(actualW/w-1));maxR=Math.Max(maxR,Math.Abs(actualR-r));
                }
                cases.Add(new{roughness=references[i][0],viewCosine=references[i][1],expectedE=e,expectedEf=ef,actualE,actualEf});
            }
            string path=Environment.GetEnvironmentVariable("VGE_MICROFACET_QUALIFICATION_OUTPUT")??Path.Combine(root,"hardware-texture-quality.json");
            File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(new{renderer=GL.GetString(StringName.Renderer),size=128,maxRelativeE=maxE,maxAbsoluteEf=maxEf,maxRelativeW=maxW,maxAbsoluteR=maxR,idealMaxE,idealMaxEf,
                errorPartition="Ideal half-quantized bilinear table retains .5% relative E/.0005 absolute Ef; actual hardware adds only per-coordinate half-output rounding, local filter-weight slopes and Float32 epsilon.",cases},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
            log.WriteLine($"Actual default texture: maxRelativeE={maxE:R}, maxAbsoluteEf={maxEf:R}, maxRelativeW={maxW:R}, maxAbsoluteR={maxR:R}");
            Assert.InRange(idealMaxE,0,.005);Assert.InRange(idealMaxEf,0,.0005);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally
        {
            ConfigModSystem.Config.ShadingQuality=oldQuality;MicrofacetEnergyLookup.Prepare();
            if(program!=0)GL.DeleteProgram(program);OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
