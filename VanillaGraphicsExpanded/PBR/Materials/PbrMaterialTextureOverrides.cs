using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal readonly record struct PbrMaterialTextureOverrides(
    string? RuleId,
    AssetLocation RuleSource,
    AssetLocation? MaterialParams,
    AssetLocation? NormalHeight)
{
    public bool IsEmpty => MaterialParams is null && NormalHeight is null;
}
