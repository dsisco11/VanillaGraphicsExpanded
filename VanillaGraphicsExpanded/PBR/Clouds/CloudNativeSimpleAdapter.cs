using System;
using System.Numerics;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Updates and immediately submits borrowed Simple geometry inside a replacement draw callback.</summary>
internal static class CloudNativeSimpleAdapter
{
    private static readonly AccessTools.FieldRef<CloudRenderer, MeshRef> Mesh = AccessTools.FieldRefAccess<CloudRenderer, MeshRef>("cloudTilesMeshRef");
    private static readonly AccessTools.FieldRef<CloudRenderer, CloudTilesState> State = AccessTools.FieldRefAccess<CloudRenderer, CloudTilesState>("committedState");
    private static readonly AccessTools.FieldRef<CloudRenderer, WeatherSystemClient> Weather = AccessTools.FieldRefAccess<CloudRenderer, WeatherSystemClient>("weatherSys");
    private static readonly AccessTools.FieldRef<CloudRenderer, bool> Enabled = AccessTools.FieldRefAccess<CloudRenderer, bool>("renderClouds");

    #region Public API
    /// <summary>Ticks exactly once when running, then borrows geometry for synchronous submission. Missing camera input skips drawing only.</summary>
    internal static void Render(ICoreClientAPI api, CloudRenderer owner, float deltaTime,
        long world, uint? frame, Action<CloudSimpleInput> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (!Enabled(owner)) return;
        if (!api.IsGamePaused) owner.CloudTick(deltaTime);
        if (api.Render.FrameWidth == 0 || frame is null) return;
        var mesh = Mesh(owner);
        if (mesh is null || mesh.Disposed || !mesh.Initialized || owner.QuantityCloudTiles <= 0 || owner.CloudTileSize <= 0 || owner.CloudTileLength <= 0) return;
        // CloudTick may rebuild and dispose the mesh. Read all committed draw inputs only
        // after it returns, then submit synchronously without retaining a mesh reference.
        var state = State(owner);
        var entity = api.World.Player.Entity;
        float height = (float)(Weather(owner).CloudLevelRel * api.World.BlockAccessor.MapSizeY + .5 - entity.CameraPos.Y);
        var placement = new CloudPlacement(state.CenterTilePos.X, state.CenterTilePos.Z, state.TileOffsetX, state.TileOffsetZ,
            state.WindTileOffsetX, state.WindTileOffsetZ, owner.windOffsetX, owner.windOffsetZ,
            new Vector3((float)((double)state.CenterTilePos.X * owner.CloudTileSize - entity.Pos.X + owner.windOffsetX), height,
                (float)((double)state.CenterTilePos.Z * owner.CloudTileSize - entity.Pos.Z + owner.windOffsetZ)),
            (float)(api.World.Calendar.TotalHours * 20 % 578));
        if (placement.IsFinite)
            draw(new(owner, mesh, owner.QuantityCloudTiles, owner.CloudTileSize, owner.CloudTileLength, placement, world, frame.Value));
    }
    #endregion
}
