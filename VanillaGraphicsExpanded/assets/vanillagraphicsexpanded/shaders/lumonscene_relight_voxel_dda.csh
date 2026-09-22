#version 430 core
#define LUMON_TRACE_SCENE_COMPUTE 1

// Phase 22.9: Relight v1 (GL 4.3 compute, voxel DDA)
// Writes RGB irradiance + accumulation weight into IrradianceAtlas.

// Import deterministic hash (shared across LumOn shaders)
@import "./includes/squirrel3.glsl"
@import "./includes/lumon_trace_scene_trace.glsl"
@import "./includes/lumonscene_material_packing.glsl"
@import "./includes/lumonscene_relight_params_ubo.glsl"

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Physical atlases (sampled).
layout(binding = 0) uniform sampler2DArray vge_depthAtlas;    // r16f
layout(binding = 1) uniform sampler2DArray vge_materialAtlas; // rgba8 (RG oct normal, BA 16-bit surfaceId)

// Trace scene (v1 uses L0 only).
layout(binding = 2) uniform usampler3D vge_occL0;             // r32ui packed payload
layout(binding = 3) uniform sampler2D vge_lightColorLut;      // rgba16f
layout(binding = 4) uniform sampler2D vge_blockLevelScalarLut;// r16f
layout(binding = 5) uniform sampler2D vge_sunLevelScalarLut;  // r16f
layout(binding = 6) uniform usampler2D vge_materialPalette;   // rgba32ui (per-face surfaceIds)
layout(binding = 7) uniform usampler2D vge_surfaceLut;        // rgba32ui (albedo rgb 0..255, roughness 0..255)

// Output atlas (read+write for temporal accumulation).
layout(binding = 0, rgba16f) uniform image2DArray vge_irradianceAtlas;

layout(std430, binding = 0) buffer VgeRelightWork
{
    uvec4 vge_relightWork[]; // (physicalPageId, chunkSlot, patchId, virtualPageIndex)
};

struct VgePatchMeta
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

layout(std430, binding = 1) readonly buffer VgePatchMetadata
{
    VgePatchMeta vge_patchMeta[];
};

// Optional debug counters (enabled via vge_debugCountersEnabled).
// Bound by CPU to atomic counter binding=1, offsets in bytes.
layout(binding = 0, offset = 0) uniform atomic_uint vge_dbgRays;
layout(binding = 0, offset = 4) uniform atomic_uint vge_dbgHits;
layout(binding = 0, offset = 8) uniform atomic_uint vge_dbgMisses;
layout(binding = 0, offset = 12) uniform atomic_uint vge_dbgOobStarts;

// Shader code below uses these as identifiers (no parentheses), so keep them as macros.
#define vge_tileSizeTexels        (vgeRelightParams.atlasLayout.x)
#define vge_tilesPerAxis          (vgeRelightParams.atlasLayout.y)
#define vge_tilesPerAtlas         (vgeRelightParams.atlasLayout.z)
#define vge_borderTexels          (vgeRelightParams.atlasLayout.w)

#define vge_frameIndex            (vgeRelightParams.relightInts0.x)
#define vge_occResolution         (vgeRelightParams.relightInts0.y)

#define vge_texelsPerPagePerFrame (vgeRelightParams.relightUints0.x)
#define vge_raysPerTexel          (vgeRelightParams.relightUints0.y)
#define vge_maxDdaSteps           (vgeRelightParams.relightUints0.z)
#define vge_debugCountersEnabled  (vgeRelightParams.relightUints0.w)

#define vge_occOriginMinCell0     (vgeRelightParams.occOriginMinCell0.xyz)
#define vge_occRing0              (vgeRelightParams.occRing0.xyz)

// Packed payload decode (matches LumonSceneOccupancyPacking).
uint UnpackBlockLevel(uint p) { return (p >> 0u) & 63u; }
uint UnpackSunLevel(uint p) { return (p >> 6u) & 63u; }
uint UnpackLightId(uint p) { return (p >> 12u) & 63u; }
uint UnpackMaterialPaletteIndex(uint p) { return (p >> 18u) & 16383u; }

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

uint HitFaceIndexFromNormal(ivec3 hitN)
{
    if (hitN.x > 0) return 1u; // +X East
    if (hitN.x < 0) return 3u; // -X West
    if (hitN.y > 0) return 4u; // +Y Up
    if (hitN.y < 0) return 5u; // -Y Down
    if (hitN.z > 0) return 2u; // +Z South
    return 0u;                 // -Z North
}

uvec4 FetchSurfaceLut(uint surfaceId)
{
    return texelFetch(vge_surfaceLut, VgeLumonSceneSurfaceLutUv(surfaceId), 0);
}

bool ShadeHitFromOutsideCell(ivec3 outsideCell, ivec3 hitN, out vec3 radiance)
{
    radiance = vec3(0.0);
    uint geometry;
    if (lumonTraceSceneReadGeometry(outsideCell, TRACE_SCENE_SURFACE, geometry) != TRACE_SCENE_READY) return false;
    uint packedWord = texelFetch(traceSceneLegacy, lumonNearFieldWrap(outsideCell, nearFieldOriginResolution.w), 0).r;
    if (packedWord == 0u)
    {
        return true;
    }

    uint blockLevel = min(UnpackBlockLevel(packedWord), 32u);
    uint sunLevel = min(UnpackSunLevel(packedWord), 32u);
    uint lightId = min(UnpackLightId(packedWord), 63u);
    uint matIdx = UnpackMaterialPaletteIndex(packedWord);
    vec3 albedo = vec3(1.0);
    if (matIdx != 0u)
    {
        // Fetch surfaceId for the hit face and derive albedo from SurfaceLut.
        uvec4 faces = texelFetch(traceSceneFaces, ivec2(int(matIdx), 0), 0);
        if ((faces.w & 1u) == 0u) return false;
        uint faceIndex = HitFaceIndexFromNormal(hitN);
        uint surfaceId = VgeLumonSceneUnpackFaceSurfaceId(faces, faceIndex);
        uvec4 surf = FetchSurfaceLut(surfaceId);
        albedo = vec3(surf.xyz) * (1.0 / 255.0);
    }

    float blockScalar = texelFetch(vge_blockLevelScalarLut, ivec2(int(blockLevel), 0), 0).r;
    float sunScalar = texelFetch(vge_sunLevelScalarLut, ivec2(int(sunLevel), 0), 0).r;
    vec3 lightColor = texelFetch(vge_lightColorLut, ivec2(int(lightId), 0), 0).rgb;

    // v1: simple additive model (tune later).
    vec3 block = lightColor * (blockScalar * 32.0);
    vec3 sun = vec3(1.0) * (sunScalar * 32.0);
    radiance = (block + sun) * albedo;
    return true;
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
    uint batchIndexIn = w.z;
    uint virtualPageIndex = w.w & 0x7FFFFFFFu;

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
        // IMPORTANT: avoid visible striping and guarantee full coverage across frames.
        // CPU provides `batchIndexIn` for each page; each texel maps to a deterministic bucket.
        uint batchIndex = (batchCount <= 1u) ? 0u : (batchIndexIn % batchCount);
        uint bucket = (batchCount <= 1u) ? 0u : (Squirrel3HashU(physicalPageId, virtualPageIndex, linear) % batchCount);
        if (bucket != batchIndex)
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

    // Reconstruct normal from material atlas (RG stores oct-encoded normal).
    vec4 mat = texelFetch(vge_materialAtlas, atlasTexel, 0);
    vec3 normalWS = VgeLumonSceneDecodeNormalOct01(mat.rg);

    // Depth is currently constant (v1), but keep the read so the shader plumbing matches the intended approach.
    float depth = texelFetch(vge_depthAtlas, atlasTexel, 0).r;
    if (depth != 0.0) { }

    // Prefer real patch metadata (written during capture) for world-space reconstruction.
    VgePatchMeta meta = vge_patchMeta[physicalPageId];
    if (dot(meta.NormalWS.xyz, meta.NormalWS.xyz) > 1e-6)
    {
        normalWS = normalize(meta.NormalWS.xyz);
    }

    vec3 t, b;
    OrthonormalBasis(normalWS, t, b);

    uint seedBase = Squirrel3HashU(virtualPageIndex, physicalPageId, batchIndexIn);

    vec2 uv = (vec2(inTile) + vec2(0.5)) / float(max(1u, vge_tileSizeTexels));

    ivec3 originCell = ivec3(0);
    if (meta.Reserved1 == 1u) originCell = ivec3(floatBitsToInt(meta.OriginWS.w), floatBitsToInt(meta.AxisUWS.w), floatBitsToInt(meta.AxisVWS.w));
    vec3 surfacePos;
    if (dot(meta.AxisUWS.xyz, meta.AxisUWS.xyz) > 1e-6 && dot(meta.AxisVWS.xyz, meta.AxisVWS.xyz) > 1e-6)
    {
        surfacePos = meta.OriginWS.xyz + meta.AxisUWS.xyz * uv.x + meta.AxisVWS.xyz * uv.y;
    }
    else
    {
        // Fallback: keep the old v1 deterministic behavior used by the test suite.
        int res = max(1, vge_occResolution);
        ivec3 localCell = ivec3(
            int(seedBase % uint(res)),
            int(Squirrel3HashU(seedBase, 1u) % uint(res)),
            int(Squirrel3HashU(seedBase, 2u) % uint(res)));
        ivec3 worldCellAbs = vge_occOriginMinCell0 + localCell;

        vec3 worldPosAbs = vec3(worldCellAbs) + vec3(0.5);
        vec2 p = (uv * 2.0 - 1.0) * 2.0; // v1: 4-block wide proxy patch
        surfacePos = worldPosAbs + t * p.x + b * p.y;
    }

    // Push the origin slightly off the surface along the normal.
    // Voxel metadata retains an integer chunk anchor; only its small local offset is converted here.
    vec3 localOrigin = surfacePos + normalWS * 0.51;
    originCell += ivec3(floor(localOrigin));
    vec3 originFraction = fract(localOrigin);

    vec3 acc = vec3(0.0);
    uint validSamples = 0u;
    uint rays = max(1u, vge_raysPerTexel);
    uint seed0 = Squirrel3HashU(seedBase, linear, uint(vge_frameIndex));
    bool dbg = (vge_debugCountersEnabled != 0u);

    for (uint r = 0u; r < rays; r++)
    {
        if (dbg) { atomicCounterIncrement(vge_dbgRays); }

        float u0 = clamp(Squirrel3HashF(seed0, r, 0u), 1e-6, 1.0 - 1e-6);
        float u1 = Squirrel3HashF(seed0, r, 1u);
        vec2 u = vec2(u0, u1);
        vec3 localDir = CosineSampleHemisphere(u);
        vec3 dir = normalize(t * localDir.x + b * localDir.y + normalWS * localDir.z);

        LumonTraceSceneHit hit = lumonTraceScene(originCell, originFraction, dir, 1e20, int(vge_maxDdaSteps), TRACE_SCENE_SURFACE);
        if (hit.outcome == LUMON_NEAR_FIELD_HIT)
        {
            vec3 radiance;
            uint hitSurface;
            if (lumonTraceSceneReadSurface(hit.cell, HitFaceIndexFromNormal(hit.normal), hitSurface) &&
                ShadeHitFromOutsideCell(hit.cell + hit.normal, hit.normal, radiance))
            {
                if (dbg) atomicCounterIncrement(vge_dbgHits);
                acc += radiance / (1.0 + hit.distance * hit.distance);
                validSamples++;
            }
        }
        else if (dbg)
        {
            atomicCounterIncrement(vge_dbgMisses);
            if (hit.reason == TRACE_SCENE_OUTSIDE && hit.distance == 0.0) atomicCounterIncrement(vge_dbgOobStarts);
        }
    }
    // A bounded or unavailable trace establishes no sky sample and adds no temporal weight.
    if (validSamples < rays) atomicOr(vge_relightWork[workIndex].w, 0x80000000u);
    if (validSamples == 0u) return;
    acc /= float(validSamples);


    // Temporal accumulation: RGBA16F (RGB irradiance, A weight/sample count).
    vec4 prev = imageLoad(vge_irradianceAtlas, atlasTexel);
    float prevW = max(0.0, prev.a);
    float newW = min(prevW + 1.0, 1024.0);
    vec3 outRgb = (prev.rgb * prevW + acc) / max(1e-6, newW);
    imageStore(vge_irradianceAtlas, atlasTexel, vec4(outRgb, newW));
}
