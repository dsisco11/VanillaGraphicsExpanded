#version 430 core

// Phase 22.9: Relight v1 (GL 4.3 compute, voxel DDA)
// Writes RGB irradiance + accumulation weight into IrradianceAtlas.

// Import deterministic hash (shared across LumOn shaders)
@import "./includes/squirrel3.glsl"
@import "./includes/lumonscene_trace_scene_occupancy.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Physical atlases (sampled).
layout(binding = 0) uniform sampler2DArray vge_depthAtlas;    // r16f
layout(binding = 1) uniform sampler2DArray vge_materialAtlas; // rgba8 (v1: normal in rgb)

// Trace scene (v1 uses L0 only).
layout(binding = 2) uniform usampler3D vge_occL0;             // r32ui packed payload
layout(binding = 3) uniform sampler2D vge_lightColorLut;      // rgba16f
layout(binding = 4) uniform sampler2D vge_blockLevelScalarLut;// r16f
layout(binding = 5) uniform sampler2D vge_sunLevelScalarLut;  // r16f

// Output atlas (read+write for temporal accumulation).
layout(binding = 0, rgba16f) uniform image2DArray vge_irradianceAtlas;

layout(std430, binding = 0) buffer VgeRelightWork
{
    uvec4 vge_relightWork[]; // (physicalPageId, chunkSlot, patchId, virtualPageIndex)
};

uniform uint vge_tileSizeTexels;
uniform uint vge_tilesPerAxis;
uniform uint vge_tilesPerAtlas;
uniform uint vge_borderTexels; // v1 default 0

uniform int vge_frameIndex;
uniform uint vge_texelsPerPagePerFrame;
uniform uint vge_raysPerTexel;
uniform uint vge_maxDdaSteps;

uniform ivec3 vge_occOriginMinCell0;
uniform ivec3 vge_occRing0;
uniform int vge_occResolution;

// Packed payload decode (matches LumonSceneOccupancyPacking).
uint UnpackBlockLevel(uint packed) { return (packed >> 0u) & 63u; }
uint UnpackSunLevel(uint packed) { return (packed >> 6u) & 63u; }
uint UnpackLightId(uint packed) { return (packed >> 12u) & 63u; }
uint UnpackMaterialPaletteIndex(uint packed) { return (packed >> 18u) & 16383u; }

bool OccInBounds(ivec3 worldCell) { return VgeOccInBoundsL0(worldCell, vge_occOriginMinCell0, vge_occResolution); }
uint SampleOccL0(ivec3 worldCell) { return VgeSampleOccL0(vge_occL0, worldCell, vge_occOriginMinCell0, vge_occRing0, vge_occResolution); }

vec3 CosineSampleHemisphere(vec2 u)
{
    float r = sqrt(u.x);
    float phi = 6.28318530718 * u.y;
    float x = r * cos(phi);
    float y = r * sin(phi);
    float z = sqrt(max(0.0, 1.0 - u.x));
    return vec3(x, y, z);
}

void OrthonormalBasis(vec3 n, out vec3 t, out vec3 b)
{
    vec3 up = (abs(n.z) < 0.999) ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
    t = normalize(cross(up, n));
    b = cross(n, t);
}

bool TraceDdaL0(vec3 origin, vec3 dir, out ivec3 hitCell, out ivec3 hitN, out float hitT)
{
    // Constrain to the occupancy volume; treat leaving the volume as a miss.
    ivec3 cell = ivec3(floor(origin));
    if (!OccInBounds(cell))
    {
        hitCell = ivec3(0);
        hitN = ivec3(0);
        hitT = 0.0;
        return false;
    }

    ivec3 step = ivec3(sign(dir));
    vec3 adir = abs(dir);

    vec3 tDelta = vec3(1e9);
    if (adir.x > 1e-6) tDelta.x = 1.0 / adir.x;
    if (adir.y > 1e-6) tDelta.y = 1.0 / adir.y;
    if (adir.z > 1e-6) tDelta.z = 1.0 / adir.z;

    vec3 cellf = vec3(cell);
    vec3 tMax = vec3(1e9);
    if (adir.x > 1e-6) tMax.x = (dir.x > 0.0) ? ((cellf.x + 1.0 - origin.x) * tDelta.x) : ((origin.x - cellf.x) * tDelta.x);
    if (adir.y > 1e-6) tMax.y = (dir.y > 0.0) ? ((cellf.y + 1.0 - origin.y) * tDelta.y) : ((origin.y - cellf.y) * tDelta.y);
    if (adir.z > 1e-6) tMax.z = (dir.z > 0.0) ? ((cellf.z + 1.0 - origin.z) * tDelta.z) : ((origin.z - cellf.z) * tDelta.z);

    for (uint i = 0u; i < vge_maxDdaSteps; i++)
    {
        if (tMax.x < tMax.y)
        {
            if (tMax.x < tMax.z)
            {
                cell.x += step.x;
                hitN = ivec3(-step.x, 0, 0);
                hitT = tMax.x;
                tMax.x += tDelta.x;
            }
            else
            {
                cell.z += step.z;
                hitN = ivec3(0, 0, -step.z);
                hitT = tMax.z;
                tMax.z += tDelta.z;
            }
        }
        else
        {
            if (tMax.y < tMax.z)
            {
                cell.y += step.y;
                hitN = ivec3(0, -step.y, 0);
                hitT = tMax.y;
                tMax.y += tDelta.y;
            }
            else
            {
                cell.z += step.z;
                hitN = ivec3(0, 0, -step.z);
                hitT = tMax.z;
                tMax.z += tDelta.z;
            }
        }

        if (!OccInBounds(cell))
        {
            break;
        }

        uint occ = SampleOccL0(cell);
        if (occ != 0u)
        {
            hitCell = cell;
            return true;
        }
    }

    hitCell = ivec3(0);
    hitN = ivec3(0);
    hitT = 0.0;
    return false;
}

vec3 ShadeHitFromOutsideCell(ivec3 outsideCell)
{
    uint packed = SampleOccL0(outsideCell);
    if (packed == 0u)
    {
        return vec3(0.0);
    }

    uint blockLevel = min(UnpackBlockLevel(packed), 32u);
    uint sunLevel = min(UnpackSunLevel(packed), 32u);
    uint lightId = min(UnpackLightId(packed), 63u);
    uint matIdx = UnpackMaterialPaletteIndex(packed);
    if (matIdx != 0u) { }

    float blockScalar = texelFetch(vge_blockLevelScalarLut, ivec2(int(blockLevel), 0), 0).r;
    float sunScalar = texelFetch(vge_sunLevelScalarLut, ivec2(int(sunLevel), 0), 0).r;
    vec3 lightColor = texelFetch(vge_lightColorLut, ivec2(int(lightId), 0), 0).rgb;

    // v1: simple additive model (tune later).
    vec3 block = lightColor * (blockScalar * 32.0);
    vec3 sun = vec3(1.0) * (sunScalar * 32.0);
    return block + sun;
}

void main()
{
    uvec2 inTile = gl_GlobalInvocationID.xy;
    uint workIndex = gl_WorkGroupID.z;

    if (inTile.x >= vge_tileSizeTexels || inTile.y >= vge_tileSizeTexels)
    {
        return;
    }

    uvec4 w = vge_relightWork[workIndex];
    uint physicalPageId = w.x;
    uint patchId = w.z;
    uint virtualPageIndex = w.w;

    if (physicalPageId == 0u)
    {
        return;
    }

    uint totalTexels = vge_tileSizeTexels * vge_tileSizeTexels;
    uint k = vge_texelsPerPagePerFrame;
    if (k == 0u)
    {
        return;
    }

    uint linear = inTile.y * vge_tileSizeTexels + inTile.x;
    if (k < totalTexels)
    {
        uint batchCount = (totalTexels + (k - 1u)) / k;
        uint seed = Squirrel3HashU(uint(vge_frameIndex), physicalPageId, patchId);
        uint batchIndex = (batchCount <= 1u) ? 0u : (seed % batchCount);
        if ((linear / k) != batchIndex)
        {
            return;
        }
    }

    uint pageIndex = physicalPageId - 1u;
    uint atlasIndex = pageIndex / vge_tilesPerAtlas;
    uint local = pageIndex - atlasIndex * vge_tilesPerAtlas;
    uint tileY = local / vge_tilesPerAxis;
    uint tileX = local - tileY * vge_tilesPerAxis;

    ivec2 base = ivec2(int(tileX * vge_tileSizeTexels), int(tileY * vge_tileSizeTexels));
    ivec2 texelXY = base + ivec2(inTile);
    ivec3 atlasTexel = ivec3(texelXY, int(atlasIndex));

    // Reconstruct normal from material atlas (v1 stores axis normal in RGB).
    vec3 n01 = texelFetch(vge_materialAtlas, atlasTexel, 0).rgb;
    vec3 normalWS = normalize(n01 * 2.0 - 1.0);

    // Depth is currently constant (v1), but keep the read so the shader plumbing matches the intended approach.
    float depth = texelFetch(vge_depthAtlas, atlasTexel, 0).r;
    if (depth != 0.0) { }

    // Pseudo surface position anchored in the occupancy volume (v1: not yet tied to real patch metadata).
    uint seedBase = Squirrel3HashU(virtualPageIndex, physicalPageId, patchId);
    int res = max(1, vge_occResolution);
    ivec3 localCell = ivec3(
        int(seedBase % uint(res)),
        int(Squirrel3HashU(seedBase, 1u) % uint(res)),
        int(Squirrel3HashU(seedBase, 2u) % uint(res)));
    ivec3 worldCell = vge_occOriginMinCell0 + localCell;

    vec3 t, b;
    OrthonormalBasis(normalWS, t, b);

    vec2 uv = (vec2(inTile) + vec2(0.5)) / float(max(1u, vge_tileSizeTexels));
    vec2 p = (uv * 2.0 - 1.0) * 2.0; // v1: 4-block wide proxy patch

    vec3 origin = vec3(worldCell) + vec3(0.5) + t * p.x + b * p.y + normalWS * 0.51;

    vec3 acc = vec3(0.0);
    uint rays = max(1u, vge_raysPerTexel);
    uint seed0 = Squirrel3HashU(seedBase, linear, uint(vge_frameIndex));

    for (uint r = 0u; r < rays; r++)
    {
        float u0 = clamp(Squirrel3HashF(seed0, r, 0u), 1e-6, 1.0 - 1e-6);
        float u1 = Squirrel3HashF(seed0, r, 1u);
        vec2 u = vec2(u0, u1);
        vec3 localDir = CosineSampleHemisphere(u);
        vec3 dir = normalize(t * localDir.x + b * localDir.y + normalWS * localDir.z);

        ivec3 hitCell;
        ivec3 hitN;
        float hitT;
        if (TraceDdaL0(origin, dir, hitCell, hitN, hitT))
        {
            ivec3 outsideCell = hitCell + hitN; // outside-face convention
            vec3 radiance = ShadeHitFromOutsideCell(outsideCell);
            float falloff = 1.0 / (1.0 + hitT * hitT);
            acc += radiance * falloff;
        }
    }

    acc /= float(rays);

    // Temporal accumulation: RGBA16F (RGB irradiance, A weight/sample count).
    vec4 prev = imageLoad(vge_irradianceAtlas, atlasTexel);
    float prevW = max(0.0, prev.a);
    float newW = min(prevW + 1.0, 1024.0);
    vec3 outRgb = (prev.rgb * prevW + acc) / max(1e-6, newW);
    imageStore(vge_irradianceAtlas, atlasTexel, vec4(outRgb, newW));
}
