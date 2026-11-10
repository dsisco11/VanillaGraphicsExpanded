using System;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Owns native shadow inputs without enabling shadow rendering or replacing native shading.</summary>
public sealed class VirtualShadowModSystem : ClientRenderingModSystem, ILiveConfigurable
{
    private const string PatchId = Constants.ModId + ".shadowinputs";
    private ICoreClientAPI? api;
    private Harmony? hooks;
    private NativeShadowWorldAdapter? world;
    private ClientWorldMap? attachedMap;
    /// <summary>Exposes the input owner to the shared coordinator; capture remains explicitly bounded.</summary>
    internal NativeShadowWorldAdapter? World => world;

    #region Public API
    /// <inheritdoc />
    public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

    /// <summary>Registers world lifetime boundaries, leaving GPU storage lazy until a consumer requests it.</summary>
    public override void StartClientSide(ICoreClientAPI client)
    {
        api = client;
        client.Event.LevelFinalize += Attach;
        client.Event.LeaveWorld += Release;
        ((ICoreAPI)client).Event.ChunkDirty += Dirty;
        Attach();
    }

    /// <summary>Unregisters input ownership and leaves all native buffers/chunks owned by the game.</summary>
    public override void Dispose()
    {
        if (api != null)
        {
            api.Event.LevelFinalize -= Attach;
            api.Event.LeaveWorld -= Release;
            ((ICoreAPI)api).Event.ChunkDirty -= Dirty;
        }
        Release(); api = null;
    }

    /// <summary>Withdraws the previous publication while preserving pending native work at the render-thread boundary.</summary>
    public void OnConfigReloaded(ICoreAPI changedApi)
    {
        if (world == null || !ReferenceEquals(api, changedApi)) return;
        var expected = world;
        api!.Event.EnqueueMainThreadTask(() =>
        {
            if (!ReferenceEquals(world, expected)) return;
            var policy = ConfigModSystem.Config.VirtualShadows.Inputs;
            expected.Reconfigure(policy.ChunkCapacity, policy.RangeCapacity, policy.ChangeCapacity, policy.ProbeLimit);
            NativeShadowWorldHooks.Reconfigure(policy.ChangeCapacity);
        }, "vge-shadow-input-policy");
    }
    #endregion

    #region Private
    /// <summary>Installs lifecycle observations before the first safe bootstrap.</summary>
    private void Attach()
    {
        if (api?.World is not ClientMain game || game.WorldMap == null) return;
        if (world != null && ReferenceEquals(attachedMap, game.WorldMap)) return;
        Release();
        var policy = ConfigModSystem.Config.VirtualShadows.Inputs;
        var next = new NativeShadowWorldAdapter(game.WorldMap, policy.ChunkCapacity, policy.RangeCapacity, policy.ChangeCapacity, policy.ProbeLimit);
        hooks = new(PatchId);
        try
        {
            NativeShadowWorldHooks.Install(hooks, game.WorldMap, next);
            attachedMap = game.WorldMap; world = next;
        }
        catch (Exception error)
        {
            next.Dispose();
            Release();
            api.Logger.Error("[VGE Shadows] Native input publication unavailable: {0}", error);
        }
    }

    /// <summary>Records native edits and availability changes under the existing event owner.</summary>
    private void Dirty(Vec3i coordinate, IWorldChunk chunk, EnumChunkDirtyReason reason)
    {
        if (world != null && api?.World is ClientMain game)
        {
            long index = game.WorldMap.ChunkIndex3D(coordinate.X, coordinate.Y, coordinate.Z);
            if (!NativeShadowWorldHooks.CoversDirtyEvent(index)) world.Dirty(index);
        }
    }

    /// <summary>Withdraws callbacks before retiring metadata and this owner's patches.</summary>
    private void Release()
    {
        NativeShadowWorldHooks.Detach();
        world?.Dispose(); world = null;
        attachedMap = null;
        hooks?.UnpatchAll(PatchId); hooks = null;
    }
    #endregion
}
