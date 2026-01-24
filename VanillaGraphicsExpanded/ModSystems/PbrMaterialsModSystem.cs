using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class PbrMaterialsModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Load PBR material definitions (config/vge/material_definitions.json)
        PbrMaterialRegistry.Instance.Initialize(api);

        if (api is ICoreClientAPI capi)
        {
            PbrMaterialRegistry.Instance.BuildDerivedSurfaces(capi);
        }
    }

    public override void Dispose()
    {
        base.Dispose();

        // Clear material registry cache
        PbrMaterialRegistry.Instance.Clear();
    }
}
