using System;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Updates the terrain bridge UBO once per frame early in the pipeline so patched terrain shaders
/// can compute stable PatchIds / chunkSlots using world-space voxel coordinates.
/// </summary>
internal sealed class LumOnTerrainBridgeUpdateRenderer : IRenderer, IDisposable
{
    // Run very early in Opaque so the engine's chunk shaders have the UBO updated for this frame.
    private const double RenderOrderValue = -10000.0;
    private const int RenderRangeValue = 1;

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly Func<LumOnCameraState?> readCamera;

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    #region Frame publication
    /// <summary>Registers the terrain origin publisher before opaque geometry is drawn.</summary>
    public LumOnTerrainBridgeUpdateRenderer(ICoreClientAPI capi, VgeConfig config)
        : this(capi, config, () => LumOnCameraState.Read(capi)) { }

    /// <summary>Creates the publisher with an explicit per-frame camera source for controlled runtime hosts.</summary>
    internal LumOnTerrainBridgeUpdateRenderer(ICoreClientAPI capi, VgeConfig config, Func<LumOnCameraState?> readCamera)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.readCamera = readCamera ?? throw new ArgumentNullException(nameof(readCamera));

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_lumon_terrain_bridge");
        capi.Event.LeaveWorld += OnLeaveWorld;
    }

    /// <summary>Publishes the stable player origin for terrain PatchIds and chunk-slot lookup.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque)
        {
            return;
        }

        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled)
        {
            return;
        }

        if (readCamera() is not { } camera)
        {
            return;
        }

        // Terrain worldPos is player-relative before the view transform. Camera bob belongs
        // only to that transform, so it must not shift voxel identities or patch UVs.
        var (chunkOffset, blockRemainder) = LumOnFrameWorldSpaceBridge.Compute(
            camera.PositionX, camera.PositionY, camera.PositionZ);
        LumonSceneWorldCoordUniformState.Update(chunkOffset, blockRemainder);
        LumOnTerrainBridgeUboState.Update(chunkOffset, blockRemainder);
    }
    #endregion

    #region Lifetime
    /// <summary>Removes the terrain callback before releasing its world-owned uniform state.</summary>
    public void Dispose()
    {
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        capi.Event.LeaveWorld -= OnLeaveWorld;
        OnLeaveWorld();
    }

    /// <summary>Clears world-owned coordinate and GPU state when leaving the world.</summary>
    private void OnLeaveWorld()
    {
        LumonSceneWorldCoordUniformState.Disable();
        LumOnTerrainBridgeUboState.Dispose();
    }
    #endregion
}
