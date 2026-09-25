#version 430 core
#define LUMON_TRACE_SCENE_COMPUTE 1
@import "./includes/squirrel3.glsl"
@import "./includes/lumon_trace_scene_trace.glsl"
@import "./includes/lumonscene_material_packing.glsl"
@import "./includes/lumon_surface_lighting.glsl"
#define SURFACE_DIAGNOSTICS_ENABLED (lighting.policy.w != 0u)
@import "./includes/surface_work_diagnostics.glsl"
layout(local_size_x=8, local_size_y=8, local_size_z=1) in;
layout(std430, binding=0) buffer SurfaceWork { uvec4 work[]; };
layout(binding=3) uniform sampler2D lightColors;
layout(binding=4) uniform sampler2D blockLevels;
layout(binding=5) uniform sampler2D sunLevels;
layout(binding=7) uniform usampler2D surfaces;
layout(binding=0, rgba16f) uniform image2DArray indirectIrradiance;
layout(binding=1, rgba16f) uniform image2DArray directIrradiance;
layout(binding=2, rgba16f) writeonly uniform image2DArray nextOutgoing;
/** Rejects invalid inputs before conversion to bounded half-float storage. */
bool finiteLight(vec3 value) { return !any(isnan(value)) && !any(isinf(value)); }
/** Maps an axial normal into the engine face order. */
uint faceOf(ivec3 n) { return n.x > 0 ? 1u : n.x < 0 ? 3u : n.y > 0 ? 4u : n.y < 0 ? 5u : n.z > 0 ? 2u : 0u; }
/** Seeds or refreshes direct light, estimates a bounce, combines valid texels, or resets a new page. */
void main()
{
    uint wi = gl_WorkGroupID.z;
    uvec4 item = work[wi];
    uint id = item.x, edge = lighting.layoutInfo.x, operation = lighting.layoutInfo.w;
    uvec2 xy = gl_GlobalInvocationID.xy;
    if (any(greaterThanEqual(xy, uvec2(edge)))) return;
    uint linear = xy.y * edge + xy.x;
    uint batchCount = (edge * edge + max(1u, lighting.sampling.x) - 1u) / max(1u, lighting.sampling.x);
    if ((operation < 2u || operation == 4u) && linear % batchCount != item.z % batchCount) return;
    surfaceCount(SD_TEXELS);
    ivec3 address = surfaceAddress(id, ivec2(xy));
    if (operation == 3u)
    {
        imageStore(directIrradiance, address, vec4(0));
        imageStore(indirectIrradiance, address, vec4(0));
        imageStore(nextOutgoing, address, vec4(0));
        surfaceCount(SD_COMPLETED);
        return;
    }
    SurfacePatch meta = patches[id];
    vec3 n = normalize(meta.normal.xyz);
    ivec3 anchor = ivec3(floatBitsToInt(meta.origin.w), floatBitsToInt(meta.axisU.w), floatBitsToInt(meta.axisV.w));
    vec2 uv = (vec2(xy) + 0.5) / float(edge);
    vec3 position = meta.origin.xyz + meta.axisU.xyz * uv.x + meta.axisV.xyz * uv.y;
    vec4 mat = texelFetch(capturedMaterial, address, 0);
    uint surfaceId = uint(round(mat.b * 255.0)) | uint(round(mat.a * 255.0)) << 8u;
    uvec4 material = texelFetch(surfaces, VgeLumonSceneSurfaceLutUv(surfaceId), 0);
    vec3 albedo = clamp(vec3(material.xyz) / 255.0, 0.0, 1.0);
    vec3 emission = lighting.policy.x != 0u ? vec3(unpackHalf2x16(material.w).y) : vec3(0);
    if (operation == 2u)
    {
        // The page was fully captured. Per-texel direct validity distinguishes seeded
        // material from untouched storage; unresolved indirect samples retain their old mean.
        vec4 direct = imageLoad(directIrradiance, address);
        vec4 indirect = imageLoad(indirectIrradiance, address);
        bool zeroSurface = surfaceId == 0u || direct.a == 2.0;
        if ((direct.a != 1.0 && direct.a != 2.0) || !finiteLight(direct.rgb) || !finiteLight(indirect.rgb) ||
            isnan(indirect.a) || isinf(indirect.a) || (!zeroSurface && !finiteLight(emission)))
        {
            surfaceCount((direct.a != 1.0 && direct.a != 2.0) ? SD_UNSEEDED : SD_NONFINITE);
            imageStore(nextOutgoing, address, vec4(0));
            return;
        }
        vec3 outgoing = zeroSurface ? vec3(0) : (direct.rgb + indirect.rgb) * albedo / 3.14159265359 + emission;
        if (!finiteLight(outgoing)) { surfaceCount(SD_NONFINITE); imageStore(nextOutgoing, address, vec4(0)); return; }
        imageStore(nextOutgoing, address, vec4(clamp(outgoing, vec3(0), vec3(65504)), 1));
        surfaceCount(SD_COMPLETED);
        atomicOr(work[wi].w, 0x40000000u);
        return;
    }
    vec4 directState = imageLoad(directIrradiance, address);
    if (operation == 0u && (directState.a == 1.0 || directState.a == 2.0)) { surfaceCount(SD_UNCHANGED); return; }
    if (operation == 1u && directState.a != 1.0 && directState.a != 2.0)
    { surfaceCount(SD_UNSEEDED); atomicOr(work[wi].w, 0x80000000u); return; }
    // A captured empty source is initialized zero, not an uncaptured or unknown texel.
    // It needs no exterior light query. Unsupported and unpublished captures remain gated on the CPU.
    if (surfaceId == 0u || (operation == 1u && directState.a == 2.0))
    {
        surfaceCount(surfaceId == 0u ? SD_EMPTY : SD_HIDDEN); surfaceCount(SD_COMPLETED);
        if (operation == 0u || operation == 4u) imageStore(directIrradiance, address, vec4(0,0,0,1));
        imageStore(indirectIrradiance, address, vec4(0,0,0,operation == 1u ? 1 : 0));
        atomicOr(work[wi].w, 0x40000000u);
        return;
    }
    vec3 origin = position + n * 0.01;
    ivec3 cell = anchor + ivec3(floor(origin));
    uint outsideGeometry;
    int originStatus = lumonTraceSceneReadGeometry(cell, TRACE_SCENE_SURFACE, outsideGeometry);
    if (originStatus != TRACE_SCENE_READY)
    { surfaceOriginFailure(originStatus); atomicOr(work[wi].w, 0x80000000u); return; }
    // Preserve hidden-face identity in direct alpha so combining cannot add their material emission.
    if ((outsideGeometry & 3u) == 2u)
    {
        surfaceCount(SD_HIDDEN); surfaceCount(SD_COMPLETED);
        if (operation == 0u || operation == 4u) imageStore(directIrradiance, address, vec4(0,0,0,2));
        imageStore(indirectIrradiance, address, vec4(0,0,0,operation == 1u ? 1 : 0));
        atomicOr(work[wi].w, 0x40000000u);
        return;
    }
    if (operation == 0u || operation == 4u)
    {
        uint geometry;
        if (lumonTraceSceneReadGeometry(cell, TRACE_SCENE_SURFACE, geometry) != TRACE_SCENE_READY || !finiteLight(emission))
        { surfaceCount(SD_NONFINITE); atomicOr(work[wi].w, 0x80000000u); return; }
        uint light = texelFetch(traceSceneLegacy, lumonNearFieldWrap(cell, nearFieldOriginResolution.w), 0).r;
        float block = lighting.policy.x == 0u ? texelFetch(blockLevels, ivec2(int(min(light & 63u, 32u)),0),0).r : 0.0;
        float sun = texelFetch(sunLevels, ivec2(int(min((light >> 6u) & 63u,32u)),0),0).r;
        vec3 direct = 32.0 * (block * texelFetch(lightColors, ivec2(int((light >> 12u) & 63u),0),0).rgb + vec3(sun));
        if (!finiteLight(direct)) { surfaceCount(SD_NONFINITE); atomicOr(work[wi].w, 0x80000000u); return; }
        imageStore(directIrradiance, address, vec4(clamp(direct, vec3(0), vec3(65504)),1));
        // Refresh replaces only resolved direct light; retained indirect history adapts separately.
        if (operation == 0u) imageStore(indirectIrradiance, address, vec4(0));
        surfaceCount(SD_COMPLETED);
        atomicOr(work[wi].w, 0x40000000u);
        return;
    }
    vec3 tangent = normalize(cross(abs(n.z)<0.999 ? vec3(0,0,1) : vec3(0,1,0),n));
    vec3 bitangent = cross(n,tangent);
    vec3 sum = vec3(0);
    uint rays = max(1u,lighting.sampling.y);
    for (uint r=0u; r<rays; r++)
    {
        surfaceCount(SD_RAYS);
        uint seed = Squirrel3HashU(id,linear,lighting.sampling.w);
        float u = clamp(Squirrel3HashF(seed,r,0u),0.000001,0.999999);
        float phi = 6.28318530718 * Squirrel3HashF(seed,r,1u);
        vec3 dir = tangent*(sqrt(u)*cos(phi)) + bitangent*(sqrt(u)*sin(phi)) + n*sqrt(1.0-u);
        LumonTraceSceneHit hit = lumonTraceScene(cell,fract(origin),dir,1e20,
            int(lighting.sampling.z),TRACE_SCENE_SURFACE,int(lighting.policy.y));
        // The vanilla sunlight seed already supplies effective direct sky/sun irradiance.
        // A proven sky ray completes this indirect sample with zero additional energy;
        // it still counts in the full ray denominator and the completed-batch weight.
        if (hit.outcome == LUMON_NEAR_FIELD_SKY) { surfaceCount(SD_SKY); continue; }
        vec3 radiance; uint hitSurface;
        if (hit.outcome != LUMON_NEAR_FIELD_HIT)
        {
            surfaceCount(hit.outcome == LUMON_NEAR_FIELD_BUDGET ? SD_BUDGET :
                hit.outcome == LUMON_NEAR_FIELD_CLEAR ? SD_DISTANCE :
                hit.reason == TRACE_SCENE_OUTSIDE ? SD_OUTSIDE :
                hit.reason == TRACE_SCENE_UNSUPPORTED ? SD_UNSUPPORTED : SD_UNPUBLISHED);
            atomicOr(work[wi].w,0x80000000u); return;
        }
        surfaceCount(SD_HIT);
        if (!lumonTraceSceneReadSurface(hit.cell,faceOf(hit.normal),hitSurface))
        { surfaceCount(SD_HIT_MATERIAL); atomicOr(work[wi].w,0x80000000u); return; }
        if (!sampleSurfaceLighting(hit.cell,hit.normal,hit.fraction,hitSurface,radiance))
        { surfaceCount(SD_HIT_LIGHTING); atomicOr(work[wi].w,0x80000000u); return; }
        sum += radiance * 3.14159265359;
    }
    vec3 estimate = sum / float(rays);
    // A failed/nonfinite batch must not age history or replace valid lighting with an artificial zero.
    if (!finiteLight(estimate)) { surfaceCount(SD_NONFINITE); atomicOr(work[wi].w, 0x80000000u); return; }
    vec4 previous = imageLoad(indirectIrradiance,address);
    if (!finiteLight(previous.rgb) || isnan(previous.a) || isinf(previous.a)) previous = vec4(0);
    // Match UE radiosity: blend using the previous count, then cap the stored next count.
    // A lower runtime limit takes effect after this successful sample; unresolved work changes nothing.
    float previousWeight = max(0.0,previous.a);
    float weight = min(previousWeight+1.0,float(max(1u,lighting.policy.z)));
    float alpha = 1.0/(1.0+previousWeight);
    imageStore(indirectIrradiance,address,vec4(clamp(mix(previous.rgb,estimate,alpha),vec3(0),vec3(65504)),weight));
    surfaceCount(SD_COMPLETED);
    atomicOr(work[wi].w, 0x40000000u);
}
