using System;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct PbrMaterialAtlasTileJob(
    int GenerationId,
    int AtlasTextureId,
    int RectX,
    int RectY,
    int RectWidth,
    int RectHeight,
    AssetLocation Texture,
    PbrMaterialDefinition Definition,
    int Priority,
    AssetLocation? MaterialParamsOverride,
    string? OverrideRuleId,
    AssetLocation? OverrideRuleSource,
    PbrOverrideScale OverrideScale);
