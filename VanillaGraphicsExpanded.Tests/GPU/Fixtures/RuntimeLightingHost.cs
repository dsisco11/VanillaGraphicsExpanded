using System.Reflection;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Bootstraps the real mod owners and confines private engine-boundary adaptation to the test assembly.</summary>
internal sealed class RuntimeLightingHost : IDisposable
{
    private readonly VgeConfig previousConfig = ConfigModSystem.Config;
    private readonly LumOnModSystem lighting = new();
    private readonly WorldProbeModSystem world;
    private readonly DirectLightingBufferManager direct;
    public LumOnRenderer Renderer => Read<LumOnRenderer>(lighting, "lumOnRenderer");
    public LumOnWorldProbeUpdateRenderer WorldRenderer => Read<LumOnWorldProbeUpdateRenderer>(world, "worldProbeUpdateRenderer");

    #region Production initialization
    /// <summary>Runs production resource creation and provider wiring, then supplies the simulated engine camera/source boundaries.</summary>
    public RuntimeLightingHost(ICoreClientAPI api, SurfaceCacheRuntimeFixture cache, WorldProbeModSystem world,
        Func<LumOnCameraState?> camera)
    {
        this.world = world;
        typeof(ConfigModSystem).GetProperty(nameof(ConfigModSystem.Config))!.SetValue(null, cache.Config);
        int resolution = cache.Config.WorldProbeClipmap.ClipmapResolution;
        world.StartClientSide(api);
        // Small controlled scenes use a reduced runtime topology after normal startup validation.
        cache.Config.WorldProbeClipmap.ClipmapResolution = resolution;
        world.GetClipmapBufferManagerOrNull()!.RequestRecreate("controlled engine scene topology");
        direct = new(api);
        // SetDependencies is the normal main-mod handoff; it executes LumOn's initialization and wiring.
        lighting.SetDependencies(api, cache.Buffers, direct);
        var geometry = Read<TraceGeometryRenderer>(lighting, "traceGeometryRenderer");
        var feedback = Read<LumonSceneFeedbackUpdateRenderer>(lighting, "lumonSceneFeedbackUpdateRenderer");
        var debug = Read<LumOnDebugRenderer>(lighting, "lumOnDebugRenderer");
        foreach (var owner in new object[] { Renderer, WorldRenderer, geometry, feedback, debug })
            Set(owner, "readCamera", camera);
        Set(geometry, "createSource", (Func<TraceGeometryMaterials, ITraceGeometrySource>)cache.CreateSource);
        cache.AttachProduction(geometry, feedback, Read<LumonSceneRelightUpdateRenderer>(lighting, "lumonSceneRelightUpdateRenderer"));
    }

    /// <summary>Reads existing owners for observations without creating production inspection APIs.</summary>
    internal static T Read<T>(object owner, string name) => (T)Field(owner, name).GetValue(owner)!;

    /// <summary>Substitutes an engine boundary only; production provider references and pipeline state remain untouched.</summary>
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);

    /// <summary>Fails immediately if an engine-adapter field changes rather than silently skipping the test boundary.</summary>
    private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);
    #endregion

    #region Lifetime
    /// <summary>Exercises mod-owned teardown before restoring the process-wide configuration.</summary>
    public void Dispose()
    {
        world.Dispose(); lighting.Dispose(); direct.Dispose();
        typeof(ConfigModSystem).GetProperty(nameof(ConfigModSystem.Config))!.SetValue(null, previousConfig);
    }
    #endregion
}
