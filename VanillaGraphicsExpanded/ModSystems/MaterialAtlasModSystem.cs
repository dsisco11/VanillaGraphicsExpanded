using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Diagnostics;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class MaterialAtlasModSystem : ModSystem
{
    private ICoreClientAPI? capi;

    private HudMaterialAtlasProgressPanel? progressPanel;

    private bool isLevelFinalized;
    private bool pendingPopulate;
    private long populateCallbackId;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        // Material params + normal/depth atlas textures:
        // - Phase 1 (allocation) can happen any time (no-op until atlas exists)
        // - Phase 2 (populate/bake) must only run after the block atlas is finalized
        MaterialAtlasSystem.Instance.CreateTextureObjects(api);

        api.Event.BlockTexturesLoaded += OnBlockTexturesLoaded;
        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.ReloadTextures += OnReloadTextures;

        // Optional: small in-game progress overlay while the material atlas builds.
        progressPanel = new HudMaterialAtlasProgressPanel(api, MaterialAtlasSystem.Instance);
    }

    #region Event Handlers
    private void OnBlockTexturesLoaded()
    {
        // Keep textures in sync with the block atlas as soon as it exists,
        // but defer the expensive population/bake until the world is fully ready.
        MaterialAtlasSystem.Instance.CreateTextureObjects(capi!);

        // Cache-only warmup during the loading screen: upload cached tiles early,
        // while deferring cache misses to the normal pipeline.
        if (ConfigModSystem.Config.MaterialAtlas.ForceCacheWarmupDirectUploadsOnWorldLoad)
        {
            MaterialAtlasSystem.Instance.WarmupAtlasCacheBlockingDirectUploads(capi!);
        }
        else
        {
            MaterialAtlasSystem.Instance.WarmupAtlasCache(capi!);
        }

        if (isLevelFinalized)
        {
            MaterialAtlasSystem.Instance.PopulateAtlasContents(capi!);
        }
        else
        {
            pendingPopulate = true;
        }
    }

    private void OnLevelFinalize()
    {
        isLevelFinalized = true;

        if (!pendingPopulate)
        {
            return;
        }

        pendingPopulate = false;

        if (populateCallbackId != 0)
        {
            capi!.Event.UnregisterCallback(populateCallbackId);
        }

        // Give the client a brief moment after finalize to finish settling (GUI, chunk init, etc.).
        populateCallbackId = capi!.Event.RegisterCallback(
            _ => MaterialAtlasSystem.Instance.PopulateAtlasContents(capi!),
            millisecondDelay: 500);

        // Defensive: ensure any residual artifact work is idle before leaving the loading screen.
        // (In the direct-upload warmup path, no artifact jobs should be enqueued.)
        MaterialAtlasSystem.Instance.WaitForIdleAsync().GetAwaiter().GetResult();        
    }

    private void OnReloadTextures()
    {
        capi!.Logger.Debug("[VGE] ReloadTextures event");
        MaterialAtlasSystem.Instance.RequestRebuild(capi!);
    }
    #endregion

    public override void Dispose()
    {
        base.Dispose();

        if (capi != null && populateCallbackId != 0)
        {
            capi.Event.UnregisterCallback(populateCallbackId);
            populateCallbackId = 0;
        }

        progressPanel?.Dispose();
        progressPanel = null;

        MaterialAtlasSystem.Instance.Dispose();

        capi = null;
        isLevelFinalized = false;
        pendingPopulate = false;
    }
}
