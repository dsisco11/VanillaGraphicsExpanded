using System;

namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class LumonScenePhysicalPoolPlanner
{
    public const int PatchSizeVoxels = LumonSceneVoxelPatchLayout.VoxelsPerPatchEdge; // 4
    public const int PhysicalAtlasSizeTexels = LumonSceneVirtualAtlasConstants.PhysicalAtlasSizeTexels; // 4096

    public static LumonScenePhysicalPoolPlan CreateNearPlan(int nearTexelsPerVoxelFaceEdge, int nearRadiusXZChunks, int nearRadiusYChunks, int nearPagesPerChunkBudget, int maxAtlasCount)
    {
        if (nearPagesPerChunkBudget <= 0) throw new ArgumentOutOfRangeException(nameof(nearPagesPerChunkBudget));

        int tileSizeTexels = checked(nearTexelsPerVoxelFaceEdge * PatchSizeVoxels);
        int requestedChunks = LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesBoxField(nearRadiusXZChunks, nearRadiusYChunks);
        int requestedPages = checked(requestedChunks * nearPagesPerChunkBudget);
        return CreatePlan(LumonSceneField.Near, tileSizeTexels, requestedPages, maxAtlasCount);
    }

    public static LumonScenePhysicalPoolPlan CreateFarPlanAnnulus(int farTexelsPerVoxelFaceEdge, int nearRadiusXZChunks, int nearRadiusYChunks, int farRadiusXZChunks, int farRadiusYChunks, int farPagesPerChunkBudget, int maxAtlasCount)
    {
        if (farPagesPerChunkBudget <= 0) throw new ArgumentOutOfRangeException(nameof(farPagesPerChunkBudget));

        int tileSizeTexels = checked(farTexelsPerVoxelFaceEdge * PatchSizeVoxels);
        int requestedChunks = LumonScenePoolSizingUtil.ComputeGuaranteedResidentPagesFarAnnulus(nearRadiusXZChunks, nearRadiusYChunks, farRadiusXZChunks, farRadiusYChunks);
        int requestedPages = checked(requestedChunks * farPagesPerChunkBudget);
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

        // CapacityPages is the budgeting limit for the physical page pool, not the maximum tiles addressable in the GL texture.
        // We cap the pool to the requested budget (plus any margin already included in requestedPages) unless clamped by maxAtlasCount.
        int maxPagesByAtlases = checked(atlasCount * tilesPerAtlas);
        int capacityPages = clamped ? maxPagesByAtlases : Math.Min(requestedPages, maxPagesByAtlases);

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

