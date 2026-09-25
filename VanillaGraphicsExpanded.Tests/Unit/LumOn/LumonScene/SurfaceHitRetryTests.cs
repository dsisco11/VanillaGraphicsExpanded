using System.Collections.Immutable;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;
using VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.LumonScene;

/// <summary>Checks bounded retained geometry and dependency-driven hit lighting retries independently of GPU timing.</summary>
public sealed class SurfaceHitRetryTests
{
    #region Dependency progress
    /// <summary>Unchanged missing pages sleep while publication of their lighting wakes the retained query batch.</summary>
    [Fact]
    public void OnlyMissingDependencyProgressWakesQuery()
    {
        var entry=Entry(1,queries:2);
        ImmutableArray<SurfaceHitDependency> states=[Dependency(1),Dependency(2)];
        Assert.True(entry.Observe(states));Assert.True(entry.NeedsQuery);
        entry.Submitted();Assert.True(entry.Pending);Assert.False(entry.NeedsQuery);
        Assert.False(entry.Complete([Answer(true),Answer(false)]));
        Assert.True(entry.Observe(states));Assert.False(entry.NeedsQuery);
        states=states.SetItem(0,states[0] with{Publication=2});
        Assert.True(entry.Observe(states));Assert.False(entry.NeedsQuery);
        states=states.SetItem(1,states[1] with{Publication=2});
        Assert.True(entry.Observe(states));Assert.True(entry.NeedsQuery);
        Assert.Equal(2,entry.Queries.Length);
        entry.Submitted();Assert.True(entry.Complete([Answer(true),Answer(true)]));
    }

    /// <summary>Initially absent pages can acquire identity, capture and lighting without invalidating their retained rays.</summary>
    [Fact]
    public void MissingPageCanBecomeCaptured()
    {
        var entry=Entry(1);
        Assert.True(entry.Observe([default]));entry.Submitted();Assert.False(entry.Complete([Answer(false)]));
        Assert.True(entry.Observe([Dependency(3) with{Captured=false,Publication=0}]));Assert.True(entry.NeedsQuery);
        entry.Submitted();Assert.False(entry.Complete([Answer(false)]));
        Assert.True(entry.Observe([Dependency(3)]));Assert.True(entry.NeedsQuery);
    }

    /// <summary>Captured page identity is immutable even if the same physical slot later becomes ready again.</summary>
    [Theory]
    [InlineData("page")] [InlineData("key")] [InlineData("generation")] [InlineData("capture")] [InlineData("uncaptured")]
    public void ReplacedCapturedDependencyRejectsRetainedGeometry(string change)
    {
        var entry=Entry(1);var state=Dependency(3);Assert.True(entry.Observe([state]));
        var changed=change switch
        {
            "page"=>state with{Page=4},"key"=>state with{Key=4},"generation"=>state with{Generation=2},
            "capture"=>state with{CaptureRevision=2},_=>state with{Captured=false}
        };
        Assert.False(entry.Validate([changed]));
    }

    /// <summary>A changed dependency observed while GPU queries are pending is not lost when incomplete answers return.</summary>
    [Fact]
    public void ProgressDuringPendingQueryRemainsEligible()
    {
        var entry=Entry(1);var state=Dependency(3);Assert.True(entry.Observe([state]));entry.Submitted();
        Assert.True(entry.Observe([state with{Publication=2}]));
        Assert.False(entry.Complete([Answer(false)]));Assert.True(entry.NeedsQuery);Assert.False(entry.Pending);
    }

    /// <summary>Unavailable and nonfinite results remain missing and cannot produce an estimator commit.</summary>
    [Theory]
    [InlineData(false,1f)] [InlineData(true,float.NaN)] [InlineData(true,float.PositiveInfinity)]
    public void MissingAndInvalidAnswersWaitForActualPublication(bool available,float value)
    {
        var entry=Entry(1);var state=Dependency(3);Assert.True(entry.Observe([state]));entry.Submitted();
        var answer=Answer(available);answer.Result.X=value;
        Assert.False(entry.Complete([answer]));Assert.True(entry.Observe([state]));Assert.False(entry.NeedsQuery);
        Assert.True(entry.Observe([state with{Publication=2}]));Assert.True(entry.NeedsQuery);
    }
    #endregion

    #region Storage and scheduling
    /// <summary>The retained GPU record uses the exact bounded byte layout consumed by shader readback.</summary>
    [Fact]
    public void GpuRecordLayoutMatchesReadbackContract()
    {
        Assert.Equal(4176,Marshal.SizeOf<SurfaceHitCapture>());
        Assert.Equal(64,Marshal.OffsetOf<SurfaceHitCapture>(nameof(SurfaceHitCapture.Complete)).ToInt32());
        Assert.Equal(80,Marshal.OffsetOf<SurfaceHitCapture>(nameof(SurfaceHitCapture.Queries)).ToInt32());
    }

    /// <summary>Full storage rejects new or duplicate texels without evicting pending query ownership.</summary>
    [Fact]
    public void CapacityAndDuplicateAdmissionAreBounded()
    {
        var cache=new SurfaceHitRetryCache();var first=Entry(1);
        Assert.True(cache.TryAdd(first));Assert.False(cache.TryAdd(Entry(1)));
        first.Submitted();
        for(uint page=2;page<=SurfaceHitRetryCache.Capacity;page++)Assert.True(cache.TryAdd(Entry(page)));
        Assert.False(cache.TryAdd(Entry(99)));Assert.Equal(32,cache.Count);Assert.Contains(first,cache.Entries);
        cache.Remove(first);Assert.True(cache.TryAdd(Entry(99)));Assert.Equal(32,cache.Count);
    }

    /// <summary>Ready texels rotate past pending and unchanged dependencies without a head-of-line blocker.</summary>
    [Fact]
    public void SelectionRotatesAndSkipsSleepingEntries()
    {
        var cache=new SurfaceHitRetryCache();var sleeping=Entry(1);var pending=Entry(2);
        var third=Entry(3);var fourth=Entry(4);
        foreach(var entry in new[]{sleeping,pending,third,fourth})Assert.True(cache.TryAdd(entry));
        sleeping.Submitted();Assert.False(sleeping.Complete([Answer(false)]));pending.Submitted();
        Assert.Same(third,cache.Select());third.Submitted();
        Assert.Same(fourth,cache.Select());fourth.Submitted();Assert.Null(cache.Select());
        Assert.False(third.Complete([Answer(false)]));Assert.Null(cache.Select());
        Assert.True(third.Observe([Dependency(3)]));
        Assert.True(third.Observe([Dependency(3) with{Publication=2}]));Assert.Same(third,cache.Select());
    }

    /// <summary>Removing the selected entry does not skip its next ready sibling after list compaction.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RemovingSelectedEntryPreservesNextTurn(bool prune)
    {
        var cache=new SurfaceHitRetryCache();var first=Entry(1);var second=Entry(2);var third=Entry(3);
        foreach(var entry in new[]{first,second,third})Assert.True(cache.TryAdd(entry));
        Assert.Same(first,cache.Select());
        if(prune)cache.Prune(1,entry=>entry!=first);else cache.Remove(first);
        Assert.Same(second,cache.Select());
    }

    /// <summary>Expired samples release suppression after exactly 256 frames, including signed frame counter rollover.</summary>
    [Theory]
    [InlineData(0)] [InlineData(int.MaxValue-128)]
    public void RetentionExpiryRestoresOrdinaryEligibility(int frame)
    {
        var cache=new SurfaceHitRetryCache();Assert.True(cache.TryAdd(Entry(1,frame:frame)));
        cache.Prune(unchecked(frame+255),_=>true);Assert.Equal(1,cache.Count);
        cache.Prune(unchecked(frame+256),_=>true);Assert.Equal(0,cache.Count);Assert.Null(cache.Select());
    }

    /// <summary>Origin or resource invalidation removes only obsolete entries and reset retires all retained descriptors.</summary>
    [Fact]
    public void LifetimePruningAndResetReleaseStorage()
    {
        var cache=new SurfaceHitRetryCache();Assert.True(cache.TryAdd(Entry(1)));Assert.True(cache.TryAdd(Entry(2)));
        cache.Prune(1,entry=>entry.Request.Page==2);Assert.Equal(2u,Assert.Single(cache.Entries).Request.Page);
        cache.Clear();Assert.Equal(0,cache.Count);Assert.Null(cache.Select());Assert.True(cache.TryAdd(Entry(1)));
    }

    /// <summary>Admission requires a nonempty hit batch within the production ray bound.</summary>
    [Theory]
    [InlineData(0)] [InlineData(65)]
    public void InvalidQueryCountsAreRejected(int count) => Assert.Throws<ArgumentOutOfRangeException>(()=>Entry(1,queries:count));
    #endregion

    #region Fixtures
    /// <summary>Creates a retained texel with immutable query descriptors and no live GPU resource dependency.</summary>
    private static SurfaceHitRetryEntry Entry(uint page,int queries=1,int frame=0) => new(
        new SurfaceFallbackRequest{Page=page,Slot=2,Patch=3,Linear=4,Fraction=new(0,0,0,queries)},
        Enumerable.Repeat(new SurfaceLightingQuery(),queries).ToImmutableArray(),[],new(page,3,1,1),new(null!,1,1,1),frame);

    /// <summary>Creates one captured hit dependency whose publication can change independently of surface identity.</summary>
    private static SurfaceHitDependency Dependency(uint page) => new(page,page,1,1,true,1);

    /// <summary>Supplies a complete or unavailable cache answer while retaining finite radiance.</summary>
    private static SurfaceLightingQuery Answer(bool ready) => new(){Result=new(1,2,3,ready?1:0)};
    #endregion
}
