using System;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Owns one bounded GPU answer allocation until every admission has retired.</summary>
internal sealed class WorldProbeResidentAnswers : IDisposable
{
    private readonly GpuShaderStorageBuffer buffer;
    private int references = 1;
    public int Count { get; }
    public bool IsValid { get; private set; } = true;

    #region Ownership
    /// <summary>Takes ownership of a completed allocation with one initial drain reference.</summary>
    public WorldProbeResidentAnswers(GpuShaderStorageBuffer buffer, int count)
    { this.buffer = buffer; Count = count; }

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
