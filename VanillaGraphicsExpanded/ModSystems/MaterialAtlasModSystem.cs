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

        api.Event.BlockTexturesLoaded += () =>
        {
            // Keep textures in sync with the block atlas as soon as it exists,
            // but defer the expensive population/bake until the world is fully ready.
            MaterialAtlasSystem.Instance.CreateTextureObjects(api);

            // Cache-only warmup during the loading screen: upload cached tiles early,
            // while deferring cache misses to the normal pipeline.
            if (ConfigModSystem.Config.MaterialAtlas.ForceCacheWarmupDirectUploadsOnWorldLoad)
            {
                MaterialAtlasSystem.Instance.WarmupAtlasCacheBlockingDirectUploads(api);
            }
            else
            {
                MaterialAtlasSystem.Instance.WarmupAtlasCache(api);
            }

            if (isLevelFinalized)
            {
                MaterialAtlasSystem.Instance.PopulateAtlasContents(api);
            }
            else
            {
                pendingPopulate = true;
            }
        };

        api.Event.LevelFinalize += () =>
        {
            isLevelFinalized = true;

            if (!pendingPopulate)
            {
                return;
            }

            pendingPopulate = false;

            if (populateCallbackId != 0)
            {
                api.Event.UnregisterCallback(populateCallbackId);
            }

            // Give the client a brief moment after finalize to finish settling (GUI, chunk init, etc.).
            populateCallbackId = api.Event.RegisterCallback(
                _ => MaterialAtlasSystem.Instance.PopulateAtlasContents(api),
                millisecondDelay: 500);
        };

        // Optional: small in-game progress overlay while the material atlas builds.
        progressPanel = new HudMaterialAtlasProgressPanel(api, MaterialAtlasSystem.Instance);

        api.Event.ReloadTextures += () =>
        {
            api.Logger.Debug("[VGE] ReloadTextures event");
            MaterialAtlasSystem.Instance.RequestRebuild(api);
        };
    }

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
