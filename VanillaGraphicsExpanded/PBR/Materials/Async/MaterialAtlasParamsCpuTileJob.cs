using System.Threading;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// CPU job: build procedural material params for a tile.
/// </summary>
internal readonly record struct MaterialAtlasParamsCpuTileJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    AssetLocation Texture,
    PbrMaterialDefinition Definition,
    int Priority,
    IMaterialAtlasDiskCache? DiskCache,
    AtlasCacheKey CacheKey) : IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>
{
    public MaterialAtlasParamsGpuTileUpload Execute(CancellationToken cancellationToken)
    {
        float[] rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
            Texture,
            Definition,
            rectWidth: Rect.Width,
            rectHeight: Rect.Height,
            cancellationToken);

        DiskCache?.StoreMaterialParamsTile(CacheKey, Rect.Width, Rect.Height, rgb);

        return new MaterialAtlasParamsGpuTileUpload(
            GenerationId,
            AtlasTextureId,
            Rect,
            rgb,
            Texture,
            Priority);
    }
}
