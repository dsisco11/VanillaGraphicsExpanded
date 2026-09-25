using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Fixtures.NearField;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks configurable geometry work against retained publication and storage invariants.</summary>
public sealed class TraceGeometryWorkBudgetTests
{
    #region Configuration
    /// <summary>Persisted coverage requests round upward to supported bounded tiers.</summary>
    [Theory]
    [InlineData(-1,16)] [InlineData(17,32)] [InlineData(65,128)]
    [InlineData(129,192)] [InlineData(192,192)] [InlineData(193,256)] [InlineData(999,256)]
    public void CoverageSanitizesToSupportedTier(int requested, int expected)
    {
        var config = new VgeConfig();
        config.LumOn.LumonScene.TraceScene.ClipmapResolution = requested;
        config.Sanitize();
        Assert.Equal(expected, config.LumOn.LumonScene.TraceScene.ClipmapResolution);
    }

    /// <summary>Defaults increase useful coverage while preserving previous per-frame throughput limits.</summary>
    [Fact]
    public void DefaultsRetainBoundedWork()
    {
        var config = new VgeConfig();
        Assert.Equal(192, config.LumOn.LumonScene.TraceScene.ClipmapResolution);
        Assert.Equal(new TraceGeometryWorkBudget(2,16,8L << 20), TraceGeometryWorkBudget.From(config.LumOn.LumonScene.TraceScene));
    }

    /// <summary>Both direct use and persisted sanitization keep work within explicit safety bounds.</summary>
    [Theory]
    [InlineData(-1,-1,-1,0,0,0)] [InlineData(99,99,99999999,8,32,8388608)]
    [InlineData(1,1,1,1,1,65536)] [InlineData(0,0,0,0,0,0)]
    public void WorkLimitsAreClamped(int source,int cells,long bytes,int expectedSource,int expectedCells,long expectedBytes)
    {
        var config = new VgeConfig();
        var trace = config.LumOn.LumonScene.TraceScene;
        trace.SourceChunksPerFrame=source; trace.CellUploadsPerFrame=cells; trace.UploadBytesPerFrame=bytes;
        var expected = new TraceGeometryWorkBudget(expectedSource,expectedCells,expectedBytes);
        Assert.Equal(expected,TraceGeometryWorkBudget.From(trace));
        config.Sanitize();
        Assert.Equal(expected,TraceGeometryWorkBudget.From(trace));
        Assert.Equal(expectedSource,trace.SourceChunksPerFrame);
        Assert.Equal(expectedCells,trace.CellUploadsPerFrame);
        Assert.Equal(expectedBytes,trace.UploadBytesPerFrame);
    }
    #endregion

    #region Publication budgets
    /// <summary>Single-cell throughput gives both overlapping consumer classes progress before the broad window finishes.</summary>
    [Theory]
    [InlineData(1,8388608)] [InlineData(16,65536)]
    public void NarrowPublicationBudgetDoesNotStarveEitherConsumer(int cells,long bytes)
    {
        var coverage=TraceGeometryCoverage.Plan(new(0,128,0),true,128,256);
        using var fixture=new TraceGeometryFixture(coverage.Resolution);
        for(int frame=0;frame<100 && fixture.Backend.Publications.Count<16;frame++)
        { fixture.Frame(coverage,new(2,cells,bytes));fixture.Complete(); }
        Assert.True(fixture.Backend.Publications.Count>=16);
        Assert.Contains(fixture.Backend.Publications.Take(16),key=>coverage.IsNear(key.Coordinate));
        Assert.Contains(fixture.Backend.Publications.Take(16),key=>!coverage.IsNear(key.Coordinate));
    }

    /// <summary>Texture accounting includes all voxel companions, readiness and fixed shared lookup payloads.</summary>
    [Theory]
    [InlineData(208)] [InlineData(272)]
    public void TextureAccountingBoundsExpandedStorage(int physical)
    {
        long cells=physical >> 4;
        long expected=(long)physical*physical*physical*12+cells*cells*cells+2097152+644;
        Assert.Equal(expected,TraceGeometryCoverage.TextureBytes(physical));
        Assert.Throws<ArgumentOutOfRangeException>(()=>TraceGeometryCoverage.TextureBytes(288));
    }

    /// <summary>Tables and cells share a byte ceiling, and dynamic pauses retain already published identity.</summary>
    [Fact]
    public void SmallBudgetProgressesAndPausesWithoutDiscardingPublication()
    {
        using var fixture = new TraceGeometryFixture();
        var coverage=TraceGeometryCoverage.Plan(new(1,40,1),true,null,256);
        var budget=new TraceGeometryWorkBudget(1,1,1L << 16);
        for(int frame=0;frame<120;frame++)
        {
            int requests=fixture.Requests.Count, publications=fixture.Backend.Publications.Count;
            long bytes=fixture.Backend.UploadedBytes;
            fixture.Frame(coverage,budget); fixture.Complete();
            Assert.InRange(fixture.Requests.Count-requests,0,1);
            Assert.InRange(fixture.Backend.Publications.Count-publications,0,1);
            Assert.InRange(fixture.Backend.UploadedBytes-bytes,0,1L << 16);
        }
        Assert.Equal(27,fixture.Backend.Ready.Count);
        var identities=fixture.Backend.Ready.ToArray();
        foreach(var pause in new[]{new TraceGeometryWorkBudget(8,0,8L << 20),new TraceGeometryWorkBudget(8,32,0)})
        {
            long bytes=fixture.Backend.UploadedBytes;
            for(int frame=0;frame<4;frame++) fixture.Frame(coverage,pause);
            Assert.Equal(bytes,fixture.Backend.UploadedBytes);
            foreach(var pair in identities) Assert.Same(pair.Value,fixture.Backend.Ready[pair.Key]);
        }
        fixture.Frame(coverage,TraceGeometryWorkBudget.Default);
        Assert.Equal(27,fixture.Backend.Publications.Count);
    }

    /// <summary>Stopping new source requests still permits publication from a completed retained snapshot.</summary>
    [Fact]
    public void ZeroSourceBudgetPublishesCachedCells()
    {
        using var fixture=new TraceGeometryFixture();
        var coverage=TraceGeometryCoverage.Plan(new(1,40,1),true,null,256);
        fixture.Frame(coverage,new(1,16,8L << 20)); fixture.Complete();
        Assert.Single(fixture.Requests);
        for(int frame=0;frame<10;frame++) fixture.Frame(coverage,new(0,16,8L << 20));
        Assert.Single(fixture.Requests);
        Assert.NotEmpty(fixture.Backend.Ready);
        for(int frame=0;frame<20;frame++) { fixture.Frame(coverage); fixture.Complete(); }
        Assert.Equal(27,fixture.Backend.Ready.Count);
    }

    /// <summary>Larger logical windows converge under the same source, snapshot and publication ceilings.</summary>
    [Theory]
    [InlineData(192)] [InlineData(256)]
    public void ExpandedCoverageConvergesWithBoundedStorage(int resolution)
    {
        var coverage=TraceGeometryCoverage.Plan(new(-1,128,-1),true,resolution,256);
        using var fixture=new TraceGeometryFixture(coverage.Resolution);
        int expected=new PartitionLayout(new(16,16,16)).Intersecting(coverage.Clip(coverage.Surface!.Value)).Count();
        for(int frame=0;frame<700 && fixture.Backend.Ready.Count<expected;frame++)
        {
            int requests=fixture.Requests.Count, publications=fixture.Backend.Publications.Count;
            fixture.Frame(coverage);fixture.Complete();
            Assert.InRange(fixture.Requests.Count-requests,0,2);
            Assert.InRange(fixture.Backend.Publications.Count-publications,0,16);
            Assert.InRange(fixture.Cache.InFlight,0,8);
            Assert.InRange(fixture.Cache.SnapshotBytes,0,8L * 393216);
        }
        Assert.Equal(expected,fixture.Backend.Ready.Count);
        Assert.Equal(expected,fixture.Backend.Publications.Count);
        Assert.Equal(fixture.Requests.Count,fixture.Requests.Select(r=>(r.Key,r.Version)).Distinct().Count());
    }
    #endregion
}
