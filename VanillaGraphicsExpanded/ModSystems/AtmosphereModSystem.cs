using System;
using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
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
    internal static AtmosphereLighting? Lighting { get; private set; }
    internal static int SkyTextureId { get; private set; }
    internal static int AerialRadianceTextureId { get; private set; }
    internal static int AerialAttenuationTextureId { get; private set; }
    public double RenderOrder => -.5;
    public int RenderRange => 1;

    #region Lifecycle
    /// <summary>Atmospheric rendering is a client-only service independent of LumOn.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    /// <summary>Registers background completion publication before sky and lighting consumers run.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        computation = new(api);
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_atmosphere");
        api.Event.LeaveWorld += Reset;
        api.Event.ReloadShader += Reload;
    }

    /// <summary>Releases the last world's snapshot and owned GPU texture.</summary>
    private void Reset()
    {
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
        if (api?.World?.Player?.Entity is not { } player || api.World.Calendar is not { } calendar) return;
        var direction = calendar.SunPositionNormalized;
        var settings = ConfigModSystem.Config.Atmosphere;
        var seasonal = seasonInputs.Capture(api, player.Pos.AsBlockPos);
        // Supply valid resources without integrating on the render thread before the first result arrives.
        if (Lighting is null)
            Publish(new(Vector3.UnitY, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
                ImmutableArray.Create(0f, 0f, 0f, 1f)) { Width = 1, Height = 1 });
        var ready = computation!.Update(new((float)direction.X, (float)direction.Y, (float)direction.Z),
            (float)(player.Pos.Y - api.World.SeaLevel) * .001f, api.Ambient.BlendedCloudDensity,
            width: settings.LookupWidth, height: settings.LookupHeight, quality: settings.SkyLutQuality,
            groundAlbedo: seasonal.GroundAlbedo);
        if (ready is not null) Publish(ready);
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
        SkyTextureId = textures.Sky.TextureId;
        AerialRadianceTextureId = textures.Radiance.TextureId;
        AerialAttenuationTextureId = textures.Attenuation.TextureId;
        Lighting = ready;
    }
    #endregion
}
