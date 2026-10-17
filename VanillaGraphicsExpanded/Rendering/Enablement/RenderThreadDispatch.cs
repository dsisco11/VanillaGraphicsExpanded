using System;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Enablement;

/// <summary>Serializes change notifications onto the owning rendering thread and cancels pending work at disposal.</summary>
internal sealed class RenderThreadDispatch(ICoreClientAPI api) : IDisposable
{
    private readonly int thread = Environment.CurrentManagedThreadId;
    private volatile bool disposed;

    #region Public API
    /// <summary>Applies render-thread changes immediately and marshals other notifications to the native main-thread queue.</summary>
    internal void Run(Action action)
    {
        if (disposed) return;
        if (Environment.CurrentManagedThreadId == thread) action();
        else api.Event.EnqueueMainThreadTask(() => { if (!disposed) action(); }, "vge_enablement");
    }

    /// <summary>Prevents queued and future notifications from entering a retired owner.</summary>
    public void Dispose() => disposed = true;
    #endregion
}
