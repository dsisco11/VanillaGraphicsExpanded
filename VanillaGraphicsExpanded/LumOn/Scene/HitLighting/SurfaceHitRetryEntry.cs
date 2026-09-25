using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.LumOn.Scene.Fallback;

namespace VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

/// <summary>Retains one complete geometric sample until its missing hit lighting progresses.</summary>
internal sealed class SurfaceHitRetryEntry
{
    public SurfaceFallbackRequest Request { get; }
    public ImmutableArray<SurfaceLightingQuery> Queries { get; }
    public ImmutableArray<SurfaceFallbackDependency> Chunks { get; }
    public SurfaceFallbackPage Origin { get; }
    public SurfaceFallbackLifetime Lifetime { get; }
    public uint CreatedFrame { get; }
    public bool NeedsQuery { get; private set; } = true;
    public bool Pending { get; set; }
    private ImmutableArray<SurfaceHitDependency> observed;
    private readonly SurfaceHitDependency[] bound;
    private readonly bool[] missing;

    #region Retained sample
    /// <summary>Owns immutable descriptors; an initial query closes any publication race during GPU readback.</summary>
    public SurfaceHitRetryEntry(SurfaceFallbackRequest request, ImmutableArray<SurfaceLightingQuery> queries,
        ImmutableArray<SurfaceFallbackDependency> chunks, SurfaceFallbackPage origin, SurfaceFallbackLifetime lifetime, int frame)
    {
        if (queries.IsDefaultOrEmpty || queries.Length > SurfaceFallbackWorker.MaximumRays)
            throw new ArgumentOutOfRangeException(nameof(queries));
        Request = request; Queries = queries; Chunks = chunks; Origin = origin; Lifetime = lifetime;
        CreatedFrame = unchecked((uint)frame);
        bound = new SurfaceHitDependency[queries.Length]; missing = new bool[queries.Length];
        Array.Fill(missing, true);
    }

    /// <summary>Rejects replaced captured surfaces, but permits an initially missing page to become captured.</summary>
    public bool Validate(ImmutableArray<SurfaceHitDependency> states)
    {
        if (states.Length != Queries.Length) throw new ArgumentException("Mismatched hit dependencies.");
        for (int i = 0; i < states.Length; i++)
        {
            if (bound[i].Captured && !bound[i].SameIdentity(states[i])) return false;
            if (states[i].Captured) bound[i] = states[i];
        }
        return true;
    }

    /// <summary>Wakes only for missing-hit dependencies; unrelated or already resolved lighting cannot cause polling loops.</summary>
    public bool Observe(ImmutableArray<SurfaceHitDependency> states)
    {
        if (!Validate(states)) return false;
        if (!observed.IsDefault)
            for (int i = 0; i < states.Length; i++)
                if (missing[i] && states[i] != observed[i]) NeedsQuery = true;
        observed = states;
        return true;
    }

    /// <summary>Records query ownership; all hits are queried together to avoid mixing lighting generations.</summary>
    public void Submitted() { Pending = true; NeedsQuery = false; }

    /// <summary>Retains only dependency readiness after a lookup; estimates always use a coherent full query batch.</summary>
    public bool Complete(ReadOnlySpan<SurfaceLightingQuery> answers)
    {
        if (answers.Length != Queries.Length) throw new ArgumentException("Mismatched hit answers.");
        Pending = false;
        bool complete = true;
        for (int i = 0; i < answers.Length; i++)
        {
            var value = answers[i].Result;
            missing[i] = value.W != 1 || !float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z);
            complete &= !missing[i];
        }
        return complete;
    }
    #endregion
}
