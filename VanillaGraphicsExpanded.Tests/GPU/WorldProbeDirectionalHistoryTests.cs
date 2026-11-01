using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies ring-slot history retirement without erasing overlapping probes or leaking GL state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WorldProbeDirectionalHistoryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Physical slot invalidation
    /// <summary>A full reset crosses the guaranteed X workgroup limit without leaving the final physical slots uncleared.</summary>
    [Fact]
    public void FullResetCoversMoreThan65535PhysicalSlots()
    {
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        using var resources=new LumOnWorldProbeClipmapGpuResources(assets.Api,41,1,1);
        Fill(resources);
#if DEBUG
        long dispatches=resources.InvalidationDispatchCount;
#endif
        resources.ClearAll();
#if DEBUG
        Assert.Equal(dispatches+1,resources.InvalidationDispatchCount);
#endif
        ImageAssertions.CompareConstant(resources.ProbeRadianceAtlas, 0).AssertEqual();
        ImageAssertions.CompareConstant(resources.ProbeVis0, 0).AssertEqual();
        ImageAssertions.CompareConstant(resources.ProbeDist0, 0).AssertEqual();
        ImageAssertions.CompareConstant(resources.ProbeMeta0, 0).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Resources cannot become visible when their history-clear program cannot load.</summary>
    [Fact]
    public void InvalidClearProgramRejectsResourceCreation()
    {
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        assets.Overrides["shaders/lumon_worldprobe_history_clear.csh.spv"]=[];
        assets.Overrides["shaders/lumon_worldprobe_history_clear.csh"]=System.Text.Encoding.UTF8.GetBytes("invalid shader");
        Assert.Throws<InvalidOperationException>(()=>new LumOnWorldProbeClipmapGpuResources(assets.Api,2,1,4));
    }

    /// <summary>Duplicate overlapping requests clear once, an empty flush preserves new data, and full reset clears every target.</summary>
    [Fact]
    public void QueuedOverlapAndEmptyFlushPreserveNewHistoryUntilFullReset()
    {
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        using var resources=new LumOnWorldProbeClipmapGpuResources(assets.Api,4,2,4);
#if DEBUG
        long initialDispatches=resources.InvalidationDispatchCount;
#endif
        Assert.Equal(0,resources.PendingInvalidationCount);
        Fill(resources);
        resources.QueueClearLocalBox(0,new(),new(),new(2,1,1));
        Assert.Equal(12,resources.PendingInvalidationCount);
#if DEBUG
        Assert.Equal(initialDispatches,resources.InvalidationDispatchCount);
#endif
        resources.QueueClearLocalBox(0,new(),new(1,0,0),new(3,1,1));
        resources.QueueClearLocalBox(0,new(),new(),new(2,1,1));
        Assert.Equal(16,resources.PendingInvalidationCount);
        ImageAssertions.CompareConstant(resources.ProbeRadianceAtlas, 1).AssertEqual();
        resources.FlushHistoryInvalidation();
#if DEBUG
        Assert.Equal(initialDispatches+1,resources.InvalidationDispatchCount);
#endif
        Assert.Equal(0,resources.PendingInvalidationCount);
        var cleared=ImageAssertions.Predicate(resources.ProbeRadianceAtlas,"return bvec2(true,a.a==0);");
        cleared.AssertEqual();Assert.Equal((uint)(16<<4),cleared.MatchingTexels);
        Fill(resources);
        resources.FlushHistoryInvalidation();
#if DEBUG
        Assert.Equal(initialDispatches+1,resources.InvalidationDispatchCount);
#endif
        ImageAssertions.CompareConstant(resources.ProbeRadianceAtlas, 1).AssertEqual();
        ImageAssertions.CompareConstant(resources.ProbeMeta0, 1).AssertEqual();
        // Full reset must be immediate and independent of fixed-function write restrictions.
        GL.Enable(EnableCap.ScissorTest);GL.Scissor(0,0,1,1);GL.ColorMask(false,false,false,false);
        try { resources.ClearAll(); }
        finally { GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true); }
        ImageAssertions.CompareConstant(resources.ProbeRadianceAtlas, 0).AssertEqual();
        ImageAssertions.CompareConstant(resources.ProbeMeta0, 0).AssertEqual();
        ImageAssertions.CompareConstant(resources.ProbeVis0, 0).AssertEqual();
        ImageAssertions.CompareConstant(resources.ProbeDist0, 0).AssertEqual();
#if DEBUG
        Assert.Equal(initialDispatches+2,resources.InvalidationDispatchCount);
#endif
        Assert.Equal(0,resources.PendingInvalidationCount);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>A wrapped inclusive local box clears every directional and scalar target only in the selected level.</summary>
    [Fact]
    public void WrappedLocalBoxPreservesOtherSlotsAndRenderState()
    {
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        using var resources=new LumOnWorldProbeClipmapGpuResources(assets.Api,4,2,4);
        GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
        FramebufferTestSetup.ClearPass(resources.RadiancePass,1);
        FramebufferTestSetup.ClearPass(resources.MetadataPass,1);
        GL.Enable(EnableCap.ScissorTest);GL.Scissor(3,4,5,6);GL.ColorMask(false,true,false,true);
        try
        {
            resources.QueueClearLocalBox(1,new VectorInt3(3,2,1),new VectorInt3(),new VectorInt3(2,1,1));
            resources.FlushHistoryInvalidation();
            int[] scissor=new int[4];bool[] mask=new bool[4];
            GL.GetInteger(GetPName.ScissorBox,scissor);GL.GetBoolean(GetPName.ColorWritemask,mask);
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
            Assert.Equal(new[]{3,4,5,6},scissor);Assert.Equal(new[]{false,true,false,true},mask);
        }
        finally { GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true); }
        // Author expected coordinates independently of the invalidation shader, including every direction.
        var radiance=new float[resources.ProbeRadianceAtlas.Width*resources.ProbeRadianceAtlas.Height*4];
        var metadata=new float[resources.ProbeMeta0.Width*resources.ProbeMeta0.Height*2];
        var visibility=new float[resources.ProbeVis0.Width*resources.ProbeVis0.Height*4];
        var distance=new float[resources.ProbeDist0.Width*resources.ProbeDist0.Height*2];
        for(int level=0;level<2;level++)for(int z=0;z<4;z++)for(int y=0;y<4;y++)for(int x=0;x<4;x++)
        {
            bool cleared=level==1 && x!=2 && y>=2 && z>=1 && z<=2;
            float expected=cleared?0:1;
            int scalar=((level*4+y)*16+z*4+x)*4;
            metadata[scalar>>1]=expected;visibility[scalar]=expected;distance[scalar>>1]=expected;
            for(int v=0;v<4;v++)for(int u=0;u<4;u++)
            {
                int pixel=(((level*4+y)*4+v)*64+(z*4+x)*4+u)*4;
                for(int c=0;c<4;c++)radiance[pixel+c]=expected;
            }
        }
        ImageAssertions.Compare(resources.ProbeRadianceAtlas,radiance).AssertEqual();
        ImageAssertions.Compare(resources.ProbeMeta0,metadata,channelMask:1).AssertEqual();
        ImageAssertions.Compare(resources.ProbeVis0,visibility,channelMask:1).AssertEqual();
        ImageAssertions.Compare(resources.ProbeDist0,distance,channelMask:1).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Authored history
    /// <summary>Fills all production attachments with a visible sentinel before queueing invalidation.</summary>
    private static void Fill(LumOnWorldProbeClipmapGpuResources resources)
    {
        GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
        FramebufferTestSetup.ClearPass(resources.RadiancePass,1);
        FramebufferTestSetup.ClearPass(resources.MetadataPass,1);
    }
    #endregion
}
