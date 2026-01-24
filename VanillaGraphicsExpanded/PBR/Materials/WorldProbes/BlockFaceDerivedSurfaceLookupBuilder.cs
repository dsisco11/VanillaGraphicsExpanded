using System;
using System.Collections.Generic;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.WorldProbes;

internal static class BlockFaceDerivedSurfaceLookupBuilder
{
    private const int FacesPerBlock = 6;

    public readonly record struct Stats(
        int MaxBlockId,
        int TotalFaces,
        int ResolvedFaces,
        int TextureKeyResolutionFailed,
        int SurfaceMissingForResolvedKey,
        int DefaultsUsed)
    {
        public static readonly Stats Empty = new(
            MaxBlockId: -1,
            TotalFaces: 0,
            ResolvedFaces: 0,
            TextureKeyResolutionFailed: 0,
            SurfaceMissingForResolvedKey: 0,
            DefaultsUsed: 0);
    }

    public static DerivedSurface[] Build(
        IList<Block> blocks,
        IReadOnlyDictionary<AssetLocation, PbrMaterialSurface> surfaceByTexture,
        out Stats stats)
    {
        if (blocks is null) throw new ArgumentNullException(nameof(blocks));
        if (surfaceByTexture is null) throw new ArgumentNullException(nameof(surfaceByTexture));

        if (blocks.Count == 0)
        {
            stats = Stats.Empty;
            return Array.Empty<DerivedSurface>();
        }

        int maxId = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            Block? b = blocks[i];
            if (b is null) continue;
            if (b.BlockId > maxId) maxId = b.BlockId;
        }

        int len = checked((maxId + 1) * FacesPerBlock);
        var arr = new DerivedSurface[len];

        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = DerivedSurface.Default;
        }

        int resolvedFaces = 0;
        int keyResolutionFailed = 0;
        int surfaceMissing = 0;
        int defaultsUsed = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            Block? block = blocks[i];
            if (block is null) continue;

            int blockId = block.BlockId;
            if (blockId < 0 || blockId > maxId) continue;

            for (byte face = 0; face < FacesPerBlock; face++)
            {
                DerivedSurface ds = DerivedSurface.Default;

                if (!BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, face, out AssetLocation texLoc, out _))
                {
                    keyResolutionFailed++;
                    defaultsUsed++;
                    arr[blockId * FacesPerBlock + face] = ds;
                    continue;
                }

                if (!surfaceByTexture.TryGetValue(texLoc, out PbrMaterialSurface surf))
                {
                    surfaceMissing++;
                    defaultsUsed++;
                    arr[blockId * FacesPerBlock + face] = ds;
                    continue;
                }

                ds = new DerivedSurface(surf.DiffuseAlbedo, surf.SpecularF0);
                resolvedFaces++;
                arr[blockId * FacesPerBlock + face] = ds;
            }
        }

        int totalFaces = (maxId + 1) * FacesPerBlock;
        stats = new Stats(
            MaxBlockId: maxId,
            TotalFaces: totalFaces,
            ResolvedFaces: resolvedFaces,
            TextureKeyResolutionFailed: keyResolutionFailed,
            SurfaceMissingForResolvedKey: surfaceMissing,
            DefaultsUsed: defaultsUsed);

        return arr;
    }
}
