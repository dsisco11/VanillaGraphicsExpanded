using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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

    private readonly float[] modelViewMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumOnTerrainBridgeUpdateRenderer(ICoreClientAPI capi, VgeConfig config)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_lumon_terrain_bridge");
        capi.Event.LeaveWorld += OnLeaveWorld;
    }

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

        var player = capi.World?.Player;
        var entity = player?.Entity;
        if (entity is null)
        {
            return;
        }

        // World camera position (double precision, world coords).
        double camWorldX = entity.CameraPos.X;
        double camWorldY = entity.CameraPos.Y;
        double camWorldZ = entity.CameraPos.Z;

        // Camera position in render "matrix space" (derived from the camera matrix origin).
        // This matches the coordinate system used by terrain shaders for `worldPos`.
        Array.Copy(capi.Render.CameraMatrixOriginf, modelViewMatrix, 16);
        Array.Copy(modelViewMatrix, invModelViewMatrix, 16);
        MatrixHelper.Invert(invModelViewMatrix, invModelViewMatrix);

        double camMatrixX = invModelViewMatrix[12];
        double camMatrixY = invModelViewMatrix[13];
        double camMatrixZ = invModelViewMatrix[14];

        // offsetBlocks = camWorld - camMatrix.
        double offX = camWorldX - camMatrixX;
        double offY = camWorldY - camMatrixY;
        double offZ = camWorldZ - camMatrixZ;

        int offChunkX = (int)Math.Floor(offX * (1.0 / 32.0));
        int offChunkY = (int)Math.Floor(offY * (1.0 / 32.0));
        int offChunkZ = (int)Math.Floor(offZ * (1.0 / 32.0));

        double remX = offX - (offChunkX * 32.0);
        double remY = offY - (offChunkY * 32.0);
        double remZ = offZ - (offChunkZ * 32.0);

        LumonSceneWorldCoordUniformState.Update(
            new VectorInt3(offChunkX, offChunkY, offChunkZ),
            new Vector3d(remX, remY, remZ));

        // Upload UBO.
        LumOnTerrainBridgeUboState.Update(
            new VectorInt3(offChunkX, offChunkY, offChunkZ),
            new Vector3d(remX, remY, remZ));
    }

    public void Dispose()
    {
        capi.Event.LeaveWorld -= OnLeaveWorld;
        OnLeaveWorld();
    }

    private void OnLeaveWorld()
    {
        LumonSceneWorldCoordUniformState.Disable();
        LumOnTerrainBridgeUboState.Dispose();
    }
}
