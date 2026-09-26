using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Verifies selective CPU geometry continuation, lifetime rejection and explicit bounded worker credit.</summary>
public sealed class WorldProbeCpuFallbackTests
{
    #region Selective traversal
    /// <summary>Successful GPU answers and missing lighting never consume CPU traversal credit.</summary>
    [Fact]
    public void OnlyCoverageAndUnsupportedAnswersAreRetraced()
    {
        var scene=new Scene();
        using var service=new WorldProbeCpuFallbackService(scene,_=>true);service.BeginFrame(0);
        var original=ImmutableArray.Create(new WorldProbeTraceAnswerGpu{Outcome=1,Hit=new(){Result=new(2,3,4,1)}},new WorldProbeTraceAnswerGpu{Outcome=1},new WorldProbeTraceAnswerGpu{Outcome=4},
            new WorldProbeTraceAnswerGpu{Outcome=3},new WorldProbeTraceAnswerGpu{Outcome=0,Reason=2},
            new WorldProbeTraceAnswerGpu{Outcome=0,Reason=1},new WorldProbeTraceAnswerGpu{Outcome=0,Reason=3});
        var work=new WorldProbeCpuFallbackWork(31,Item(),[0,1,2,3,4,5,6],original);
        Assert.True(service.TryEnqueue(work));var result=Read(service);
        Assert.True(result.Success);Assert.Equal(2,scene.Calls.Count);Assert.Equal(work.Item,result.Work.Item);
        for(int index=0;index<5;index++)Assert.Equal(original[index],result.Work.Answers[index]);
        Assert.All(result.Work.Answers.Skip(5),answer=>Assert.Equal(WorldProbeTraceOutcome.Sky,answer.TraceOutcome));
    }

    /// <summary>CPU outcomes remain explicit and cannot create repeated fallback loops or false sky.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void CpuOutcomesAndOriginalRayInputsArePreserved(int outcome)
    {
        var scene=new Scene { Outcome=(WorldProbeTraceOutcome)outcome };
        using var service=new WorldProbeCpuFallbackService(scene,_=>true);service.BeginFrame(1);
        var item=Item() with { FrameIndex=1,ProbePosWorld=new(-16777216.25,35.5,16777216.75),NearbySolidHitDistance=2 };
        var work=new WorldProbeCpuFallbackWork(7,item,[0x80000005u],[new(){Outcome=0,Reason=3}]);
        Assert.True(service.TryEnqueue(work));var result=Read(service);
        Assert.True(result.Success);Assert.Equal((WorldProbeTraceOutcome)outcome,result.Work.Answers[0].TraceOutcome);
        Assert.False(result.Work.Answers[0].RequiresCpuFallback);
        var call=Assert.Single(scene.Calls);Assert.Equal(item.ProbePosWorld,call.Origin);Assert.Equal(-Vector3.UnitZ,call.Direction);Assert.Equal(2,call.Distance);
        if(outcome==(int)WorldProbeTraceOutcome.Hit)
        { Assert.Equal(71,result.Work.Answers[0].Hit.BlockId);Assert.Equal(.25f,result.Work.Answers[0].Hit.Fraction.W);Assert.Equal(0,result.Work.Answers[0].Hit.Result.W); }
    }
    #endregion

    #region Bounded worker credit
    /// <summary>Waiters wake for exhausted credit and observe completed results repeatedly without draining them.</summary>
    [Fact]
    public async Task ProgressWaitPreservesResultsAndWakesForFrameCredit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var service = new WorldProbeCpuFallbackService(new Scene(), _ => true);
        Assert.True(service.TryEnqueue(Work(257)));
        await service.WaitForProgressAsync(timeout.Token);
        Assert.True(service.RequiresFrameCredit);
        service.BeginFrame(0);
        await service.WaitForProgressAsync(timeout.Token);
        Assert.True(service.RequiresFrameCredit);
        Assert.False(service.TryDequeue(out _));
        service.BeginFrame(1);
        await service.WaitForProgressAsync(timeout.Token);
        await service.WaitForProgressAsync(timeout.Token);
        Assert.True(service.TryDequeue(out var result));
        Assert.True(result.Success);
        Assert.False(service.HasOutstandingWork);
    }

    /// <summary>Canceling an observer leaves work untouched; disposal releases an observer blocked on terrain.</summary>
    [Fact]
    public async Task ProgressWaitCancellationAndDisposalDoNotConsumeWork()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var service = new WorldProbeCpuFallbackService(new Scene { BeforeTrace = token => { entered.Set(); release.Wait(token); } }, _ => true);
        service.BeginFrame(0);
        Assert.True(service.TryEnqueue(Work(1)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        var canceledWait = service.WaitForProgressAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
        Assert.True(service.HasOutstandingWork);
        var disposedWait = service.WaitForProgressAsync(TestContext.Current.CancellationToken);
        service.Dispose();
        await disposedWait.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        release.Set();
    }

    /// <summary>Unused credit never accumulates and repeated frame notification cannot bypass the 256-ray limit.</summary>
    [Fact]
    public void PerFrameRayStartsAreBoundedAndDoNotAccumulate()
    {
        var scene=new Scene();using var service=new WorldProbeCpuFallbackService(scene,_=>true);
        service.BeginFrame(0);service.BeginFrame(1);
        Assert.True(service.TryEnqueue(Work(513)));
        Assert.True(SpinWait.SpinUntil(()=>scene.Calls.Count==256,TimeSpan.FromSeconds(3)));
        Assert.False(SpinWait.SpinUntil(()=>scene.Calls.Count>256,TimeSpan.FromMilliseconds(50)));
        service.BeginFrame(1);
        Assert.False(SpinWait.SpinUntil(()=>scene.Calls.Count>256,TimeSpan.FromMilliseconds(50)));
        service.BeginFrame(2);Assert.True(SpinWait.SpinUntil(()=>scene.Calls.Count==512,TimeSpan.FromSeconds(3)));
        Assert.False(service.TryDequeue(out _));
        service.BeginFrame(3);Assert.True(Read(service).Success);Assert.Equal(513,scene.Calls.Count);
    }

    /// <summary>Completed but undrained answers retain both admission and ray storage charges.</summary>
    [Theory]
    [InlineData(64,1)] [InlineData(1,8192)]
    public void BackpressureIncludesCompletedUndrainedWork(int admissions,int answers)
    {
        var scene=new Scene();int checks=0;
        using var service=new WorldProbeCpuFallbackService(scene,_=>{Interlocked.Increment(ref checks);return true;});
        var work=Work(answers,false);
        for(int index=0;index<admissions;index++)Assert.True(service.TryEnqueue(work with {Id=index}));
        Assert.True(SpinWait.SpinUntil(()=>Volatile.Read(ref checks)>=admissions*2,TimeSpan.FromSeconds(3)));
        Assert.False(service.TryEnqueue(Work(1,false)));
        Assert.True(Read(service).Success);Assert.True(service.TryEnqueue(Work(1,false)));Assert.Empty(scene.Calls);
    }
    #endregion

    #region Lifetime
    /// <summary>A stale ticket cannot enter collision traversal, even when frame credit is available.</summary>
    [Fact]
    public void StaleTicketRejectsBeforeAnyCpuRead()
    {
        var scene=new Scene();using var service=new WorldProbeCpuFallbackService(scene,_=>false);service.BeginFrame(0);
        Assert.True(service.TryEnqueue(Work(1)));Assert.False(Read(service).Success);Assert.Empty(scene.Calls);
    }

    /// <summary>Disposal cancels a worker blocked in terrain access without waiting for it on the disposing thread.</summary>
    [Fact]
    public async Task DisposeCancelsWithoutBlockingOnCollision()
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var scene=new Scene { BeforeTrace=token=>{entered.Set();release.Wait(token);} };
        var service=new WorldProbeCpuFallbackService(scene,_=>true);service.BeginFrame(0);Assert.True(service.TryEnqueue(Work(1)));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3),TestContext.Current.CancellationToken));
        try { await Task.Run(service.Dispose,TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(1),TestContext.Current.CancellationToken);Assert.False(service.TryEnqueue(Work(1))); }
        finally { release.Set();service.Dispose(); }
    }
    #endregion

    #region Fixtures
    /// <summary>Creates a deterministic admission without any lighting dependency.</summary>
    private static LumOnWorldProbeTraceWorkItem Item()=>new(0,new(0,new(),new(),0,Ticket:11),new(.5,35.5,.5),64,8,8,false,.25f,-1,1e-6f,0,true);
    /// <summary>Allocates immutable retained answers for worker-storage and credit tests.</summary>
    private static WorldProbeCpuFallbackWork Work(int count,bool fallback=true)=>new(1,Item(),Enumerable.Repeat(0u,count).ToImmutableArray(),
        Enumerable.Repeat(new WorldProbeTraceAnswerGpu{Outcome=fallback?0:1,Reason=fallback?1:0},count).ToImmutableArray());
    /// <summary>Waits only in the test harness for one immutable completion.</summary>
    private static WorldProbeCpuFallbackResult Read(WorldProbeCpuFallbackService service)
    { WorldProbeCpuFallbackResult result=default;Assert.True(SpinWait.SpinUntil(()=>service.TryDequeue(out result),TimeSpan.FromSeconds(3)));return result; }
    /// <summary>Records exact ray inputs while permitting controlled outcomes and cancellation barriers.</summary>
    private sealed class Scene:IWorldProbeTraceScene
    {
        public ConcurrentQueue<(Vector3d Origin,Vector3 Direction,double Distance)> Calls { get; }=new();
        public WorldProbeTraceOutcome Outcome { get; init; }=WorldProbeTraceOutcome.Sky;
        public Action<CancellationToken>? BeforeTrace { get; init; }
        /// <summary>Supplies geometry only and preserves enough hit metadata to detect lossy conversions.</summary>
        public WorldProbeTraceOutcome Trace(Vector3d origin,Vector3 direction,double distance,CancellationToken token,out LumOnWorldProbeTraceHit hit)
        {
            BeforeTrace?.Invoke(token);Calls.Enqueue((origin,direction,distance));
            hit=new(.25,71,default,new((int)Math.Floor(origin.X-.25),35,(int)Math.Floor(origin.Z)),new(1,0,0),default,default);
            return Outcome;
        }
    }
    #endregion
}
