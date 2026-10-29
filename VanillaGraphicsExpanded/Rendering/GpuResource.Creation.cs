using System;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Protects native allocations until an owning wrapper has completed construction.</summary>
public abstract partial class GpuResource
{
    #region Internal API
    /// <summary>Transfers a newly created name to a wrapper, retiring it in its original context if construction fails.</summary>
    internal static T CompleteOwnedCreation<T>(GpuResourceKind kind, nint id, Func<T> construct)
    {
        var origin = RenderContextRegistry.Current();
        var manager = RHIResourceManagerSystem.CurrentManager;
        try { return construct(); }
        catch
        {
            // The allocation has no returned owner. Cleanup uses the captured namespace and
            // preserves the construction exception even when shutdown prevents native cleanup.
            try { RetireResource(kind, id, origin, manager); }
            catch { }
            throw;
        }
    }
    #endregion
}
