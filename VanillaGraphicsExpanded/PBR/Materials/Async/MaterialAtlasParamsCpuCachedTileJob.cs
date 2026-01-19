using System.Threading;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// CPU job: emits a GPU upload from already-cached tile data.
/// This is used to keep the async scheduler plumbing consistent when a cache hit occurs.
/// </summary>
internal readonly record struct MaterialAtlasParamsCpuCachedTileJob(
    int GenerationId,
    int AtlasTextureId,
    AtlasRect Rect,
    float[] RgbTriplets,
    AssetLocation TargetTexture,
    int Priority) : IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>
{
    public MaterialAtlasParamsGpuTileUpload Execute(CancellationToken cancellationToken)
        => new(
            GenerationId,
            AtlasTextureId,
            Rect,
            RgbTriplets,
            TargetTexture,
            Priority,
            SkipUpload: false,
            SuppressOverrideUpload: false);
}
