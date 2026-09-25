using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies ring-slot history retirement without erasing overlapping probes or leaking GL state.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
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
        long dispatches=resources.InvalidationDispatchCount;
        resources.ClearAll();
        Assert.Equal(dispatches+1,resources.InvalidationDispatchCount);
        Assert.All(resources.ProbeRadianceAtlas.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.All(resources.ProbeVis0.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.All(resources.ProbeDist0.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.All(resources.ProbeMeta0.ReadPixels(),value=>Assert.Equal(0,value));
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
        long initialDispatches=resources.InvalidationDispatchCount;
        Assert.Equal(0,resources.PendingInvalidationCount);
        Fill(resources);
        resources.QueueClearLocalBox(0,new(),new(),new(2,1,1));
        Assert.Equal(12,resources.PendingInvalidationCount);
        Assert.Equal(initialDispatches,resources.InvalidationDispatchCount);
        resources.QueueClearLocalBox(0,new(),new(1,0,0),new(3,1,1));
        resources.QueueClearLocalBox(0,new(),new(),new(2,1,1));
        Assert.Equal(16,resources.PendingInvalidationCount);
        Assert.All(resources.ProbeRadianceAtlas.ReadPixels(),value=>Assert.Equal(1,value));
        resources.FlushHistoryInvalidation();
        Assert.Equal(initialDispatches+1,resources.InvalidationDispatchCount);
        Assert.Equal(0,resources.PendingInvalidationCount);
        Assert.Equal(16<<4,resources.ProbeRadianceAtlas.ReadPixels().Where((_,i)=>(i&3)==3).Count(value=>value==0));
        Fill(resources);
        resources.FlushHistoryInvalidation();
        Assert.Equal(initialDispatches+1,resources.InvalidationDispatchCount);
        Assert.All(resources.ProbeRadianceAtlas.ReadPixels(),value=>Assert.Equal(1,value));
        Assert.All(resources.ProbeMeta0.ReadPixels(),value=>Assert.Equal(1,value));
        // Full reset must be immediate and independent of fixed-function write restrictions.
        GL.Enable(EnableCap.ScissorTest);GL.Scissor(0,0,1,1);GL.ColorMask(false,false,false,false);
        try { resources.ClearAll(); }
        finally { GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true); }
        Assert.All(resources.ProbeRadianceAtlas.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.All(resources.ProbeMeta0.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.All(resources.ProbeVis0.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.All(resources.ProbeDist0.ReadPixels(),value=>Assert.Equal(0,value));
        Assert.Equal(initialDispatches+2,resources.InvalidationDispatchCount);
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
        resources.GetRadianceFbo().BindWithViewport();GL.ClearBuffer(ClearBuffer.Color,0,new[]{1f,1f,1f,1f});
        resources.GetFbo().BindWithViewport();
        for(int attachment=0;attachment<3;attachment++) GL.ClearBuffer(ClearBuffer.Color,attachment,new[]{1f,1f,1f,1f});
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
        var radiance=resources.ProbeRadianceAtlas.ReadPixels();
        var metadata=resources.ProbeMeta0.ReadPixels();
        var visibility=resources.ProbeVis0.ReadPixels();
        var distance=resources.ProbeDist0.ReadPixels();
        for(int level=0;level<2;level++)for(int z=0;z<4;z++)for(int y=0;y<4;y++)for(int x=0;x<4;x++)
        {
            bool cleared=level==1 && x!=2 && y>=2 && z>=1 && z<=2;
            float expected=cleared?0:1;
            int scalar=((level*4+y)*16+z*4+x)*4;
            Assert.Equal(expected,metadata[scalar>>1]);Assert.Equal(expected,visibility[scalar]);Assert.Equal(expected,distance[scalar>>1]);
            for(int v=0;v<4;v++)for(int u=0;u<4;u++)
            {
                int pixel=(((level*4+y)*4+v)*64+(z*4+x)*4+u)*4;
                for(int c=0;c<4;c++) Assert.Equal(expected,radiance[pixel+c]);
            }
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #region Authored history
    /// <summary>Fills all production attachments with a visible sentinel before queueing invalidation.</summary>
    private static void Fill(LumOnWorldProbeClipmapGpuResources resources)
    {
        GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
        resources.GetRadianceFbo().BindWithViewport();GL.ClearBuffer(ClearBuffer.Color,0,new[]{1f,1f,1f,1f});
        resources.GetFbo().BindWithViewport();
        for(int attachment=0;attachment<3;attachment++) GL.ClearBuffer(ClearBuffer.Color,attachment,new[]{1f,1f,1f,1f});
    }
    #endregion
}
