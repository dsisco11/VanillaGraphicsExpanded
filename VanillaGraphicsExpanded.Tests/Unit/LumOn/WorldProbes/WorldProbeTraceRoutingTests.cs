using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks routing contracts independently of the temporary CPU compatibility implementation.</summary>
public sealed class WorldProbeTraceRoutingTests
{
    #region Backend selection
    /// <summary>Only enabled L0 admissions use the GPU entry point, preserving the complete scheduler work item.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RoutesOriginalAdmissionByLevel(bool enabled)
    {
        var cpu=new Backend(); var gpu=new Backend();
        using(var router=new LumOnWorldProbeTraceRouter(enabled,cpu,gpu))
        {
            for(int level=0;level<3;level++)
            {
                var item=new LumOnWorldProbeTraceWorkItem(17,new(level,new(),new(),3,Ticket:123),new(1,2,3),99,16,7,true,.3f,2,.001f,5,true,42);
                var selected=enabled && level==0 ? gpu : cpu;
                Assert.True(router.TryEnqueue(item));
                Assert.Equal(item,selected.Items[^1]);
                selected.Accept=false;
                Assert.False(router.TryEnqueue(item));
                selected.Accept=true;
            }
            Assert.Equal(enabled?1:0,gpu.Items.Count);
            Assert.Equal(enabled?2:3,cpu.Items.Count);
        }
        Assert.Equal(1,cpu.Disposals); Assert.Equal(1,gpu.Disposals);
    }

    /// <summary>Both completion queues progress fairly and retain original result tickets and metadata.</summary>
    [Fact]
    public void AlternatesCompletionsAndFallsBackToNonemptyQueue()
    {
        var cpu=new Backend(); var gpu=new Backend();
        using var router=new LumOnWorldProbeTraceRouter(true,cpu,gpu);
        var first=default(LumOnWorldProbeTraceResult) with { FrameIndex=1,SurfaceRevision=71 };
        var second=first with { FrameIndex=2 };
        var third=first with { FrameIndex=3 };
        gpu.Results.Enqueue(first);gpu.Results.Enqueue(third);cpu.Results.Enqueue(second);
        foreach(var expected in new[]{first,second,third})
        { Assert.True(router.TryDequeueResult(out var actual));Assert.Equal(expected,actual); }
        Assert.False(router.TryDequeueResult(out _));
    }

    /// <summary>The compute backend bounds queued rays and defers scheduler claims until dispatch.</summary>
    [Fact]
    public void ComputeBackendBoundsAdmissionWithoutClaimingQueuedWork()
    {
        int claims=0;
        using var backend=new LumOnWorldProbeGpuTraceBackend(new Moq.Mock<Vintagestory.API.Common.ICoreAPI>().Object,256,
            ()=>null,()=>null,(_,_)=>{claims++;return true;});
        var item=new LumOnWorldProbeTraceWorkItem(0,new(0,new(),new(),0),new(1,2,3),64,64,4096,false,.25f,-1,1e-6f);
        Assert.Throws<ArgumentOutOfRangeException>(()=>backend.TryEnqueue(item with { ProbePosWorld=new(double.NaN,2,3) }));
        Assert.Equal(0,claims);
        Assert.True(backend.TryEnqueue(item));Assert.True(backend.TryEnqueue(item));
        Assert.False(backend.TryEnqueue(item)); Assert.Equal(0,claims);
        Assert.False(backend.TryEnqueue(item with { Request=item.Request with { Level=1 } }));
        backend.Dispose();
        Assert.False(backend.TryEnqueue(item));Assert.False(backend.TryDequeueResult(out _));
    }
    #endregion

    #region Configuration
    /// <summary>Existing configuration enables GPU routing and explicit choices survive persistence.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RoutingFlagDefaultsOnAndRoundTrips(bool enabled)
    {
        var config=Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>("{}")!;
        Assert.True(config.WorldProbeClipmap.EnableGpuTracing);
        config.WorldProbeClipmap.EnableGpuTracing=enabled;
        config=Newtonsoft.Json.JsonConvert.DeserializeObject<VgeConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(config))!;
        config.Sanitize();
        Assert.Equal(enabled,config.WorldProbeClipmap.EnableGpuTracing);
    }
    #endregion

    #region Controlled backend
    /// <summary>Records exact admissions and owns deterministic completion values without worker or GPU dependencies.</summary>
    private sealed class Backend : IWorldProbeTraceBackend
    {
        public List<LumOnWorldProbeTraceWorkItem> Items { get; }=[];
        public Queue<LumOnWorldProbeTraceResult> Results { get; }=[];
        public bool Accept { get; set; }=true;
        public int Disposals { get; private set; }
        /// <summary>Records only accepted work so rejected admissions cannot silently spill into another backend.</summary>
        public bool TryEnqueue(in LumOnWorldProbeTraceWorkItem item)
        { if(!Accept)return false;Items.Add(item);return true; }
        /// <summary>Returns each deterministic completion once.</summary>
        public bool TryDequeueResult(out LumOnWorldProbeTraceResult result)=>Results.TryDequeue(out result);
        /// <summary>Counts owner retirement.</summary>
        public void Dispose()=>Disposals++;
    }
    #endregion
}
