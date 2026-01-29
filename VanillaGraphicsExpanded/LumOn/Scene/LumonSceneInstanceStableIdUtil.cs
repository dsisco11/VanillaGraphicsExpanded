using System;

using VanillaGraphicsExpanded.Noise;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Deterministic stable-id helpers for mesh-card patch keys.
/// </summary>
/// <remarks>
/// Requirements:
/// - Must be stable across chunk remeshes and across sessions as long as the underlying world state is unchanged.
/// - Must not depend on managed object identity / <see cref="object.GetHashCode"/>.
/// - Intended usage: <see cref="LumonScenePatchKey.CreateMeshCard"/> where the key is (instanceStableId, cardIndex).
/// </remarks>
internal static class LumonSceneInstanceStableIdUtil
{
    // Purpose: keep stable IDs partitioned from other hashing uses.
    private const uint DomainSalt0 = 0x4C_53_49_44u; // 'LSID'
    private const uint DomainSalt1 = 0x56_47_45_22u; // 'VGE'+phase marker

    /// <summary>
    /// Computes a deterministic stable id for a "placed thing" at a world block position.
    /// </summary>
    /// <remarks>
    /// Use this for rotated static block meshes that are tesselated into chunk meshes; the block position is the natural stable anchor.
    /// Include extra discriminators (e.g. block id, variant id, rotation) so different instances at the same pos do not collide.
    /// </remarks>
    public static ulong FromWorldBlock(
        int worldX,
        int worldY,
        int worldZ,
        int blockId,
        int variantOrRotation = 0,
        int extra = 0)
    {
        unchecked
        {
            uint ux = (uint)worldX;
            uint uy = (uint)worldY;
            uint uz = (uint)worldZ;
            uint ub = (uint)blockId;
            uint ur = (uint)variantOrRotation;
            uint ue = (uint)extra;

            uint lo = Squirrel3Noise.HashU(DomainSalt0, ux, uy) ^ Squirrel3Noise.HashU(uz, ub, ur) ^ ue;
            uint hi = Squirrel3Noise.HashU(DomainSalt1, uz, ux) ^ Squirrel3Noise.HashU(uy, ur, ub);
            return ((ulong)hi << 32) | lo;
        }
    }

    /// <summary>
    /// Computes a deterministic stable id for an entity-based instance.
    /// </summary>
    /// <remarks>
    /// Prefer a persisted game-provided id when available (e.g. entity id / block-entity id),
    /// and add a small <paramref name="subIndex"/> to distinguish multiple cards within the same instance.
    /// </remarks>
    public static ulong FromEntityId(long entityId, int subIndex = 0)
    {
        unchecked
        {
            ulong eid = (ulong)entityId;
            uint loIn = (uint)(eid & 0xFFFF_FFFFu);
            uint hiIn = (uint)(eid >> 32);

            uint lo = Squirrel3Noise.HashU(DomainSalt0, loIn, hiIn) ^ (uint)subIndex;
            uint hi = Squirrel3Noise.HashU(DomainSalt1, hiIn, loIn) ^ Squirrel3Noise.HashU((uint)subIndex);
            return ((ulong)hi << 32) | lo;
        }
    }
}

