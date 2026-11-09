using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Prevents disabled secondary caches from retaining every shared-scene worker result.</summary>
public sealed class GeometryArtifactCacheTests
{
    #region Public API
    /// <summary>Known-size and unknown-size artifacts are both discarded when retention is disabled.</summary>
    [Fact]
    public void ZeroBudgetDoesNotRetainArtifacts()
    {
        // Arrange
        var cache = new ArtifactCache(0);
        var key = new ArtifactKey(default, 1, "geometry");
        // Act and Assert
        cache.Put(key, new Sized(393216));
        Assert.False(cache.TryGet(key, out _));
        cache.Put(key, new object());
        Assert.False(cache.TryGet(key, out _));
        Assert.Equal(0, cache.BytesInUse);
    }

    /// <summary>Positive budgets retain recent artifacts and evict older payloads normally.</summary>
    [Fact]
    public void PositiveBudgetRemainsBounded()
    {
        // Arrange
        var cache = new ArtifactCache(16);
        var first = new ArtifactKey(default, 1, "geometry");
        var second = new ArtifactKey(default, 2, "geometry");
        // Act and Assert
        cache.Put(first, new Sized(12)); cache.Put(second, new Sized(12));
        Assert.False(cache.TryGet(first, out _));
        Assert.True(cache.TryGet(second, out _));
        Assert.Equal(12, cache.BytesInUse);
    }

    #endregion
    #region Private
    /// <summary>Provides an explicit retained-payload estimate for cache accounting.</summary>
    private sealed record Sized(long EstimatedBytes) : IArtifactSizeInfo;
    #endregion
}
