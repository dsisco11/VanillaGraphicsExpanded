using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Connects actual traced GPU textures to both production gather paths and final composition.</summary>
public abstract class SurfaceLightingPipelineTestBase : NearFieldShaderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    protected SurfaceLightingPipelineTestBase(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Retains intermediate readbacks for identifying the boundary that lost lighting.</summary>
    protected sealed record LightingPixels(float[] Trace, float[] Meta, float[] Filtered, float[] Gathered, float[] Final);

    #region Pipeline execution
    /// <summary>Traces only controlled offscreen geometry, then passes its GPU attachments directly downstream.</summary>
    private protected LightingPixels RenderLighting(SurfaceLightingEnclosureFixture room, bool sh9)
    {
        using var placeholder = new NearFieldVoxelFixture();
        LightingPixels? result = null;
        Trace(placeholder, worldCache: false, shared: room.Geometry.Scene, surfaceLighting: room.Snapshot,
            anchorPosition: new(0,0,-3), worldOffset: new(0,32,0), matrixRemainder: new(4,4,7),
            consume: output => result = FinishLighting(output, sh9));
        return Assert.IsType<LightingPixels>(result);
    }

    /// <summary>Runs filtering, optional SH projection, gather, upsample and material composition without synthesized lighting.</summary>
    protected LightingPixels FinishLighting(GpuFramebuffer traced, bool sh9)
    {
        var programs = new List<int>();
        var projection = LumOnTestInputFactory.CreateRealisticProjection();
        float z = -3, depthValue = (projection[10]*z+projection[14])/(projection[11]*z+projection[15])*.5f+.5f;
        using var anchor = TestFramework.CreateTexture(2,2,PixelInternalFormat.Rgba16f,CreateUniformColorData(2,2,0,0,-3,1));
        using var anchorNormal = TestFramework.CreateTexture(2,2,PixelInternalFormat.Rgba16f,CreateUniformColorData(2,2,.5f,.5f,1,0));
        using var depth = TestFramework.CreateTexture(4,4,PixelInternalFormat.R32f,Enumerable.Repeat(depthValue,16).ToArray());
        using var normal = TestFramework.CreateTexture(4,4,PixelInternalFormat.Rgba16f,CreateUniformColorData(4,4,.5f,.5f,1,0));
        using var albedo = TestFramework.CreateTexture(4,4,PixelInternalFormat.Rgba16f,CreateUniformColorData(4,4,.5f,.5f,.5f,1));
        using var material = TestFramework.CreateTexture(4,4,PixelInternalFormat.Rgba16f,CreateUniformColorData(4,4,1,0,0,0));
        using var black = TestFramework.CreateTexture(4,4,PixelInternalFormat.Rgba16f,new float[64]);
        using var filtered = TestFramework.CreateTestGBuffer(16,16,PixelInternalFormat.Rgba16f,PixelInternalFormat.Rg32f);
        using var projected = TestFramework.CreateTestGBuffer(2,2,Enumerable.Repeat(PixelInternalFormat.Rgba16f,7).ToArray());
        using var gathered = TestFramework.CreateTestGBuffer(2,2,PixelInternalFormat.Rgba16f);
        using var full = TestFramework.CreateTestGBuffer(4,4,PixelInternalFormat.Rgba16f);
        using var final = TestFramework.CreateTestGBuffer(4,4,PixelInternalFormat.Rgba16f);
        using var parameters = new ObjectParamsUbo("Tests.SurfaceLighting.Downstream");
        try
        {
            int filter = Program("lumon_probe_atlas_filter");
            Bind(filter,"octahedralAtlas",0,traced[0]); Bind(filter,"probeAtlasMeta",1,traced[1]); Bind(filter,"probeAnchorPosition",2,anchor);
            ProbeParameters(filter); TestFramework.RenderQuadTo(filter,filtered);
            int gather;
            if(sh9)
            {
                int project = Program("lumon_probe_atlas_project_sh9");
                Bind(project,"octahedralAtlas",0,filtered[0]); Bind(project,"probeAtlasMeta",1,filtered[1]); Bind(project,"probeAnchorPosition",2,anchor);
                TestFramework.RenderQuadTo(project,projected);
                gather = Program("lumon_probe_sh9_gather");
                for(int i=0;i<7;i++) Bind(gather,$"probeSh{i}",i,projected[i]);
                Bind(gather,"probeAnchorPosition",7,anchor); Bind(gather,"probeAnchorNormal",8,anchorNormal);
                Bind(gather,"primaryDepth",9,depth); Bind(gather,"gBufferNormal",10,normal);
            }
            else
            {
                gather=Program("lumon_probe_atlas_gather");
                Bind(gather,"octahedralAtlas",0,filtered[0]); Bind(gather,"probeAnchorPosition",1,anchor);
                Bind(gather,"probeAnchorNormal",2,anchorNormal); Bind(gather,"primaryDepth",3,depth); Bind(gather,"gBufferNormal",4,normal);
            }
            ProbeParameters(gather); TestFramework.RenderQuadTo(gather,gathered);
            int upsample=Program("lumon_upsample");
            Bind(upsample,"indirectHalf",0,gathered[0]); Bind(upsample,"primaryDepth",1,depth); Bind(upsample,"gBufferNormal",2,normal);
            UniformBlockBindingUtil.EnsureBlockBound(upsample,LumOnUpsampleParamsUbo.BlockName,GpuBindingRegistry.Ubo.Object);
            parameters.UploadAndBind(new LumOnUpsampleParamsUbo { UpsampleDepthSigma=.1f, UpsampleNormalSigma=16, UpsampleSpatialSigma=1 }.Bytes);
            TestFramework.RenderQuadTo(upsample,full);
            int combine=Program("lumon_combine");
            Bind(combine,"sceneDirect",0,black); Bind(combine,"indirectDiffuse",1,full[0]); Bind(combine,"gBufferAlbedo",2,albedo);
            Bind(combine,"gBufferMaterial",3,material); Bind(combine,"gBufferNormal",4,normal); Bind(combine,"primaryDepth",5,depth);
            UpdateAndBindLumOnCombineParamsUbo(combine,1,(1,1,1),0,0);
            TestFramework.RenderQuadTo(combine,final);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
            return new(traced[0].ReadPixels(),traced[1].ReadPixels(),filtered[0].ReadPixels(),gathered[0].ReadPixels(),final[0].ReadPixels());
        }
        finally { foreach(int program in programs) TestShaderInterfaces.DeleteProgram(program); }

        /// <summary>Loads the production binary variant and binds a consistent camera for every downstream pass.</summary>
        int Program(string name)
        {
            int program = name is "lumon_probe_atlas_gather" or "lumon_probe_sh9_gather"
                ? CompileShaderWithDefines(name+".vsh",name+".fsh",new Dictionary<string,string?> { ["VGE_LUMON_WORLDPROBE_ENABLED"]="0" })
                : CompileShader(name+".vsh",name+".fsh");
            programs.Add(program);
            UpdateAndBindLumOnFrameUbo(program,invProjectionMatrix:LumOnTestInputFactory.CreateRealisticInverseProjection(),projectionMatrix:projection);
            return program;
        }
        /// <summary>Supplies deterministic quality controls without changing radiance.</summary>
        void ProbeParameters(int program)
        {
            UniformBlockBindingUtil.EnsureBlockBound(program,LumOnProbeParamsUbo.BlockName,GpuBindingRegistry.Ubo.Object);
            parameters.UploadAndBind(new LumOnProbeParamsUbo { Intensity=1,IndirectTint=Vector3.One,SampleStride=1,FilterRadius=1,HitDistanceSigma=1,LeakThreshold=.5f }.Bytes);
        }
    }

    /// <summary>Binds a real upstream resource to the shader's declared sampler.</summary>
    private static void Bind(int program,string name,int unit,GpuTexture texture)
    {
        GL.UseProgram(program);
        GL.Uniform1(TestShaderInterfaces.GetUniformLocation(program,name),unit);
        texture.Bind(unit);
        GL.UseProgram(0);
    }
    #endregion

    #region Boundary assertions
    /// <summary>Asserts every RGB pixel is finite and the named boundary contains or excludes energy.</summary>
    protected static float AssertEnergy(float[] pixels,bool lit,string boundary)
    {
        float maximum=0;
        for(int i=0;i<pixels.Length;i+=4) for(int c=0;c<3;c++)
        {
            Assert.True(float.IsFinite(pixels[i+c]),boundary+": nonfinite RGB");
            maximum=Math.Max(maximum,pixels[i+c]);
            if(!lit) Assert.InRange(Math.Abs(pixels[i+c]),0,.0001f);
        }
        if(lit) Assert.True(maximum>.001f,$"{boundary}: expected produced lighting, maximum={maximum}");
        return maximum;
    }

    /// <summary>Reports each stage independently instead of attributing every failure to final composition.</summary>
    protected static void AssertPipeline(LightingPixels pixels,bool lit)
    {
        AssertEnergy(pixels.Trace,lit,"trace"); AssertEnergy(pixels.Filtered,lit,"filter");
        AssertEnergy(pixels.Gathered,lit,"gather"); AssertEnergy(pixels.Final,lit,"final pixels");
    }
    #endregion
}
