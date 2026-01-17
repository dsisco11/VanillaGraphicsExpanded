using System;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal readonly record struct PbrMaterialAtlasTileUpload(
    int GenerationId,
    int AtlasTextureId,
    int RectX,
    int RectY,
    int RectWidth,
    int RectHeight,
    float[] RgbTriplets,
    AssetLocation TargetTexture,
    AssetLocation? MaterialParamsOverride,
    string? OverrideRuleId,
    AssetLocation? OverrideRuleSource,
    PbrOverrideScale OverrideScale);
