using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Loads material override textures (e.g. params/normal-height) into float RGBA data suitable for CPU-side application.
/// Wraps the existing loader to provide a stable abstraction for the refactor.
/// </summary>
internal sealed class MaterialOverrideTextureLoader
{
    public bool TryLoadRgbaFloats01(
        ICoreClientAPI capi,
        AssetLocation overrideId,
        out int width,
        out int height,
        out float[] rgba01,
        out string? reason,
        int? expectedWidth = null,
        int? expectedHeight = null)
        => PbrOverrideTextureLoader.TryLoadRgbaFloats01(
            capi,
            overrideId,
            out width,
            out height,
            out rgba01,
            out reason,
            expectedWidth,
            expectedHeight);

    public void ClearCache() => PbrOverrideTextureLoader.ClearCache();
}
