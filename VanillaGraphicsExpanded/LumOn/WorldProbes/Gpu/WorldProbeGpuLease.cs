using System;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Retains one admission's resident range and validates its original ownership immediately before commit.</summary>
internal sealed class WorldProbeGpuLease : IDisposable
{
    private bool disposed;
    public WorldProbeResidentAnswers Answers { get; }
    public int First { get; }
    public int Count { get; }
    public WorldProbeCommitIdentity Identity { get; }
    public bool IsCurrent => !disposed && Answers.IsValid && Identity.IsCurrent;

    #region Lifetime
    /// <summary>Captures render-thread lifetime checks and retains a disjoint resident answer range.</summary>
    public WorldProbeGpuLease(WorldProbeResidentAnswers answers, int first, int count, Func<bool> validate)
    {
        if (first < 0 || count <= 0 || first > answers.Count - count) throw new ArgumentOutOfRangeException(nameof(first));
        Answers = answers; First = first; Count = count; Identity = new(validate); answers.Retain();
    }

    /// <summary>Releases the range once even when immutable result copies share the lease.</summary>
    public void Dispose()
    { if (disposed) return; disposed = true; Answers.Release(); }
    #endregion
}
