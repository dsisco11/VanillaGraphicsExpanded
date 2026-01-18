using System.Threading;

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
    int Priority) : IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>
{
    public MaterialAtlasParamsGpuTileUpload Execute(CancellationToken cancellationToken)
    {
        float[] rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
            Texture,
            Definition,
            rectWidth: Rect.Width,
            rectHeight: Rect.Height,
            cancellationToken);

        return new MaterialAtlasParamsGpuTileUpload(
            GenerationId,
            AtlasTextureId,
            Rect,
            rgb,
            Texture,
            Priority);
    }
}
