using System;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Replaces feature-owned compute executables without retiring retained input storage.</summary>
internal abstract partial class GpuComputeProgram
{
    #region Internal API
    /// <summary>Admits replacement only after all current and suspended uses end.</summary>
    internal void RequireReloadBoundary() { lifetime.RequireOutsidePublication(); lifetime.RequireUnborrowed(); }
    /// <summary>Withdraws readiness without retiring retained inputs or the old allocation.</summary>
    internal void InvalidateAssets()
    {
        RequireReloadBoundary();
        if (lifetime.IsRetired || rebuild is null) return;
        assetGeneration++;
        reloadRequired = true;
        reloadFailed = false;
        reloadFailure = null;
    }

    #endregion
    #region Private
    /// <summary>Prepares the initial executable or atomically replaces invalidated assets while retaining authored inputs.</summary>
    bool IGpuProgram.PrepareExecutable()
    {
        if (renderingLifetime is { IsClosed: true } or { Reloading: true }) return false;
        if (!reloadRequired)
        {
            ulong preparingAssets = assetGeneration;
            bool ready = pipeline.EnsureReady();
            if (lifetime.IsRetired || preparingAssets != assetGeneration) return false;
            if (ready && executableRevision == 0) executableRevision = 1;
            return ready;
        }
        if (reloadFailed || rebuild is null) return false;
        RequireReloadBoundary();
        var context = Integration.RenderContextRegistry.Current();
        ulong assets = assetGeneration;
        GpuComputePipeline? candidate;
        try { candidate = rebuild(); }
        catch (Exception error)
        {
            reloadFailure = error.Message;
            if (assets == assetGeneration && !lifetime.IsRetired) reloadFailed = true;
            return false;
        }
        if (candidate is null)
        {
            if (assets == assetGeneration && !lifetime.IsRetired) reloadFailed = true;
            return false;
        }
        if (lifetime.IsRetired || renderingLifetime is { IsClosed: true } || assets != assetGeneration || context != Integration.RenderContextRegistry.Current()) { candidate.Dispose(); return false; }
        // Publish executable, interface and revision together; retained generated values and CPU blocks remain on this wrapper.
        var previous = pipeline;
        pipeline = candidate;
        lifetime.NativeExecutable = candidate;
        executableRevision++;
        reloadRequired = false;
        previous.Dispose();
        return true;
    }

    #endregion

}
