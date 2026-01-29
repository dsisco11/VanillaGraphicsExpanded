#version 430 core

// Phase 22.11: Mesh-card capture v1 (GL 4.3 compute)
// For each MeshCardCaptureWork item, fills the corresponding physical tile in:
// - DepthAtlas (r16f): signed displacement along card normal (depth in card space)
// - MaterialAtlas (rgba8): v1 stores a per-texel normal in RGB, alpha=valid

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(binding = 0, r16f) writeonly uniform image2DArray vge_depthAtlas;
layout(binding = 1, rgba8) writeonly uniform image2DArray vge_materialAtlas;

layout(std430, binding = 0) buffer VgeMeshCardCaptureWork
{
    // (physicalPageId, triangleOffset, triangleCount, unused)
    uvec4 vge_meshCardCaptureWork[];
};

struct VgePatchMetadata
{
    vec4 OriginWS;
    vec4 AxisUWS;
    vec4 AxisVWS;
    vec4 NormalWS;

    uint VirtualBasePageX;
    uint VirtualBasePageY;
    uint VirtualSizePagesX;
    uint VirtualSizePagesY;

    uint ChunkSlot;
    uint PatchId;
    uint Reserved0;
    uint Reserved1;
};

layout(std430, binding = 1) readonly buffer VgePatchMetadataBuffer
{
    VgePatchMetadata vge_patchMetadata[];
};

struct VgeTriangle
{
    vec4 P0;
    vec4 P1;
    vec4 P2;
    vec4 N0; // xyz: face normal (world), w unused
};

layout(std430, binding = 2) readonly buffer VgeTriangles
{
    VgeTriangle vge_triangles[];
};

uniform uint vge_tileSizeTexels;
uniform uint vge_tilesPerAxis;
uniform uint vge_tilesPerAtlas;
uniform uint vge_borderTexels; // v1 default 0

uniform float vge_captureDepthRange; // signed depth range is [-range, +range]

bool IntersectRayTriangle(vec3 ro, vec3 rd, vec3 v0, vec3 v1, vec3 v2, out float t)
{
    vec3 e1 = v1 - v0;
    vec3 e2 = v2 - v0;
    vec3 p = cross(rd, e2);
    float det = dot(e1, p);
    if (abs(det) < 1e-8)
    {
        t = 0.0;
        return false;
    }

    float invDet = 1.0 / det;
    vec3 s = ro - v0;
    float u = dot(s, p) * invDet;
    if (u < 0.0 || u > 1.0)
    {
        t = 0.0;
        return false;
    }

    vec3 q = cross(s, e1);
    float v = dot(rd, q) * invDet;
    if (v < 0.0 || (u + v) > 1.0)
    {
        t = 0.0;
        return false;
    }

    t = dot(e2, q) * invDet;
    return t >= 0.0;
}

void main()
{
    uvec3 gid = gl_GlobalInvocationID;
    uint workIndex = gid.z;

    uvec2 inTile = gid.xy;
    if (inTile.x >= vge_tileSizeTexels || inTile.y >= vge_tileSizeTexels)
    {
        return;
    }

    uvec4 w = vge_meshCardCaptureWork[workIndex];
    uint physicalPageId = w.x;
    uint triOffset = w.y;
    uint triCount = w.z;

    if (physicalPageId == 0u)
    {
        return;
    }

    uint pageIndex = physicalPageId - 1u;
    uint atlasIndex = pageIndex / vge_tilesPerAtlas;
    uint local = pageIndex - atlasIndex * vge_tilesPerAtlas;
    uint tileY = local / vge_tilesPerAxis;
    uint tileX = local - tileY * vge_tilesPerAxis;

    ivec2 base = ivec2(int(tileX * vge_tileSizeTexels), int(tileY * vge_tileSizeTexels));
    ivec2 texelXY = base + ivec2(inTile);
    ivec3 texel = ivec3(texelXY, int(atlasIndex));

    if (vge_borderTexels != 0u) { }

    VgePatchMetadata meta = vge_patchMetadata[physicalPageId];

    vec3 originWS = meta.OriginWS.xyz;
    vec3 axisUWS = meta.AxisUWS.xyz;
    vec3 axisVWS = meta.AxisVWS.xyz;
    vec3 normalWS = normalize(meta.NormalWS.xyz);

    vec2 uv01 = (vec2(inTile) + vec2(0.5)) / float(max(1u, vge_tileSizeTexels));
    vec3 planePointWS = originWS + axisUWS * uv01.x + axisVWS * uv01.y;

    float range = max(1e-6, vge_captureDepthRange);
    vec3 ro = planePointWS - normalWS * range;
    vec3 rd = normalWS;

    float bestAbsDepth = 1e30;
    float bestDepth = 0.0;
    vec3 bestN = normalWS;
    bool hit = false;

    uint end = triOffset + triCount;
    for (uint i = triOffset; i < end; i++)
    {
        VgeTriangle tri = vge_triangles[i];
        vec3 p0 = tri.P0.xyz;
        vec3 p1 = tri.P1.xyz;
        vec3 p2 = tri.P2.xyz;

        float t;
        if (!IntersectRayTriangle(ro, rd, p0, p1, p2, t))
        {
            continue;
        }

        if (t > (2.0 * range))
        {
            continue;
        }

        float depth = -range + t;
        float absDepth = abs(depth);
        if (absDepth < bestAbsDepth)
        {
            bestAbsDepth = absDepth;
            bestDepth = depth;

            vec3 n = tri.N0.xyz;
            float len = length(n);
            if (len > 1e-12)
            {
                n /= len;
            }
            else
            {
                n = normalWS;
            }

            if (dot(n, normalWS) < 0.0)
            {
                n = -n;
            }

            bestN = n;
            hit = true;
        }
    }

    if (!hit)
    {
        imageStore(vge_depthAtlas, texel, vec4(0.0));
        vec3 n01 = normalWS * 0.5 + 0.5;
        imageStore(vge_materialAtlas, texel, vec4(n01, 0.0));
        return;
    }

    imageStore(vge_depthAtlas, texel, vec4(bestDepth));
    vec3 n01 = bestN * 0.5 + 0.5;
    imageStore(vge_materialAtlas, texel, vec4(n01, 1.0));
}

