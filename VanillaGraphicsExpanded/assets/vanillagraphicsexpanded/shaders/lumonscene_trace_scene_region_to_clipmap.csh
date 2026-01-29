#version 430 core

// Phase 23.4: TraceScene region -> clipmap update (GL 4.3 compute)
// Reads packed 32^3 region payload words from an SSBO and writes directly into the ring-buffered
// occupancy clipmap textures (R32UI) using OriginMinCell + Ring mapping.

layout(local_size_x = 8, local_size_y = 8, local_size_z = 8) in;

layout(std430, binding = 0) readonly buffer VgeRegionPayloadWords
{
    uint vge_regionPayloadWords[];
};

struct VgeRegionUpdate
{
    ivec3 RegionCoord;     // in 32^3 units
    uint  SrcOffsetWords;  // into vge_regionPayloadWords
    uint  LevelMask;       // bit i => update level i
    uint  VersionOrPad;
};

layout(std430, binding = 1) readonly buffer VgeRegionUpdates
{
    VgeRegionUpdate vge_regionUpdates[];
};

// Count of valid region updates in the SSBO (written by CPU via atomic counter buffer).
layout(binding = 0, offset = 0) uniform atomic_uint vge_regionUpdateCount;

// Destination images: bind OccupancyLevels[i] to image unit i.
layout(binding = 0, r32ui) writeonly uniform uimage3D vge_occLevels[8];

uniform int vge_levels;      // <= 8
uniform int vge_resolution;  // per axis
uniform ivec3 vge_originMinCell[8];
uniform ivec3 vge_ring[8];

const uint VGE_REGION_SIZE = 32u;
const uint VGE_REGION_SHIFT = 5u;

int Wrap(int x, int m)
{
    int r = x % m;
    return r < 0 ? r + m : r;
}

bool TryMapLevelCellToTexel(int level, ivec3 levelCell, out ivec3 texel)
{
    ivec3 local = levelCell - vge_originMinCell[level];
    if ((uint)local.x >= (uint)vge_resolution ||
        (uint)local.y >= (uint)vge_resolution ||
        (uint)local.z >= (uint)vge_resolution)
    {
        texel = ivec3(0);
        return false;
    }

    ivec3 ring = vge_ring[level];
    texel = ivec3(
        Wrap(local.x + ring.x, vge_resolution),
        Wrap(local.y + ring.y, vge_resolution),
        Wrap(local.z + ring.z, vge_resolution));
    return true;
}

bool IsRepresentativeSampleForLevel(ivec3 worldCell, int level)
{
    if (level <= 0)
    {
        return true;
    }

    int spacing = 1 << level;
    int mask = spacing - 1;
    int half = spacing >> 1;

    return ((worldCell.x & mask) == half)
        && ((worldCell.y & mask) == half)
        && ((worldCell.z & mask) == half);
}

void main()
{
    // We pack region updates into gl_WorkGroupID.z as:
    // updateIndex = workGroupZ / groupsPerRegionZ
    // groupZWithin = workGroupZ % groupsPerRegionZ
    const uint groupsPerRegionZ = 4u; // 32 / local_size_z
    uint updateIndex = gl_WorkGroupID.z / groupsPerRegionZ;
    uint groupZWithin = gl_WorkGroupID.z - updateIndex * groupsPerRegionZ;

    uint updateCount = atomicCounter(vge_regionUpdateCount);
    if (updateIndex >= updateCount)
    {
        return;
    }

    VgeRegionUpdate upd = vge_regionUpdates[updateIndex];

    uvec3 local = uvec3(
        gl_WorkGroupID.x * gl_WorkGroupSize.x + gl_LocalInvocationID.x,
        gl_WorkGroupID.y * gl_WorkGroupSize.y + gl_LocalInvocationID.y,
        groupZWithin * gl_WorkGroupSize.z + gl_LocalInvocationID.z);

    if (local.x >= VGE_REGION_SIZE || local.y >= VGE_REGION_SIZE || local.z >= VGE_REGION_SIZE)
    {
        return;
    }

    uint linear = (local.z * VGE_REGION_SIZE + local.y) * VGE_REGION_SIZE + local.x;
    uint payload = vge_regionPayloadWords[upd.SrcOffsetWords + linear];

    ivec3 worldCell = upd.RegionCoord * int(VGE_REGION_SIZE) + ivec3(local);

    int maxLevel = min(max(vge_levels, 0), 8);

    for (int level = 0; level < maxLevel; level++)
    {
        if (((upd.LevelMask >> uint(level)) & 1u) == 0u)
        {
            continue;
        }

        if (!IsRepresentativeSampleForLevel(worldCell, level))
        {
            continue;
        }

        ivec3 levelCell = ivec3(worldCell.x >> level, worldCell.y >> level, worldCell.z >> level);

        ivec3 texel;
        if (!TryMapLevelCellToTexel(level, levelCell, texel))
        {
            continue;
        }

        imageStore(vge_occLevels[level], texel, uvec4(payload, 0u, 0u, 0u));
    }
}

