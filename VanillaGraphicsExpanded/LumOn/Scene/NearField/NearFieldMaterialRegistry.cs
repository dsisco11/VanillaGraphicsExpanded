using System;
using System.Collections.Generic;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Collision-free near-field hit materials, stored as six diffuse RGB/emission RGB pairs per entry.</summary>
internal sealed class NearFieldMaterialRegistry
{
    public const int Capacity = 16384;
    public const int Width = 256;
    public const int Height = Capacity * 12 / Width;
    private readonly object gate = new();
    private readonly Dictionary<int, uint> indices = new();
    private readonly float[] data = new float[Capacity * 12 * 4];
    private bool dirty = true;
    public const long MaximumUploadBytes = (long)Capacity * 12 * 4 * sizeof(float);

    #region Material Publication
    /// <summary>Returns zero when face data is unresolved or the bounded palette is exhausted.</summary>
    public uint Resolve(Block block)
    {
        lock (gate)
        {
            if (indices.TryGetValue(block.Id, out uint existing)) return existing;
            if (indices.Count + 1 >= Capacity) return 0;
            var faces = new Vector4[12];
            for (byte face = 0; face < 6; face++)
            {
                if (!BlockFaceTextureKeyResolver.TryResolveBaseTextureLocation(block, face, out var texture, out _) ||
                    !PbrMaterialRegistry.Instance.TryGetSurface(texture, out var surface) ||
                    !PbrMaterialRegistry.Instance.TryGetDerivedSurface(block.Id, face, out var derived)) return 0;
                faces[face * 2] = new Vector4(derived.DiffuseAlbedo, 0);
                // Registry diffuse and F0 share the same base color. Recover it without
                // dividing by (1 - metallic), which would discard fully metallic emission.
                var baseColor = Vector3.Clamp(surface.DiffuseAlbedo + surface.SpecularF0 -
                    new Vector3(0.04f * (1f - surface.Metallic)), Vector3.Zero, Vector3.One);
                faces[face * 2 + 1] = new Vector4(baseColor * surface.Emissive, 0);
            }
            uint index = (uint)indices.Count + 1;
            indices.Add(block.Id, index);
            for (int face = 0; face < 12; face++)
            {
                int o = ((int)index * 12 + face) * 4;
                data[o] = faces[face].X; data[o + 1] = faces[face].Y;
                data[o + 2] = faces[face].Z; data[o + 3] = faces[face].W;
            }
            dirty = true;
            return index;
        }
    }

    /// <summary>Returns an immutable upload copy after materials change, synchronized with concurrent capture.</summary>
    public float[]? TakeUpload()
    {
        lock (gate)
        {
            if (!dirty) return null;
            dirty = false;
            return (float[])data.Clone();
        }
    }

    /// <summary>Invalidates all identities when the owning scene is rebuilt.</summary>
    public void Reset()
    {
        lock (gate)
        {
            indices.Clear();
            Array.Clear(data);
            dirty = true;
        }
    }
    #endregion
}
