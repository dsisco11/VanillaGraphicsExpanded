using System;
using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Schedules background atmosphere builds and publishes matching sky and lighting inputs before scene rendering.</summary>
public sealed class AtmosphereModSystem : ModSystem, IRenderer
{
    private System.IDisposable? reloadRegistration;
    private ICoreClientAPI? api;
    private AtmosphereBackend? computation;
    private AtmosphereSeasonInputs seasonInputs = new();
    private AtmosphereTextureSet? textures, stagingTextures;
    private AtmosphereAmbientPublication? ambientPublication;
    private bool updateFailureReported;
    private CelestialSourceLifetime? celestial;
    private AtmosphereLighting? pendingPublication;
    private (nint Handle, long Generation) context;
    /// <summary>Returns the source committed with matching visible and incident atmospheric illumination.</summary>
    internal CelestialResourceSet? CelestialSource => celestial?.Completed;
    internal static AtmosphereLighting? Lighting { get; private set; }
    internal static int SkyTextureId { get; private set; }
    /// <summary>Lends the sky lookup from the coherently installed atmospheric generation.</summary>
    internal static Rendering.GpuTextureRef SkyTexture { get; private set; }
    internal static int AerialRadianceTextureId { get; private set; }
    internal static int AerialAttenuationTextureId { get; private set; }
    /// <summary>Returns the borrowed radiance volume from the published atmosphere generation.</summary>
    internal static DynamicTexture3D? AerialRadianceTexture { get; private set; }
    /// <summary>Returns the borrowed attenuation volume from the published atmosphere generation.</summary>
    internal static DynamicTexture3D? AerialAttenuationTexture { get; private set; }
    /// <summary>Returns borrowed observer-to-space transmission published atomically with sky and aerial transport.</summary>
    internal static DynamicTexture2D? ExternalTransmissionTexture { get; private set; }
    public double RenderOrder => -.5;
    public int RenderRange => 1;

    #region Lifecycle
    /// <summary>Atmospheric rendering is a client-only service independent of LumOn.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    /// <summary>Registers background completion publication before sky and lighting consumers run.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        ambientPublication = new(api);
        computation = new(api);
        celestial = new(api);
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_atmosphere");
        api.Event.LeaveWorld += Reset;
        api.Event.BlockTexturesLoaded += StartCelestialLoading;
        api.Event.LevelFinalize += FinishCelestialLoading;
        reloadRegistration = VanillaGraphicsExpanded.Rendering.Integration.EngineShaderReload.Register(api, Reload);
    }

    /// <summary>Releases the last world's snapshot and owned GPU texture.</summary>
    private void Reset()
    {
        updateFailureReported = false;
        pendingPublication = null;
        context = default;
        ambientPublication?.Dispose();
        AerialRadianceTexture = null; AerialAttenuationTexture = null;
        ExternalTransmissionTexture = null;
        Lighting = null; SkyTextureId = 0; SkyTexture = default; AerialRadianceTextureId = 0; AerialAttenuationTextureId = 0;
        seasonInputs = new();
        Rendering.Integration.RenderingCleanup.Run(
            () => { var owner = celestial; celestial = null; owner?.Dispose(); },
            () => { var owner = computation; computation = null; owner?.Dispose(); },
            () => { var owner = textures; textures = null; owner?.Dispose(); },
            () => { var owner = stagingTextures; stagingTextures = null; owner?.Dispose(); });
        computation = api is null ? null : new(api);
        celestial = api is null ? null : new(api);
    }

    /// <summary>Invalidates pending shader generations while keeping the last complete display snapshot.</summary>
    private bool Reload()
    {
        // Completed textures are independent of the compute executable. Keep every consumer on
        // the same publication until replacement transport and uploads complete successfully.
        computation?.Reload();
        pendingPublication = null;
        celestial?.Reload((int)ConfigModSystem.Config.Atmosphere.SkyLutQuality);
        return true;
    }

    /// <summary>Unregisters callbacks and releases atmosphere resources.</summary>
    public override void Dispose()
    {
        var client = api; api = null;
        Rendering.Integration.RenderingCleanup.Run(
            () => { if (client is not null) client.Event.UnregisterRenderer(this, EnumRenderStage.Before); },
            () => { if (client is not null) client.Event.LeaveWorld -= Reset; },
            () => { if (client is not null) client.Event.BlockTexturesLoaded -= StartCelestialLoading; },
            () => { if (client is not null) client.Event.LevelFinalize -= FinishCelestialLoading; },
            () => { var owner = reloadRegistration; reloadRegistration = null; owner?.Dispose(); },
            Reset,
            () => base.Dispose());
    }
    #endregion

    #region Update
    /// <summary>Prepares deterministic source inputs during the engine's world-loading texture callback.</summary>
    private void StartCelestialLoading() => celestial?.StartLoading((int)ConfigModSystem.Config.Atmosphere.SkyLutQuality);
    /// <summary>Submits the complete tiled GPU build on the load callback's owning render context.</summary>
    private void FinishCelestialLoading() => celestial?.FinishLoading((int)ConfigModSystem.Config.Atmosphere.SkyLutQuality);

    /// <summary>One block is one metre; cloud coverage is mapped to bounded aerosol turbidity, not used as an SI density.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Before) return;
        PrepareContext();
        celestial?.Update((int)ConfigModSystem.Config.Atmosphere.SkyLutQuality);
        if (context == default) return;
        if (api?.World?.Player?.Entity is not { } player || api.World.Calendar is not { } calendar)
        {
            ambientPublication?.Dispose();
            return;
        }
        try
        {
            // Supply valid resources without integrating on the render thread before the first result arrives.
            if (Lighting is null)
                Publish(new(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
                    ImmutableArray.Create(0f, 0f, 0f, 1f)) { Width = 1, Height = 1 });
            var sources = AtmosphereCelestialInputs.Capture(calendar);
            var settings = ConfigModSystem.Config.Atmosphere;
            var seasonal = seasonInputs.Capture(api, player.Pos.AsBlockPos);
            pendingPublication ??= computation!.Update(sources,
                (float)(player.Pos.Y - api.World.SeaLevel) * .001f, api.Ambient.BlendedCloudDensity,
                width: settings.LookupWidth, height: settings.LookupHeight, quality: (int)settings.SkyLutQuality,
                groundAlbedo: seasonal.GroundAlbedo,
                source: (celestial?.Ready ?? celestial?.Completed)?.Source,
                rotation: CelestialOrientation.Capture(api));
            PublishPending();
            updateFailureReported = false;
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            // Upload publication is atomic. Continue serving the last complete snapshot and
            // advancing shared frame inputs even if the next atmospheric update fails.
            if (!updateFailureReported)
                api.Logger.Error("[VGE] Atmosphere update failed; retaining the last published lighting. {0}", error);
            updateFailureReported = true;
        }
        if (SkyRenderer.IsEnabled)
        {
            // Environmental and frame publication continue even when drawing resources are unavailable.
            // The neutral startup snapshot is a valid publication until physical transport completes.
            if (Lighting is { } lighting) ambientPublication?.Publish(lighting);
            EngineSkyFrameInputs.Publish(api);
        }
        else ambientPublication?.Dispose();
    }

    /// <summary>Commits a matching source/light pair, retaining the candidate unchanged if texture upload fails.</summary>
    internal void PublishPending()
    {
        PrepareContext();
        if (pendingPublication is not { } ready) return;
        if (ready.CelestialSource is { } required
            && !ReferenceEquals(required, celestial?.Ready?.Source)
            && !ReferenceEquals(required, celestial?.Completed?.Source))
        {
            // Context recovery may invalidate a source admitted by an old worker. Withdraw
            // that result before any upload, and restart transport against the recovered source.
            pendingPublication = null; computation?.Reload(); return;
        }
        Publish(ready);
        if (ready.CelestialSource is { } source) celestial?.Commit(source);
        pendingPublication = null;
    }

    /// <summary>Withdraws dead-context transport and textures without cancelling the source owner's recovery lifecycle.</summary>
    internal void PrepareContext()
    {
        var current = Rendering.Integration.RenderContextRegistry.Current();
        if (context != default && current != context)
        {
            // Handles and fences belong to the old namespace. Recreate the existing backend;
            // celestial lifetime independently abandons its dead sets and restarts load preparation.
            pendingPublication = null; Lighting = null;
            SkyTextureId = AerialRadianceTextureId = AerialAttenuationTextureId = 0; SkyTexture = default;
            AerialRadianceTexture = AerialAttenuationTexture = null; ExternalTransmissionTexture = null;
            Rendering.Integration.RenderingCleanup.Run(
                () => { computation?.Dispose(); computation = null; },
                () => { textures?.Dispose(); textures = null; },
                () => { stagingTextures?.Dispose(); stagingTextures = null; });
            if (api is not null) computation = new(api);
        }
        context = current;
    }

    /// <summary>Uploads a completed lookup before swapping dimensions and lighting visible to consumers.</summary>
    internal void Publish(AtmosphereLighting ready)
    {
        PrepareContext();
        // Integrators retain physical FP32 data; only the immutable uploaded generation is encoded.
        ready = ready with { LookupStorageScale = PBR.LightingStorage.PersistentScale, CalibrationGeneration = 1 };
        if (stagingTextures is null || !stagingTextures.Matches(ready))
        {
            stagingTextures?.Dispose(); stagingTextures = null;
            stagingTextures = new(ready);
        }
        stagingTextures.Upload(ready);
        // A failed upload leaves every published texture and its lighting unchanged.
        // Reuse two complete sets rather than allocating textures on every sun update.
        (textures, stagingTextures) = (stagingTextures, textures);
        AerialRadianceTexture = textures.Radiance;
        AerialAttenuationTexture = textures.Attenuation;
        ExternalTransmissionTexture = textures.ExternalTransmission;
        SkyTextureId = textures.Sky.TextureId;
        SkyTexture = textures.Sky;
        AerialRadianceTextureId = textures.Radiance.TextureId;
        AerialAttenuationTextureId = textures.Attenuation.TextureId;
        Lighting = ready with { ReconstructionMaximumNits = textures.ReconstructionMaximumNits };
    }
    #endregion
}
