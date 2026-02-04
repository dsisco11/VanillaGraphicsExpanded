using System;

using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Thread-safe mapping from TraceScene <c>materialPaletteIndex</c> values to compact per-face surface ids.
/// </summary>
/// <remarks>
/// v1 palette payload (uploaded to <see cref="LumonSceneOccupancyClipmapGpuResources.MaterialPalette"/>):
/// - RGBA32UI, one texel per palette index.
/// - Packs 6x 16-bit surface ids (block faces 0..5) into 3x 32-bit lanes:
///   - x: face0 | (face1&lt;&lt;16)
///   - y: face2 | (face3&lt;&lt;16)
///   - z: face4 | (face5&lt;&lt;16)
///   - w: reserved (0 for now)
/// 
/// Surface ids index into <see cref="LumonSceneOccupancyClipmapGpuResources.SurfaceLut"/> which stores
/// derived PBR values (albedo/roughness) per unique base texture key.
/// </remarks>
internal sealed class LumonSceneTraceSceneMaterialPaletteRegistry
{
    private const byte EntryMissing = 0;
    private const byte EntryResolved = 1;

    private readonly object gate = new();
    private readonly LumonScenePbrSurfaceLutRegistry surfaceLut;

    private readonly byte[] entryState = new byte[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries];
    private readonly uint[] paletteData = new uint[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries * 4];
    private bool paletteDirty;

    public LumonSceneTraceSceneMaterialPaletteRegistry(LumonScenePbrSurfaceLutRegistry surfaceLut)
    {
        this.surfaceLut = surfaceLut ?? throw new ArgumentNullException(nameof(surfaceLut));
        Reset();
    }

    public void Reset()
    {
        lock (gate)
        {
            Array.Clear(entryState, 0, entryState.Length);
            Array.Clear(paletteData, 0, paletteData.Length);

            // Index 0 is reserved for air / missing.
            entryState[0] = EntryResolved;
            paletteDirty = true;
        }
    }

    public bool TryCopyAndClearDirtyPalette(uint[] dst)
    {
        if (dst is null) throw new ArgumentNullException(nameof(dst));
        if (dst.Length < paletteData.Length) throw new ArgumentException("Destination buffer too small.", nameof(dst));

        lock (gate)
        {
            if (!paletteDirty)
            {
                return false;
            }

            Array.Copy(paletteData, dst, paletteData.Length);
            paletteDirty = false;
            return true;
        }
    }

    public void EnsureEntryForBlockId(ICoreClientAPI capi, int blockId, int materialPaletteIndex)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));

        if ((uint)materialPaletteIndex >= (uint)LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries)
        {
            return;
        }

        if (materialPaletteIndex == 0)
        {
            return;
        }

        lock (gate)
        {
            if (entryState[materialPaletteIndex] == EntryResolved)
            {
                return;
            }
        }

        Block? block = capi.World?.GetBlock(blockId);
        if (block is null)
        {
            return;
        }

        // Resolve per-face base texture keys and remap to surface ids.
        ushort f0 = ResolveFaceSurfaceId(block, faceIndex: 0);
        ushort f1 = ResolveFaceSurfaceId(block, faceIndex: 1);
        ushort f2 = ResolveFaceSurfaceId(block, faceIndex: 2);
        ushort f3 = ResolveFaceSurfaceId(block, faceIndex: 3);
        ushort f4 = ResolveFaceSurfaceId(block, faceIndex: 4);
        ushort f5 = ResolveFaceSurfaceId(block, faceIndex: 5);

        lock (gate)
        {
            if (entryState[materialPaletteIndex] == EntryResolved)
            {
                return;
            }

            int o = materialPaletteIndex * 4;
            paletteData[o + 0] = Pack2x16(f0, f1);
            paletteData[o + 1] = Pack2x16(f2, f3);
            paletteData[o + 2] = Pack2x16(f4, f5);
            paletteData[o + 3] = 0u;

            entryState[materialPaletteIndex] = EntryResolved;
            paletteDirty = true;
        }
    }

    private ushort ResolveFaceSurfaceId(Block block, byte faceIndex)
    {
        if (!BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, faceIndex, out AssetLocation texKey, out _))
        {
            return 0;
        }

        return surfaceLut.GetOrAssignSurfaceId(texKey);
    }

    private static uint Pack2x16(ushort lo, ushort hi)
    {
        return (uint)lo | ((uint)hi << 16);
    }
}
