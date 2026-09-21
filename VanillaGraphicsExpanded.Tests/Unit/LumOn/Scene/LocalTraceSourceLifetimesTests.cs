using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Source instance lifetimes invalidate artifact-cache revisions independently of chunk dirty events.</summary>
public sealed class LocalTraceSourceLifetimesTests
{
    #region Identity observations
    /// <summary>Initial load, replacement, unload, and reload each advance dependency identity while stable observations do not.</summary>
    [Fact]
    public void ChangedLoadedIdentityAdvancesArtifactVersion()
    {
        var versions=new LumonSceneTraceSceneChunkVersionProvider();
        var lifetimes=new LocalTraceSourceLifetimes(versions);
        var key=ChunkKey.FromChunkCoords(-1,2,3);
        var first=new object();var second=new object();
        Assert.True(lifetimes.Observe(key,first));int initial=versions.GetCurrentVersion(key);
        Assert.True(initial>0);lifetimes.Observe(key,first);Assert.Equal(initial,versions.GetCurrentVersion(key));
        lifetimes.Observe(key,second);Assert.Equal(initial+1,versions.GetCurrentVersion(key));
        Assert.False(lifetimes.Observe(key,null));Assert.Equal(initial+2,versions.GetCurrentVersion(key));
        lifetimes.Observe(key,null);Assert.Equal(initial+2,versions.GetCurrentVersion(key));
        lifetimes.Observe(key,second);Assert.Equal(initial+3,versions.GetCurrentVersion(key));
    }
    /// <summary>Leaving the retained window forces a new source lifetime on revisit, even for the same object.</summary>
    [Fact]
    public void WindowReentryAdvancesArtifactVersion()
    {
        var versions=new LumonSceneTraceSceneChunkVersionProvider();
        var lifetimes=new LocalTraceSourceLifetimes(versions);
        var key=ChunkKey.FromChunkCoords(0,0,0);var identity=new object();
        lifetimes.Observe(key,identity);int old=versions.GetCurrentVersion(key);
        lifetimes.Retain(new PartitionCellRange(new(0,0,0),new(2,2,2)));
        lifetimes.Observe(key,identity);Assert.Equal(old,versions.GetCurrentVersion(key));
        lifetimes.Retain(new PartitionCellRange(new(2,0,0),new(4,2,2)));
        lifetimes.Observe(key,identity);Assert.True(versions.GetCurrentVersion(key)>old);
    }
    #endregion
}
