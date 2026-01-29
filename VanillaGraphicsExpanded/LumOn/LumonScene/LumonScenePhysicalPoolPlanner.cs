using System;

namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal static class LumonScenePhysicalPoolPlanner
{
    public const int PatchSizeVoxels = LumonSceneVoxelPatchLayout.VoxelsPerPatchEdge; // 4
    public const int PhysicalAtlasSizeTexels = LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels; // 4096

    public static LumonScenePhysicalPoolPlan CreateNearPlan(int nearTexelsPerVoxelFaceEdge, int nearRadiusChunks, int maxAtlasCount)
    {
        int tileSizeTexels = checked(nearTexelsPerVoxelFaceEdge * PatchSizeVoxels);
        int requestedPages = LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesSquareField(nearRadiusChunks);
        return CreatePlan(LumonSceneField.Near, tileSizeTexels, requestedPages, maxAtlasCount);
    }

    public static LumonScenePhysicalPoolPlan CreateFarPlanAnnulus(int farTexelsPerVoxelFaceEdge, int nearRadiusChunks, int farRadiusChunks, int maxAtlasCount)
    {
        int tileSizeTexels = checked(farTexelsPerVoxelFaceEdge * PatchSizeVoxels);
        int requestedPages = LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesFarAnnulus(nearRadiusChunks, farRadiusChunks);
        return CreatePlan(LumonSceneField.Far, tileSizeTexels, requestedPages, maxAtlasCount);
    }

    private static LumonScenePhysicalPoolPlan CreatePlan(LumonSceneField field, int tileSizeTexels, int requestedPages, int maxAtlasCount)
    {
        if (tileSizeTexels <= 0) throw new ArgumentOutOfRangeException(nameof(tileSizeTexels));
        if (requestedPages < 0) throw new ArgumentOutOfRangeException(nameof(requestedPages));
        if (maxAtlasCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxAtlasCount));

        if ((PhysicalAtlasSizeTexels % tileSizeTexels) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tileSizeTexels), $"tileSizeTexels={tileSizeTexels} must evenly divide {PhysicalAtlasSizeTexels}.");
        }

        int tilesPerAxis = PhysicalAtlasSizeTexels / tileSizeTexels;
        int tilesPerAtlas = checked(tilesPerAxis * tilesPerAxis);

        int atlasCountNeeded = requestedPages <= 0 ? 1 : (int)(((long)requestedPages + tilesPerAtlas - 1) / tilesPerAtlas);
        bool clamped = atlasCountNeeded > maxAtlasCount;
        int atlasCount = Math.Min(atlasCountNeeded, maxAtlasCount);

        int capacityPages = checked(atlasCount * tilesPerAtlas);
        if (!clamped && capacityPages > requestedPages)
        {
            // We only provision the requested number of pages in the pool, even if the last atlas has spare tiles.
            // This keeps the pool aligned with the “only allocate enough pages” requirement.
            capacityPages = requestedPages;
        }

        return new LumonScenePhysicalPoolPlan(
            field: field,
            tileSizeTexels: tileSizeTexels,
            tilesPerAxis: tilesPerAxis,
            tilesPerAtlas: tilesPerAtlas,
            requestedPages: requestedPages,
            capacityPages: capacityPages,
            atlasCount: atlasCount,
            isClampedByMaxAtlases: clamped);
    }
}

