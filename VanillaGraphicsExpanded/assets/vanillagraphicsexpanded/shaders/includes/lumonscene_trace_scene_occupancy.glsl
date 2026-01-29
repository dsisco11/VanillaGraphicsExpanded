// LumOn TraceScene occupancy sampling helpers (L0).
//
// Contract:
// - worldCell is an integer cell coordinate in world space (block coords in v1).
// - originMinCell0 + occResolution define the valid window.
// - ring0 is applied as: tex = Wrap(local + ring0, occResolution)
// - Outside bounds => treat as empty (0), and rays exiting bounds => miss/sky.

int VgeModI(int x, int m)
{
    int r = x % m;
    return (r < 0) ? (r + m) : r;
}

bool VgeOccInBoundsL0(ivec3 worldCell, ivec3 originMinCell0, int occResolution)
{
    ivec3 local = worldCell - originMinCell0;
    return
        (uint)local.x < (uint)occResolution &&
        (uint)local.y < (uint)occResolution &&
        (uint)local.z < (uint)occResolution;
}

uint VgeSampleOccL0(
    usampler3D occL0,
    ivec3 worldCell,
    ivec3 originMinCell0,
    ivec3 ring0,
    int occResolution)
{
    ivec3 local = worldCell - originMinCell0;
    if ((uint)local.x >= (uint)occResolution ||
        (uint)local.y >= (uint)occResolution ||
        (uint)local.z >= (uint)occResolution)
    {
        return 0u;
    }

    ivec3 tex = ivec3(
        VgeModI(local.x + ring0.x, occResolution),
        VgeModI(local.y + ring0.y, occResolution),
        VgeModI(local.z + ring0.z, occResolution));

    return texelFetch(occL0, tex, 0).x;
}

