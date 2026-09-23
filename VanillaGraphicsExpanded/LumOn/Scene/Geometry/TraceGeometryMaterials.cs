using System;
using System.Collections.Generic;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>One collision-free material identity space for both geometry consumers in a scene generation.</summary>
internal sealed class TraceGeometryMaterials
{
    public const int Capacity = 16384;
    private readonly object gate = new();
    private readonly Dictionary<int, uint> indices = new();
    private readonly uint[] faces = new uint[Capacity * 4];
    private readonly byte[] colors = new byte[Capacity * 48];
    private readonly LumonScenePbrSurfaceLutRegistry surfaces = new();
    private readonly LumonSceneTraceSceneLightIdRegistry lights = new();
    private long revision;
    private TraceGeometryTables? cached;

    #region Worker material resolution
    /// <summary>Resolves once per block per scene; unresolved flags never turn opaque geometry into air.</summary>
    public uint Resolve(Block block)
    {
        if (block.Id == 0) return 0;
        lock (gate)
        {
            if (indices.TryGetValue(block.Id, out uint index)) return index;
            if (indices.Count + 1 >= Capacity) return 0;
            index = (uint)indices.Count + 1;
            indices.Add(block.Id, index);
            bool surfaceReady = true, hitReady = true;
            for (byte face = 0; face < 6; face++)
            {
                if (!BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, face, out var key, out _) ||
                    !PbrMaterialRegistry.Instance.TryGetSurface(key, out var surface))
                { surfaceReady = hitReady = false; continue; }
                ushort id = surfaces.GetOrAssignSurfaceId(key);
                surfaceReady &= id != 0;
                faces[index * 4 + face / 2] |= (uint)id << ((face % 2) * 16);
                if (!PbrMaterialRegistry.Instance.TryGetDerivedSurface(block.Id, face, out var derived))
                { hitReady = false; continue; }
                Vector3 baseColor = Vector3.Clamp(surface.DiffuseAlbedo + surface.SpecularF0 - new Vector3(0.04f * (1 - surface.Metallic)), Vector3.Zero, Vector3.One);
                uint diffuse = TraceGeometryVoxel.PackLight(new(derived.DiffuseAlbedo, 0));
                uint emission = TraceGeometryVoxel.PackLight(new(baseColor * surface.Emissive, 0));
                for (int c = 0; c < 4; c++)
                {
                    colors[index * 48 + face * 8 + c] = (byte)(diffuse >> (c * 8));
                    colors[index * 48 + face * 8 + 4 + c] = (byte)(emission >> (c * 8));
                }
            }
            // Preserve the engine block identity beside readiness so delayed CPU hits can reject replaced geometry.
            faces[index * 4 + 3] = (surfaceReady ? 1u : 0u) | (hitReady ? 2u : 0u) | ((uint)block.Id << 2);
            // Mirror hit readiness in unused diffuse alpha to stay within the fragment sampler budget.
            for (int face = 0; face < 6; face++) colors[index * 48 + face * 8 + 3] = hitReady ? (byte)255 : (byte)0;
            revision++;
            return index;
        }
    }

    /// <summary>Shares the existing legacy RGB quantization while tracking table publication revisions.</summary>
    public uint ResolveLight(int rgb)
    {
        lock (gate)
        {
            uint id = (uint)lights.GetOrAssignLightId(rgb);
            if (lights.TryCopyAndClearDirtyLut(lightTable)) revision++;
            return id;
        }
    }
    #endregion

    private float[] lightTable = CreateWhiteLightTable();
    private uint[] surfaceTable = new uint[65536 * 4];

    #region Render publication snapshots
    /// <summary>Copies immutable tables under the same lock used for worker identity assignment.</summary>
    public TraceGeometryTables Snapshot()
    {
        lock (gate)
        {
            if (cached?.Revision == revision) return cached;
            surfaces.TryCopyAndClearDirtyLut(surfaceTable);
            return cached = new(revision, faces, colors, surfaceTable, lightTable);
        }
    }

    /// <summary>Matches the legacy neutral fallback for unused light-color entries.</summary>
    private static float[] CreateWhiteLightTable()
    {
        var table = new float[256]; Array.Fill(table, 1); return table;
    }
    #endregion
}
