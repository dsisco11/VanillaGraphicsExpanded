using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares the production AO owner against its frozen prior submission and descriptor policy.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AoMinimumChildProductionTests(HeadlessGLFixture fixture, ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks exact images at small odd, collapsed, tied, cutout and transmitted receiver workloads.</summary>
    [Theory]
    [InlineData(1,1,1,0)]
    [InlineData(1,257,1,0)]
    [InlineData(33,1,2,4)]
    [InlineData(65,37,1,4)]
    [InlineData(65,37,2,5)]
    [InlineData(257,129,1,9)]
    [InlineData(257,129,2,9)]
    [InlineData(67,39,2,6)]
    [InlineData(67,39,2,8)]
    [InlineData(67,39,2,1)]
    [InlineData(67,39,2,2)]
    [InlineData(67,39,2,3)]
    [InlineData(67,39,2,7)]
    [InlineData(67,39,2,0)]
    [InlineData(65,37,1,10)]
    [InlineData(65,37,2,10)]
    public void SmallImages(int width,int height,int quality,int scene)
    {
        Synthetic(width,height,quality,scene,false);
    }

    /// <summary>Measures the full production path only when explicitly requested.</summary>
    [Theory]
    [InlineData(1280,720,2,4)]
    [InlineData(1280,720,2,5)]
    [InlineData(3440,1377,1,4)]
    [InlineData(3440,1377,2,4)]
    [InlineData(3440,1377,2,5)]
    [InlineData(3440,1377,2,9)]
    [InlineData(3440,1377,2,0)]
    [InlineData(3440,1377,1,10)]
    [InlineData(3440,1377,2,10)]
    public void FullImagesAndCosts(int width,int height,int quality,int scene)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_AO_MASK_EVALUATION")=="1","Explicit production mask measurement required.");
        Synthetic(width,height,quality,scene,true);
    }

    /// <summary>Checks captured raw output and every hierarchy mip before measuring both complete owners.</summary>
    [Fact]
    public void CapturedImageAndCosts()
    {
        string? directory=Environment.GetEnvironmentVariable("VGE_AO_CAPTURE_INPUT_DIR");
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_AO_MASK_EVALUATION")=="1" && !string.IsNullOrEmpty(directory),"Explicit captured production measurement required.");
        EnsureShaderTestAvailable();
        using var inputs=new CapturedAmbientOcclusionFixture(Programs,directory!);
        Run(inputs.Width,inputs.Height,inputs.Quality,-1,inputs.CapturedOutput,true,
            (reference,draw,target)=>inputs.PrepareComparison(reference,draw,target,target));
    }
    #endregion

    #region Private
    #region Fixture execution
    /// <summary>Builds immutable geometry shared by both owners, preserving narrow-FOV collapsed-row coverage.</summary>
    private void Synthetic(int width,int height,int quality,int scene,bool measure)
    {
        EnsureShaderTestAvailable();
        using var inputs=new AmbientOcclusionSceneFixture(TestFramework,Programs,width,height,scene,
            verticalFov:height==1?1:90,occluderTransmission:.3f,receiverTransmission:.2f);
        Run(width,height,quality,scene,null,measure,
            (reference,draw,target)=>inputs.PrepareHorizonComparison(reference,draw,target,target,quality));
    }

    /// <summary>Uses the same mutable filter publication policy and includes hierarchy and mask generation in each query.</summary>
    private void Run(int width,int height,int quality,int scene,float[]? captured,bool measure,
        Func<AoHorizonReferenceProgram,PostprocessDraw,GpuFramebuffer,(Action Reference,Action Current)> prepare)
    {
        using var draw=new PostprocessDraw();
        var reference=Programs.Create<AoHorizonReferenceProgram>();
        using var original=TestFramework.CreateTestGBuffer((width+1)/2,(height+1)/2,TextureFormat.Rgba16f);
        prepare(reference,draw,original).Reference();
        float[] rawOriginal=original[0].ReadPixels();
        if(captured is not null) SameBits(captured,rawOriginal);
        using var hierarchy=new DepthHierarchyPass();
        hierarchy.Prepare(width,height,Programs.CreateDepthHierarchy());
        hierarchy.Render(reference.ReceiverDepth);
        var imported=(DynamicTexture2D)reference.Hierarchy;
        for(int level=0;level<imported.MipLevels;level++) SameBits(imported.ReadPixels(level),hierarchy.Texture!.ReadPixels(level));
        using var frozen=new FrozenAmbientOcclusionPass();
        using var current=new AmbientOcclusionPass();
        var baselinePipelines=frozen.Prepare(draw,width,height,quality,reference,Programs.Create<AmbientOcclusionFilterShaderProgram>());
        var candidatePipelines=current.Prepare(draw,width,height,quality,Programs.Create<PostSsaoShaderProgram>(),
            Programs.Create<AmbientOcclusionFilterShaderProgram>(),Programs.Create<MinimumChildMaskShaderProgram>());
        Action baseline=()=> {
            hierarchy.Render(reference.ReceiverDepth);
            {
    var commands=RHICommandContext.Current;
    using var scope1=commands.EnterBoundary(new("Tests.FrozenAoOwner",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>(baselinePipelines,value=>value),operations:BoundaryOperations.RenderPasses));
    scope1.Run(()=>frozen.Render(commands,draw,reference.ReceiverDepth,reference.Surface,hierarchy.Texture!,reference.Camera));
}
        };
        Action candidate=()=> {
            hierarchy.Render(reference.ReceiverDepth);
            {
    var commands=RHICommandContext.Current;
    using var scope2=commands.EnterBoundary(new("Tests.CurrentAoOwner",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>(candidatePipelines,value=>value),operations:BoundaryOperations.RenderPasses));
    scope2.Run(()=>current.Render(commands,draw,reference.ReceiverDepth,reference.Surface,hierarchy.Texture!,reference.Camera));
}
        };
        baseline();candidate();
        SameBits(rawOriginal,Read(frozen,"raw"));
        CompareStages(frozen,current);
        var masks=Assert.IsType<MinimumChildMaskPass>(AccessTools.Field(typeof(AmbientOcclusionPass),"minimumChildren").GetValue(current));
        ValidateMasks(masks.Texture!,hierarchy.Texture!,width,height);
        List<double> before=[],after=[];
        if(measure) {
            for(int warmup=0;warmup<4;warmup++){baseline();candidate();}
            // Alternate order within each paired round to expose contention rather than timing all of one owner first.
            for(int round=0;round<24;round++) {
                if((round&1)==0){before.Add(Time(baseline));after.Add(Time(candidate));}
                else{after.Add(Time(candidate));before.Add(Time(baseline));}
            }
        }
        baseline();candidate();CompareStages(frozen,current);
        if(measure) {
            int[] gamePids=Process.GetProcessesByName("Vintagestory").Select(process=>process.Id).ToArray();
            string output=Environment.GetEnvironmentVariable("VGE_AO_MASK_OUTPUT_DIR")??Path.Combine(AppContext.BaseDirectory,"ao-production-masks");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,$"production-{width}x{height}-q{quality}-scene{scene}.json"),JsonSerializer.Serialize(new {
                width,height,quality,scene,capturedAtUtc=DateTime.UtcNow,gamePids,contended=gamePids.Length>0,
                renderer=GL.GetString(StringName.Renderer),hierarchyBytes=Enumerable.Range(0,hierarchy.Texture!.MipLevels).Sum(level=>4L*Math.Max(1,width>>level)*Math.Max(1,height>>level)),
                addedMaskBytes=current.StorageBytes-frozen.StorageBytes,
                description="Actual production owner versus frozen old owner. Identical mutable single-filter publication policy. Full GPU queries include hierarchy, mask generation where applicable, raw horizon, filter and reconstruction. Preparation, allocations, readback and query waits excluded. Game-open timings do not prove live frame-time improvement.",
                referenceMilliseconds=before,candidateMilliseconds=after,pairedSavingsMilliseconds=before.Zip(after,(a,b)=>a-b).ToArray()
            },new JsonSerializerOptions{WriteIndented=true}));
            log.WriteLine($"Production AO {width}x{height} q{quality} scene{scene}: reference median {Median(before):F6}ms candidate {Median(after):F6}ms paired saving {Median(before.Zip(after,(a,b)=>a-b)):F6}ms");
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    #endregion
    #region Image and storage validation
    /// <summary>Reconstructs each conservative footprint independently on the CPU, including nine-child odd extents.</summary>
    private static void ValidateMasks(DynamicTexture2D masks,DynamicTexture2D hierarchy,int width,int height)
    {
        for(int level=1;level<=Math.Min(4,hierarchy.MipLevels-1);level++)
        {
            int w=Math.Max(1,width>>level),h=Math.Max(1,height>>level);
            int cw=Math.Max(1,width>>(level-1)),ch=Math.Max(1,height>>(level-1));
            float[] parent=hierarchy.ReadPixels(level),children=hierarchy.ReadPixels(level-1);
            ushort[] bits=new ushort[w*h];
            using(OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,masks.TextureId))
            using(OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new OpenGlStateCache.PixelPackState(1)))
            {
                GL.GetTexLevelParameter(TextureTarget.Texture2D,level-1,GetTextureParameter.TextureWidth,out int allocatedWidth);
                GL.GetTexLevelParameter(TextureTarget.Texture2D,level-1,GetTextureParameter.TextureHeight,out int allocatedHeight);
                GL.GetTexLevelParameter(TextureTarget.Texture2D,level-1,GetTextureParameter.TextureInternalFormat,out int allocatedFormat);
                Assert.Equal(w,allocatedWidth);Assert.Equal(h,allocatedHeight);
                Assert.Equal((int)PixelInternalFormat.R16ui,allocatedFormat);
                GL.GetTexImage(TextureTarget.Texture2D,level-1,PixelFormat.RedInteger,PixelType.UnsignedShort,bits);
            }
            for(int y=0;y<h;y++) for(int x=0;x<w;x++)
            {
                int firstX=x*cw/w,firstY=y*ch/h,endX=((x+1)*cw+w-1)/w,endY=((y+1)*ch+h-1)/h;
                uint expected=0;
                for(int dy=0;dy<3;dy++) for(int dx=0;dx<3;dx++)
                    if(firstX+dx<endX && firstY+dy<endY && children[(firstY+dy)*cw+firstX+dx]==parent[y*w+x]) expected|=1u<<(dy*3+dx);
                Assert.Equal(expected,(uint)bits[y*w+x]);
            }
        }
    }

    /// <summary>Checks raw, filtered and reconstructed bits through borrowed test-only reflection.</summary>
    private static void CompareStages(FrozenAmbientOcclusionPass frozen,AmbientOcclusionPass current)
    {
        var stages=AmbientOcclusionSceneFixture.ReadStages(current);
        SameBits(Read(frozen,"raw"),stages.Raw);
        SameBits(Read(frozen,"filtered"),stages.Filtered);
        SameBits(((DynamicTexture2D)frozen.Texture!).ReadPixels(),stages.Reconstructed);
    }

    /// <summary>Reads owned baseline stages without changing the executable owner.</summary>
    private static float[] Read(FrozenAmbientOcclusionPass frozen,string field)
    {
        return Assert.IsType<DynamicTexture2D>(AccessTools.Field(typeof(FrozenAmbientOcclusionPass),field).GetValue(frozen)).ReadPixels();
    }

    /// <summary>Rejects any changed floating-point representation, including signed zero.</summary>
    private static void SameBits(float[] expected,float[] actual)
    {
        Assert.Equal(expected.Length,actual.Length);
        Assert.True(MemoryMarshal.AsBytes(expected.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(actual.AsSpan())),"AO image bits differ.");
    }

    #endregion
    #region Timing
    /// <summary>Includes only submitted GPU work in elapsed-time queries.</summary>
    private static double Time(Action action)
    {
        using var timer=GpuTimerQuery.Create();timer.Begin();action();timer.End();
        return timer.GetResultNanoseconds()/1e6;
    }

    /// <summary>Computes the midpoint of the sorted paired samples.</summary>
    private static double Median(IEnumerable<double> values)
    {
        var ordered=values.Order().ToArray();
        return (ordered[(ordered.Length-1)/2]+ordered[ordered.Length/2])/2;
    }
    #endregion
    #endregion
}
