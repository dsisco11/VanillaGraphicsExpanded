using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Owns one bounded GPU answer allocation until every admission has retired.</summary>
internal sealed class WorldProbeResidentAnswers : IDisposable
{
    private readonly GpuShaderStorageBuffer buffer;
    private readonly ImmutableArray<WorldProbeTraceAnswerGpu> completions;
    private int references = 1;
    /// <summary>Exposes only initialized resident records for retained submission.</summary>
    internal GpuStorageBufferBinding Binding => IsValid ? new(buffer, 0, Count * 80) : throw new ObjectDisposedException(nameof(WorldProbeResidentAnswers));
    public int Count { get; }
    public bool IsValid { get; private set; } = true;

    #region Ownership
    /// <summary>Takes ownership of a completed allocation with one initial drain reference.</summary>
    public WorldProbeResidentAnswers(GpuShaderStorageBuffer buffer, ImmutableArray<WorldProbeTraceAnswerGpu> completions)
    {
        if (completions.IsDefaultOrEmpty) throw new ArgumentException("Resident lighting requires completed admission metadata.", nameof(completions));
        this.buffer = buffer; this.completions = completions; Count = completions.Length;
    }

    /// <summary>Requires the existing compact GPU completion to have certified this immutable resident direction.</summary>
    internal bool DirectionalReady(int index) => IsValid && (uint)index < (uint)Count
        && completions[index].Outcome == 1 && completions[index].Hit.Result.W == 1;

    /// <summary>Retains the allocation for one independently completed admission.</summary>
    public void Retain()
    { if (!IsValid) throw new ObjectDisposedException(nameof(WorldProbeResidentAnswers)); references++; }

    /// <summary>Retires a drain or admission reference on the render thread.</summary>
    public void Release()
    { if (IsValid && --references == 0) Dispose(); }

    /// <summary>Binds initialized resident answers for the validated atlas commit.</summary>
    public void Bind(int binding)
    { if (!IsValid) throw new ObjectDisposedException(nameof(WorldProbeResidentAnswers)); buffer.BindRange(binding, 0, Count * 80); }

    /// <summary>Invalidates every borrowed admission before releasing the GPU allocation.</summary>
    public void Dispose()
    { if (!IsValid) return; IsValid = false; buffer.Dispose(); }
    #endregion
}
