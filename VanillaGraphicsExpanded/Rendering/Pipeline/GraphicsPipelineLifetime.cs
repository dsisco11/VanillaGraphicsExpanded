using System;

namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Bounds borrowed prepared realizations to one renderer on its rendering thread and live context.</summary>
internal sealed class GraphicsPipelineLifetime : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private bool disposed;

    #region Public API
    /// <summary>Rejects renderer teardown and cross-thread use without tracking context replacements.</summary>
    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (thread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Graphics pipelines belong to their rendering thread.");
    }

    /// <summary>Invalidates every borrowed realization at renderer teardown without acquiring resource ownership.</summary>
    public void Dispose() => disposed = true;
    #endregion
}
