using System;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Expires captured geometry packets when their owning adapter or synchronous engine borrow ends.</summary>
internal sealed class RHIGeometryBorrow : IDisposable
{
    private int expired;
    private readonly System.Collections.Generic.List<Action> realizations = new();

    #region Public API
    /// <summary>Ends CPU packet admission without disposing or deleting the referenced resource storage.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref expired, 1) != 0) return;
        foreach (var retire in realizations) retire();
        realizations.Clear();
    }
    #endregion

    #region Internal API
    /// <summary>Rejects packets retained beyond their explicitly granted adapter lifetime.</summary>
    internal void RequireLive() => ObjectDisposedException.ThrowIf(Volatile.Read(ref expired) != 0, this);
    /// <summary>Attaches backend cleanup to this interval without transferring any source-buffer ownership.</summary>
    internal void RetireWithBorrow(Action retire)
    {
        RequireLive();
        realizations.Add(retire);
    }
    #endregion
}
