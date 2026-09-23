using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Thread-safe mapping from texture keys to compact surface ids, plus a LUT payload suitable for uploading to
/// <see cref="Geometry.TraceGeometryGpuScene.Surfaces"/>.
/// </summary>
/// <remarks>
/// v1 payload:
/// - RGBA32UI, one texel per surface id.
/// - x/y/z: base color (0..255) (linear)
/// - w: low byte roughness (0..255), upper half binary16 neutral emission in engine units
/// </remarks>
internal sealed class LumonScenePbrSurfaceLutRegistry
{
    public const int MaxSurfaceEntries = 65536; // 16-bit surfaceId space.

    private const byte EntryMissing = 0;
    private const byte EntryResolved = 1;

    private readonly object gate = new();
    private readonly Dictionary<AssetLocation, ushort> idByTexture = new();
    private readonly byte[] entryState = new byte[MaxSurfaceEntries];
    private readonly uint[] lutData = new uint[MaxSurfaceEntries * 4];

    private bool lutDirty;

    public LumonScenePbrSurfaceLutRegistry()
    {
        Reset();
    }

    public void Reset()
    {
        lock (gate)
        {
            idByTexture.Clear();
            Array.Clear(entryState, 0, entryState.Length);
            Array.Clear(lutData, 0, lutData.Length);

            // Surface id 0 is reserved as a default fallback.
            WriteSurfaceUnsafe(
                surfaceId: 0,
                surface: PbrMaterialSurface.Default);
            entryState[0] = EntryResolved;
            lutDirty = true;
        }
    }

    public bool TryCopyAndClearDirtyLut(uint[] dst)
    {
        if (dst is null) throw new ArgumentNullException(nameof(dst));
        if (dst.Length < lutData.Length) throw new ArgumentException("Destination buffer too small.", nameof(dst));

        lock (gate)
        {
            if (!lutDirty)
            {
                return false;
            }

            Array.Copy(lutData, dst, lutData.Length);
            lutDirty = false;
            return true;
        }
    }

    public ushort GetOrAssignSurfaceId(AssetLocation textureKey)
    {
        if (textureKey is null)
        {
            return 0;
        }

        textureKey = NormalizeTextureKey(textureKey);

        lock (gate)
        {
            if (idByTexture.TryGetValue(textureKey, out ushort existing))
            {
                // Upgrade stale entries if possible.
                if (entryState[existing] != EntryResolved
                    && PbrMaterialRegistry.Instance.TryGetSurface(textureKey, out PbrMaterialSurface surf))
                {
                    WriteSurfaceUnsafe(existing, surf);
                    entryState[existing] = EntryResolved;
                    lutDirty = true;
                }

                return existing;
            }

            int next = idByTexture.Count + 1; // reserve 0
            if (next >= MaxSurfaceEntries)
            {
                return 0;
            }

            ushort id = (ushort)next;
            idByTexture[textureKey] = id;

            if (PbrMaterialRegistry.Instance.TryGetSurface(textureKey, out PbrMaterialSurface resolved))
            {
                WriteSurfaceUnsafe(id, resolved);
                entryState[id] = EntryResolved;
            }
            else
            {
                // Placeholder: default surface until registry resolves this key.
                WriteSurfaceUnsafe(id, PbrMaterialSurface.Default);
                entryState[id] = EntryMissing;
            }

            lutDirty = true;
            return id;
        }
    }

    /// <summary>
    /// Opportunistically upgrades any entries that were created before the PBR registry had a resolved surface
    /// for their texture key.
    /// </summary>
    public int UpgradeUnresolvedEntries(int maxToUpgrade)
    {
        if (maxToUpgrade <= 0)
        {
            return 0;
        }

        int upgraded = 0;

        lock (gate)
        {
            foreach ((AssetLocation key, ushort id) in idByTexture)
            {
                if (entryState[id] == EntryResolved)
                {
                    continue;
                }

                if (!PbrMaterialRegistry.Instance.TryGetSurface(key, out PbrMaterialSurface surf))
                {
                    continue;
                }

                WriteSurfaceUnsafe(id, surf);
                entryState[id] = EntryResolved;
                lutDirty = true;

                upgraded++;
                if (upgraded >= maxToUpgrade)
                {
                    break;
                }
            }
        }

        return upgraded;
    }

    /// <summary>Packs diffuse reflectance, roughness and HDR neutral emission into one immutable surface row.</summary>
    private void WriteSurfaceUnsafe(int surfaceId, PbrMaterialSurface surface)
    {
        int o = surfaceId * 4;

        lutData[o + 0] = Float01ToByte(surface.DiffuseAlbedo.X);
        lutData[o + 1] = Float01ToByte(surface.DiffuseAlbedo.Y);
        lutData[o + 2] = Float01ToByte(surface.DiffuseAlbedo.Z);
        // Upper half stores neutral emitted radiance in engine units without LDR clipping.
        float emission = float.IsFinite(surface.Emissive) ? Math.Clamp(surface.Emissive * 32f, 0f, 65504f) : 0f;
        lutData[o + 3] = Float01ToByte(surface.Roughness) | ((uint)BitConverter.HalfToUInt16Bits((Half)emission) << 16);
    }

    private static AssetLocation NormalizeTextureKey(AssetLocation key)
    {
        // Match PBR registry normalization: lowercase + "textures/" prefix + extensionless.
        string domain = (key.Domain ?? "game").ToLowerInvariant();

        string path = (key.Path ?? string.Empty).Replace('\\', '/').ToLowerInvariant().TrimStart('/');
        if (!path.StartsWith("textures/", StringComparison.Ordinal))
        {
            path = "textures/" + path;
        }

        int lastSlash = path.LastIndexOf('/');
        int lastDot = path.LastIndexOf('.');
        if (lastDot > lastSlash)
        {
            path = path[..lastDot];
        }

        return new AssetLocation(domain, path);
    }

    private static uint Float01ToByte(float v)
    {
        if (!(v >= 0f)) return 0u; // handles NaN
        if (v >= 1f) return 255u;
        return (uint)(v * 255f + 0.5f);
    }
}
