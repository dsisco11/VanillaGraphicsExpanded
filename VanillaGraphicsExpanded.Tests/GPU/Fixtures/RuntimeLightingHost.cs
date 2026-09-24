using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Starts the production mod owners with controlled engine dependencies and observes registered renderers.</summary>
internal sealed class RuntimeLightingHost : IDisposable
{
    private readonly LumOnModSystem lighting;
    private readonly WorldProbeModSystem world;
    private readonly DirectLightingBufferManager direct;
    private readonly DirectLightingRenderer? directRenderer;
    private readonly PBRCompositeRenderer? compositeRenderer;
    public DirectLightingBufferManager Direct => direct;
    public LumOnWorldProbeUpdateRenderer WorldRenderer { get; }
    public LumOnBufferManager Screen => lighting.GetLumOnBufferManagerOrNull()!;

    #region Production initialization
    /// <summary>Runs normal mod wiring; camera and source dependencies are provided before owner construction.</summary>
    public RuntimeLightingHost(ICoreClientAPI api, SurfaceCacheRuntimeFixture cache, WorldProbeModSystem world,
        Func<LumOnCameraState?> camera, bool pbrComposition = false)
    {
        this.world = world;
        lighting = new(() => cache.Config, _ => camera(), (_, materials) => cache.CreateSource(materials));
        int resolution = cache.Config.WorldProbeClipmap.ClipmapResolution;
        world.StartClientSide(api);
        // The bounded authored world uses a smaller runtime topology than the game's configuration minimum.
        cache.Config.WorldProbeClipmap.ClipmapResolution = resolution;
        world.GetClipmapBufferManagerOrNull()!.RequestRecreate("controlled engine scene topology");
        direct = new(api);
        lighting.SetDependencies(api, cache.Buffers, direct);
        if (pbrComposition)
        {
            directRenderer = new(api, cache.Buffers, direct, () =>
            {
                var current = camera() ?? throw new InvalidOperationException("The controlled scene requires a camera.");
                return new(current.CameraX, current.CameraY, current.CameraZ);
            });
            compositeRenderer = new(api, cache.Buffers, direct, cache.Config, Screen);
        }
        var registered = cache.Events.Registrations.Select(entry => entry.Renderer).Distinct().ToArray();
        WorldRenderer = Assert.Single(registered.OfType<LumOnWorldProbeUpdateRenderer>());
        cache.AttachProduction(Assert.Single(registered.OfType<TraceGeometryRenderer>()),
            Assert.Single(registered.OfType<LumonSceneFeedbackUpdateRenderer>()),
            Assert.Single(registered.OfType<LumonSceneRelightUpdateRenderer>()));
    }
    #endregion

    #region Lifetime
    /// <summary>Exercises normal mod disposal without changing process-wide configuration.</summary>
    public void Dispose()
    {
        compositeRenderer?.Dispose(); directRenderer?.Dispose();
        world.Dispose(); lighting.Dispose(); direct.Dispose();
    }
    #endregion
}
