using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Thread-safe mapping from TraceScene <c>materialPaletteIndex</c> values to a compact material palette payload,
/// suitable for uploading to <see cref="LumonSceneOccupancyClipmapGpuResources.MaterialPalette"/>.
/// </summary>
/// <remarks>
/// v1 palette payload:
/// - RGBA32UI, one texel per palette index.
/// - x/y/z: base color (0..255)
/// - w: roughness (0..255; v1 uses 255 for fully rough)
/// </remarks>
internal sealed class LumonSceneTraceSceneMaterialPaletteRegistry
{
    private readonly object gate = new();
    private readonly bool[] hasEntry = new bool[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries];
    private readonly uint[] paletteData = new uint[LumonSceneOccupancyClipmapGpuResources.MaxMaterialPaletteEntries * 4];

    private bool paletteDirty;

    public LumonSceneTraceSceneMaterialPaletteRegistry()
    {
        Reset();
    }

    public void Reset()
    {
        lock (gate)
        {
            Array.Clear(hasEntry, 0, hasEntry.Length);
            Array.Clear(paletteData, 0, paletteData.Length);

            // Index 0 is reserved for air / missing.
            hasEntry[0] = true;
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

    public void EnsureEntryForBlockId(ICoreClientAPI capi, int blockId, int materialPaletteIndex, BlockPos scratchPos)
    {
        if (capi is null) throw new ArgumentNullException(nameof(capi));
        if (scratchPos is null) throw new ArgumentNullException(nameof(scratchPos));

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
            if (hasEntry[materialPaletteIndex])
            {
                return;
            }

            uint r = 255u, g = 0u, b = 255u;

            try
            {
                var blocks = capi.World.Blocks;
                if (blockId >= 0 && blockId < blocks.Count)
                {
                    Block block = blocks[blockId];
                    int rgba = block.GetColorWithoutTint(capi, scratchPos);

                    // Vintage Story color ints are RGBA: 0xRRGGBBAA.
                    r = (uint)((rgba >> 24) & 0xFF);
                    g = (uint)((rgba >> 16) & 0xFF);
                    b = (uint)((rgba >> 8) & 0xFF);
                }
            }
            catch
            {
                // Keep magenta fallback.
            }

            int o = materialPaletteIndex * 4;
            paletteData[o + 0] = r;
            paletteData[o + 1] = g;
            paletteData[o + 2] = b;
            paletteData[o + 3] = 255u; // roughness (v1: fully rough)

            hasEntry[materialPaletteIndex] = true;
            paletteDirty = true;
        }
    }
}
