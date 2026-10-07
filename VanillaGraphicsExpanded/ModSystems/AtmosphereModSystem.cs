using System;
using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Schedules background atmosphere builds and publishes matching sky and lighting inputs before scene rendering.</summary>
public sealed class AtmosphereModSystem : ModSystem, IRenderer
{
    private ICoreClientAPI? api;
    private AtmosphereBackend? computation;
    private AtmosphereSeasonInputs seasonInputs = new();
    private AtmosphereTextureSet? textures, stagingTextures;
    private AtmosphereAmbientPublication? ambientPublication;
    private bool updateFailureReported;
    internal static AtmosphereLighting? Lighting { get; private set; }
    internal static int SkyTextureId { get; private set; }
    internal static int AerialRadianceTextureId { get; private set; }
    internal static int AerialAttenuationTextureId { get; private set; }
    /// <summary>Returns the borrowed radiance volume from the published atmosphere generation.</summary>
    internal static DynamicTexture3D? AerialRadianceTexture { get; private set; }
    /// <summary>Returns the borrowed attenuation volume from the published atmosphere generation.</summary>
    internal static DynamicTexture3D? AerialAttenuationTexture { get; private set; }
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
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_atmosphere");
        api.Event.LeaveWorld += Reset;
        api.Event.ReloadShader += Reload;
    }

    /// <summary>Releases the last world's snapshot and owned GPU texture.</summary>
    private void Reset()
    {
        updateFailureReported = false;
        ambientPublication?.Dispose();
        AerialRadianceTexture = null; AerialAttenuationTexture = null;
        Lighting = null; SkyTextureId = 0; AerialRadianceTextureId = 0; AerialAttenuationTextureId = 0;
        seasonInputs = new();
        computation?.Dispose(); computation = api is null ? null : new(api);
        textures?.Dispose(); stagingTextures?.Dispose(); textures = null; stagingTextures = null;
    }

    /// <summary>Invalidates pending shader generations while keeping the last complete display snapshot.</summary>
    private bool Reload()
    {
        computation?.Dispose(); computation = api is null ? null : new(api);
        return true;
    }

    /// <summary>Unregisters callbacks and releases atmosphere resources.</summary>
    public override void Dispose()
    {
        if (api is not null)
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            api.Event.LeaveWorld -= Reset;
            api.Event.ReloadShader -= Reload;
        }
        Reset(); computation?.Dispose(); computation = null; api = null; base.Dispose();
    }
    #endregion

    #region Update
    /// <summary>One block is one metre; cloud coverage is mapped to bounded aerosol turbidity, not used as an SI density.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Before) return;
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
            var direction = calendar.SunPositionNormalized;
            var settings = ConfigModSystem.Config.Atmosphere;
            var seasonal = seasonInputs.Capture(api, player.Pos.AsBlockPos);
            var ready = computation!.Update(new((float)direction.X, (float)direction.Y, (float)direction.Z),
                (float)(player.Pos.Y - api.World.SeaLevel) * .001f, api.Ambient.BlendedCloudDensity,
                width: settings.LookupWidth, height: settings.LookupHeight, quality: (int)settings.SkyLutQuality,
                groundAlbedo: seasonal.GroundAlbedo);
            if (ready is not null) Publish(ready);
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
        if (AtmosphereSkyRenderer.IsEnabled)
        {
            // Environmental and frame publication continue even when drawing resources are unavailable.
            // The neutral startup snapshot is a valid publication until physical transport completes.
            if (Lighting is { } lighting) ambientPublication?.Publish(lighting);
            EngineSkyFrameInputs.Publish(api);
        }
        else ambientPublication?.Dispose();
    }

    /// <summary>Uploads a completed lookup before swapping dimensions and lighting visible to consumers.</summary>
    internal void Publish(AtmosphereLighting ready)
    {
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
        SkyTextureId = textures.Sky.TextureId;
        AerialRadianceTextureId = textures.Radiance.TextureId;
        AerialAttenuationTextureId = textures.Attenuation.TextureId;
        Lighting = ready;
    }
    #endregion
}
