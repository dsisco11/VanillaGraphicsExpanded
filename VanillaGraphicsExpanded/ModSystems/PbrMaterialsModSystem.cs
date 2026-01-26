using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class PbrMaterialsModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void AssetsLoaded(ICoreAPI api)
    {
        ConfigModSystem.EnsureConfigLoaded(api);

        // Load PBR material definitions (config/vge/material_definitions.json)
        PbrMaterialRegistry.Instance.Initialize(api);

        if (api is ICoreClientAPI capi)
        {
            // Preload base-color cache before any derived-surface computation.
            PbrMaterialRegistry.Instance.PreloadBaseColorCache(capi);
            PbrMaterialRegistry.Instance.StartBaseColorBackgroundRegen(capi);

            // Derived terms can be computed as soon as assets exist.
            PbrMaterialRegistry.Instance.BuildDerivedSurfaces(capi);

            // BlockId×face lookup requires blocks + textures. Build when block textures are ready.
            capi.Event.BlockTexturesLoaded += () =>
            {
                PbrMaterialRegistry.Instance.StopBaseColorBackgroundRegen();
                PbrMaterialRegistry.Instance.PreloadBaseColorCache(capi);
                PbrMaterialRegistry.Instance.StartBaseColorBackgroundRegen(capi);
                PbrMaterialRegistry.Instance.BuildDerivedSurfaces(capi);
                PbrMaterialRegistry.Instance.BuildBlockFaceDerivedSurfaceLookup(capi);
            };

            capi.Event.ReloadTextures += () =>
            {
                PbrMaterialRegistry.Instance.StopBaseColorBackgroundRegen();
                PbrMaterialRegistry.Instance.PreloadBaseColorCache(capi);
                PbrMaterialRegistry.Instance.StartBaseColorBackgroundRegen(capi);
                PbrMaterialRegistry.Instance.BuildDerivedSurfaces(capi);
                PbrMaterialRegistry.Instance.BuildBlockFaceDerivedSurfaceLookup(capi);
            };
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        PbrMaterialRegistry.Instance.StopBaseColorBackgroundRegen();

        // Clear material registry cache
        PbrMaterialRegistry.Instance.Clear();
    }
}
