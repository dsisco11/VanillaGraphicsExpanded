using System;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Owns bounded atmosphere updates and atomically publishes matching sky and lighting inputs before scene rendering.</summary>
public sealed class AtmosphereModSystem : ModSystem, IRenderer
{
    private ICoreClientAPI? api;
    private AtmosphereLookup lookup = new();
    private DynamicTexture2D? sky;
    internal static AtmosphereLighting? Lighting { get; private set; }
    internal static int SkyTextureId { get; private set; }
    public double RenderOrder => -.5;
    public int RenderRange => 1;

    #region Lifecycle
    /// <summary>Atmospheric rendering is a client-only service independent of LumOn.</summary>
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    /// <summary>Registers synchronous initialization and bounded refresh before any sky or lighting consumer runs.</summary>
    public override void StartClientSide(ICoreClientAPI api)
    {
        this.api = api;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_atmosphere");
        api.Event.LeaveWorld += Reset;
    }

    /// <summary>Releases the last world's snapshot and owned GPU texture.</summary>
    private void Reset()
    {
        Lighting = null; SkyTextureId = 0;
        sky?.Dispose(); sky = null; lookup = new();
    }

    /// <summary>Unregisters callbacks and releases atmosphere resources.</summary>
    public override void Dispose()
    {
        if (api is not null)
        {
            api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            api.Event.LeaveWorld -= Reset;
        }
        Reset(); api = null; base.Dispose();
    }
    #endregion

    #region Update
    /// <summary>One block is one metre; cloud coverage is mapped to bounded aerosol turbidity, not used as an SI density.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (api?.World?.Player?.Entity is not { } player || api.World.Calendar is not { } calendar) return;
        var direction = calendar.SunPositionNormalized;
        // The first scene frame must have a complete texture and matching lighting; later refreshes retain it.
        if (!lookup.Update(new((float)direction.X, (float)direction.Y, (float)direction.Z),
            (float)(player.Pos.Y - api.World.SeaLevel) * .001f, api.Ambient.BlendedCloudDensity, complete: lookup.Current is null)) return;
        var ready = lookup.Current!;
        sky ??= DynamicTexture2D.Create(AtmosphereLookup.Width, AtmosphereLookup.Height, PixelInternalFormat.Rgba16f,
            debugName: "Atmosphere.Sky");
        sky.DisableMipmaps();
        sky.SetTexFilter(TextureMinFilter.Linear, TextureMagFilter.Linear);
        sky.SetTexWrap(TextureWrapMode.Repeat, TextureWrapMode.ClampToEdge);
        sky.UploadData(ready.Sky.AsSpan().ToArray());
        // Publication follows the upload; consumers never see new illumination with the previous lookup.
        SkyTextureId = sky.TextureId; Lighting = ready;
    }
    #endregion
}
