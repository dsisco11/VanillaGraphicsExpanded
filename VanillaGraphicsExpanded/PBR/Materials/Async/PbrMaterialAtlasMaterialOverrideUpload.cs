using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct PbrMaterialAtlasMaterialOverrideUpload(
    int GenerationId,
    int AtlasTextureId,
    int RectX,
    int RectY,
    int RectWidth,
    int RectHeight,
    AssetLocation TargetTexture,
    AssetLocation OverrideAsset,
    string? RuleId,
    AssetLocation? RuleSource,
    PbrOverrideScale Scale);
