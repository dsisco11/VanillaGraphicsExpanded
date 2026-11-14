using System;
using System.Diagnostics;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Composes native world inputs and the opt-in shared surface shadow provider through the client rendering lifetime.</summary>
public sealed class VirtualShadowModSystem : ClientRenderingModSystem, ILiveConfigurable
{
    private const string PatchId = Constants.ModId + ".shadowinputs";
    private ICoreClientAPI? api;
    private Harmony? hooks;
    private NativeShadowWorldAdapter? world;
    private ClientWorldMap? attachedMap;
    private ShadowFrameRenderer? frames;
    private (nint Handle,long Generation) context;
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
        RenderContextRegistry.Retiring += OnContextRetiring;
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
        try{Release();}
        finally{api=null;RenderContextRegistry.Retiring-=OnContextRetiring;}
    }

    /// <summary>Withdraws the previous publication while preserving pending native work at the render-thread boundary.</summary>
    public void OnConfigReloaded(ICoreAPI changedApi)
    {
        if (world == null || !ReferenceEquals(api, changedApi)) return;
        frames?.ApplyPolicy(ConfigModSystem.Config.VirtualShadows);
        SurfaceShadowBindings.Attach(()=>frames?.Current?.Publication,ConfigModSystem.Config.VirtualShadows.Pages);
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
            context=RenderContextRegistry.Current();
            var settings=ConfigModSystem.Config.VirtualShadows;
            frames=new(api,policy=>NativeShadowFrameFactory.Create(api,game,next,policy,Clock),settings,Clock);
            SurfaceShadowBindings.Attach(()=>frames?.Current?.Publication,settings.Pages);
            // Compatibility particle/decal shaders still own native cascade
            // sampling. Migrated physical surface shaders never consume those
            // textures, including when shared shadow readiness is unavailable.
            api.Logger.Notification("[VGE Shadows] Shared surface provider registered; native cascades remain owned by compatibility particles and decals.");
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
        var retiringFrames=frames;frames=null;
        var retiringWorld=world;world=null;
        var retiringHooks=hooks;hooks=null;attachedMap=null;
        context=default;
        RenderingCleanup.Run(()=>retiringFrames?.Dispose(),SurfaceShadowBindings.Retire,
            NativeShadowWorldHooks.Detach,()=>retiringWorld?.Dispose(),()=>retiringHooks?.UnpatchAll(PatchId));
    }

    /// <summary>Withdraws every borrower before its originating context and resource namespace are replaced.</summary>
    private void OnContextRetiring((nint Handle,long Generation) retiring){if(retiring==context)Release();}

    /// <summary>Uses a monotonic time origin for finite temporal eligibility without depending on world simulation time.</summary>
    private static double Clock()=>Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency;
    #endregion
}
