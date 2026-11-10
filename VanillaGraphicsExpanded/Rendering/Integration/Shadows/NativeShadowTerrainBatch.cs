using System;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Owns only temporary group borrows for one bounded traversal of the native category table.</summary>
internal sealed class NativeShadowTerrainBatch : IDisposable
{
    private bool disposed;

    #region Public API
    internal ImmutableArray<NativeShadowTerrainGroup> Groups { get; }
    internal int GroupVisits { get; }
    internal bool Complete { get; }
    internal string? Failure { get; }

    /// <summary>Freezes either all supplied groups or an explicitly incomplete batch.</summary>
    internal NativeShadowTerrainBatch(ImmutableArray<NativeShadowTerrainGroup> groups, int visits, string? failure)
    {
        Groups = groups; GroupVisits = visits; Failure = failure; Complete = failure is null;
    }

    /// <summary>Requires every borrow to remain live; an empty incomplete batch is never accepted.</summary>
    internal bool IsCurrent
    {
        get
        {
            if (disposed || !Complete) return false;
            foreach (var group in Groups) if (!group.IsCurrent) return false;
            return true;
        }
    }

    /// <summary>Releases group borrows together at the submission boundary.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var group in Groups) group.Dispose();
    }
    #endregion
}
