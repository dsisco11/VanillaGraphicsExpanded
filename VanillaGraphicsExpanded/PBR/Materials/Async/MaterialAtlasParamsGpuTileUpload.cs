using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// GPU job: upload procedural material params tile data.
/// </summary>
internal readonly record struct MaterialAtlasParamsGpuTileUpload(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    float[] RgbTriplets,
    AssetLocation TargetTexture,
    int Priority) : IMaterialAtlasGpuJob
{
    public void Execute(ICoreClientAPI capi, System.Func<int, PbrMaterialAtlasPageTextures?> tryGetPageTextures, PbrMaterialAtlasBuildSession session)
    {
        var pageTextures = tryGetPageTextures(AtlasTextureId);
        if (pageTextures is null)
        {
            return;
        }

        pageTextures.MaterialParamsTexture.UploadData(
            RgbTriplets,
            Rect.X,
            Rect.Y,
            Rect.Width,
            Rect.Height);

        session.IncrementCompletedTile();

        if (session.PagesByAtlasTexId.TryGetValue(AtlasTextureId, out PbrMaterialAtlasPageBuildState? page))
        {
            page.InFlightTiles = System.Math.Max(0, page.InFlightTiles - 1);
            page.CompletedTiles++;
        }

        // Enqueue override upload if any (including override-only rects).
        if (session.TryDequeueOverrideForRect(AtlasTextureId, Rect, out MaterialAtlasParamsGpuOverrideUpload ov))
        {
            session.EnqueueOverrideUpload(ov);
        }
    }
}
