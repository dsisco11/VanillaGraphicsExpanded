using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Hosts the deletion service whose terminal lifetime is owned by the rendering root.</summary>
public sealed class GpuResourceManagerModSystem : ModSystem
{
    private ICoreClientAPI? capi;
    private GpuResourceManager? renderer;
    private bool rendererRegistered;
    private bool disposed;
    private bool rootOwned;

    #region Public API
    /// <summary>Establishes native deletion before rendering composition can prepare executable resources.</summary>
    public override double ExecuteOrder() => 0.025;

    /// <summary>Loads the GPU service only on the client.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Registers the originating context and publishes the render-thread deletion service.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        Rendering.Integration.EngineRenderContext.RegisterCurrent();

        renderer ??= new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(renderer);

        api.Event.RegisterRenderer(renderer, GpuResourceManager.Stage, "vge_gpu_resource_manager");
        rendererRegistered = true;

        api.Logger.Notification(
            "[VGE] GpuResourceManager registered ({0} @ {1})",
            GpuResourceManager.Stage,
            renderer.RenderOrder);
    }

    /// <summary>Allows standalone cleanup; root-owned managers await the root's ordered shutdown.</summary>
    public override void Dispose()
    {
        if (!rootOwned) ShutdownRenderingResources();
    }

    #endregion

    #region Internal API
    /// <summary>Transfers teardown authority to the rendering composition root.</summary>
    internal void AdoptByRenderingRoot() => rootOwned = true;

    /// <summary>Drains shared resources after the root has retired feature borrowers and programs.</summary>
    internal void ShutdownRenderingResources()
    {
        if (disposed) return;
        disposed = true;
        base.Dispose();
        var failures = new System.Collections.Generic.List<System.Exception>();
        if (capi != null && rendererRegistered && renderer is not null)
            TryCleanup(() => capi.Event.UnregisterRenderer(renderer, GpuResourceManager.Stage), failures);
        // Keep the deletion service available until every shared owner has retired.
        TryCleanup(GpuSamplers.Dispose, failures);
        TryCleanup(TextureStreamingSystem.Dispose, failures);
        TryCleanup(() => renderer?.Dispose(), failures);
        renderer = null; rendererRegistered = false;
        TryCleanup(GpuResourceManagerSystem.Shutdown, failures);
        capi = null;
        if (failures.Count != 0) throw new System.AggregateException("VGE GPU manager cleanup failed.", failures);
    }

    #endregion

    #region Private
    /// <summary>Retains an independent cleanup error while permitting the final context drain and service closure.</summary>
    private static void TryCleanup(System.Action cleanup, System.Collections.Generic.List<System.Exception> failures)
    {
        try { cleanup(); } catch (System.Exception error) { failures.Add(error); }
    }
    #endregion
}
