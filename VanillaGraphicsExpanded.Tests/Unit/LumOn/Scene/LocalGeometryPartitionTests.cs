using System.Buffers;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Tests.Fixtures.LocalTracing;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Verifies coalesced chunks, dependency lifetimes, and independent publication cells.</summary>
public sealed class LocalGeometryPartitionTests
{
    #region Snapshot ownership and sharing
    /// <summary>Logical coordinates cannot alias the packed source chunk key beyond its signed range.</summary>
    [Fact]
    public void SourceChunkRejectsPackedCoordinateOverflow()
    {
        const long limit = 1L << 21;
        var positive = LocalGeometryPartition.SourceChunk(new(limit-1,0,0));
        positive.Decode(out int x,out _,out _); Assert.Equal((int)(limit/2-1),x);
        var negative = LocalGeometryPartition.SourceChunk(new(-limit,0,0));
        negative.Decode(out x,out _,out _); Assert.Equal((int)(-limit/2),x);
        Assert.Throws<ArgumentOutOfRangeException>(()=>LocalGeometryPartition.SourceChunk(new(limit,0,0)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>LocalGeometryPartition.SourceChunk(new(-limit-1,0,0)));
    }

    /// <summary>All eight siblings use one source read and retain independent ready cells.</summary>
    [Fact]
    public void EightSubcellsShareOneSourceRead()
    {
        using var f = new LocalGeometryFixture(); f.Frame(0);
        Assert.Equal(1,f.Cache.SourceReads); Assert.Equal(8,f.Published.Count);
        Assert.All(f.Coordinator.Cells(f.Instance), c=>Assert.True(c.Ready));
    }
    /// <summary>Changing a source chunk invalidates every resident dependent before its replacement arrives.</summary>
    [Fact]
    public void DirtyChunkInvalidatesEightDependents()
    {
        using var f = new LocalGeometryFixture(); f.Frame(0);
        f.Version++; f.Deferred=true; f.Frame(1);
        Assert.Empty(f.Published); Assert.All(f.Coordinator.Cells(f.Instance), c=>Assert.False(c.Ready));
        Assert.Single(f.Pending); f.Complete(0); f.Frame(2);
        Assert.Equal(8,f.Published.Count); Assert.Equal(2,f.Cache.SourceReads);
    }
    /// <summary>Missing chunks retry without empty publication; supported snapshots classify unsupported cells once.</summary>
    [Fact]
    public void MissingAndUnsupportedRemainDistinct()
    {
        using var f = new LocalGeometryFixture { Available=false }; f.Frame(0);
        Assert.Empty(f.Published); Assert.Equal(0,f.Cache.SourceReads);
        Assert.All(f.Coordinator.Cells(f.Instance),c=>Assert.Equal(PartitionContentStatus.MissingDependencies,c.ContentStatus));
        f.Available=true; f.Source[0]=default; f.Frame(1);
        Assert.Equal(8,f.Published.Count);
        Assert.Equal(PartitionContentStatus.Unsupported,f.Coordinator.Cells(f.Instance).Single(c=>c.Key.Coordinate==new PartitionCoordinate(0,0,0)).ContentStatus);
        f.Frame(2); Assert.Equal(1,f.Cache.SourceReads);
    }
    /// <summary>Owned chunk copies survive pooled-buffer reuse and extract negative coordinates with X/Z/Y ordering.</summary>
    [Fact]
    public void SnapshotCopiesPooledInputAndExtractsCorrectSubcell()
    {
        var buffer=ArrayPool<LocalTraceSourceCell>.Shared.Rent(32768);
        Array.Fill(buffer,new LocalTraceSourceCell(1,default));
        var value=new LocalTraceSourceCell(6,new Vector4(.1f,.2f,.3f,.4f));
        buffer[((3+16)*32+5+16)*32+7+16]=value;
        var snapshot=new LocalTraceChunkSnapshot(ChunkKey.FromChunkCoords(-1,-1,-1),7,buffer.AsSpan(0,32768));
        Assert.Equal(32768L * 20, snapshot.EstimatedBytes);
        Array.Clear(buffer); ArrayPool<LocalTraceSourceCell>.Shared.Return(buffer);
        var content=snapshot.Extract(new(-1,-1,-1));
        Assert.Equal(value,content.Cells[(3*16+5)*16+7]); Assert.False(content.Unsupported);
        Assert.Equal(ChunkKey.FromChunkCoords(-1,-1,-1),LocalGeometryPartition.SourceChunk(new(-1,-1,-1)));
        Assert.Equal(ChunkKey.FromChunkCoords(-2,0,1),LocalGeometryPartition.SourceChunk(new(-3,1,2)));
        Assert.Throws<ArgumentException>(() => snapshot.Extract(new(0,0,0)));
    }

    /// <summary>Snapshot payload bytes participate in ordinary artifact-cache eviction.</summary>
    [Fact]
    public void SnapshotMemoryIsAccountedAndEvicted()
    {
        var first = new LocalTraceChunkSnapshot(default,1,new LocalTraceSourceCell[32768]);
        var secondKey = ChunkKey.FromChunkCoords(1,0,0);
        var second = new LocalTraceChunkSnapshot(secondKey,1,new LocalTraceSourceCell[32768]);
        var cache = new ArtifactCache(first.EstimatedBytes);
        var a = new ArtifactKey(default,1,"local"); var b = new ArtifactKey(secondKey,1,"local");
        cache.Put(a,first); cache.Put(b,second);
        Assert.False(cache.TryGet(a,out _)); Assert.True(cache.TryGet(b,out _));
        Assert.Equal(second.EstimatedBytes,cache.BytesInUse);
    }
    #endregion

    #region Source budgets and obsolete work
    /// <summary>A loader result with the wrong source identity cannot become current content.</summary>
    [Fact]
    public void WrongSourceIdentityIsRejected()
    {
        using var cache = new LocalTraceChunkCache((key,version,cancel) => Task.FromResult<LocalTraceChunkSnapshot?>(
            new(ChunkKey.FromChunkCoords(1,0,0),version,new LocalTraceSourceCell[32768])), _=>1, _=>true);
        cache.BeginFrame(new(new(0,0,0),new(2,2,2)));
        Assert.False(cache.TryGet(default,out _));
        Assert.False(cache.TryGet(default,out _));
    }

    /// <summary>Disjoint requests outside the contiguous backend remain explicitly unsupported without source reads.</summary>
    [Fact]
    public void OutsideBackendEnvelopeIsReportedWithoutCapture()
    {
        using var f = new LocalGeometryFixture();
        f.Coordinator.RemoveSource(f.Instance,1);
        f.Coordinator.SetSource(new(2,f.Instance,"test",new(160,0,0),new(new(160,0,0),new(161,1,1))));
        f.Frame(0);
        Assert.Equal(1,f.Provider.UnsupportedCellCount);
        Assert.Equal(0,f.Cache.SourceReads);
        Assert.Empty(f.Published);
    }

    /// <summary>Source unload hides every dependent and reload requires a fresh snapshot even at the same revision.</summary>
    [Fact]
    public void SourceUnloadReloadDoesNotReuseOldSnapshot()
    {
        using var f = new LocalGeometryFixture(); f.Frame(0);
        f.Available = false; f.Frame(1);
        Assert.Empty(f.Published);
        f.Source[0] = new(6, new Vector4(.5f));
        f.Available = true; f.Frame(2);
        Assert.Equal(2, f.Cache.SourceReads);
        Assert.Equal(6u, f.Published.Single(x => x.Key.Coordinate == new PartitionCoordinate(0,0,0)).Value[0].Geometry);
    }

    /// <summary>Cancelled source captures keep their in-flight credit until acknowledgement and cannot publish old revisions.</summary>
    [Fact]
    public void OldRevisionAndCancelledWorkersCannotEscapeBudget()
    {
        using var f=new LocalGeometryFixture(maximumInFlight:1){Deferred=true};
        f.Frame(0); Assert.Single(f.Pending);
        f.Version++; f.Frame(1);
        Assert.True(f.Pending[0].Cancellation.IsCancellationRequested); Assert.Single(f.Pending);
        f.Complete(0); f.Frame(2); Assert.Equal(2,f.Pending.Count); Assert.Empty(f.Published);
        f.Complete(1); f.Frame(3); Assert.Equal(8,f.Published.Count);
    }
    /// <summary>Source capture budget is separate from ready snapshot reuse and resets only at frame boundaries.</summary>
    [Fact]
    public void CaptureBudgetBoundsDistinctChunkReads()
    {
        using var f=new LocalGeometryFixture(capturesPerFrame:1);
        f.Cache.BeginFrame(new(new(0,0,0),new(8,2,2)));
        Assert.True(f.Cache.TryGet(ChunkKey.FromChunkCoords(0,0,0),out var first));
        Assert.True(f.Cache.TryGet(ChunkKey.FromChunkCoords(0,0,0),out var again)); Assert.Same(first,again);
        Assert.False(f.Cache.TryGet(ChunkKey.FromChunkCoords(1,0,0),out _));
        f.Cache.BeginFrame(new(new(0,0,0),new(8,2,2)));
        Assert.True(f.Cache.TryGet(ChunkKey.FromChunkCoords(1,0,0),out _)); Assert.Equal(2,f.Cache.SourceReads);
    }
    /// <summary>Departed window work cancels while its late result cannot refill the new window.</summary>
    [Fact]
    public void WindowDepartureRetainsCancelledCaptureCredit()
    {
        using var f=new LocalGeometryFixture(maximumInFlight:1){Deferred=true};
        f.Cache.BeginFrame(f.Window); Assert.False(f.Cache.TryGet(default,out _));
        f.Cache.BeginFrame(new(new(4,0,0),new(6,2,2)));
        Assert.True(f.Pending[0].Cancellation.IsCancellationRequested);
        Assert.False(f.Cache.TryGet(ChunkKey.FromChunkCoords(2,0,0),out _)); Assert.Single(f.Pending);
        f.Complete(0);f.Cache.BeginFrame(new(new(4,0,0),new(6,2,2)));
        Assert.False(f.Cache.TryGet(ChunkKey.FromChunkCoords(2,0,0),out _));Assert.Equal(2,f.Pending.Count);
    }
    #endregion
}
