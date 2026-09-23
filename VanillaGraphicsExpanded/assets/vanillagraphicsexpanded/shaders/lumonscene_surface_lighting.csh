#version 430 core
#define LUMON_TRACE_SCENE_COMPUTE 1
@import "./includes/squirrel3.glsl"
@import "./includes/lumon_trace_scene_trace.glsl"
@import "./includes/lumonscene_material_packing.glsl"
@import "./includes/lumon_surface_lighting.glsl"
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
/** Seeds direct light, estimates a previous-generation bounce, or combines a complete page. */
void main()
{
    uint wi = gl_WorkGroupID.z;
    uvec4 item = work[wi];
    uint id = item.x, edge = lighting.layoutInfo.x, operation = lighting.layoutInfo.w;
    uvec2 xy = gl_GlobalInvocationID.xy;
    if (any(greaterThanEqual(xy, uvec2(edge)))) return;
    uint linear = xy.y * edge + xy.x;
    uint batchCount = (edge * edge + max(1u, lighting.sampling.x) - 1u) / max(1u, lighting.sampling.x);
    if (operation != 2u && linear % batchCount != item.z % batchCount) return;
    ivec3 address = surfaceAddress(id, ivec2(xy));
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
    // Hidden voxel faces have no exposed hemisphere. They are valid zero texels, not
    // failed ray batches; otherwise corners could keep an entire physical page pending.
    vec3 origin = position + n * 0.01;
    ivec3 cell = anchor + ivec3(floor(origin));
    uint outsideGeometry;
    if (lumonTraceSceneReadGeometry(cell, TRACE_SCENE_SURFACE, outsideGeometry) != TRACE_SCENE_READY)
    { atomicOr(work[wi].w, 0x80000000u); return; }
    if ((outsideGeometry & 3u) == 2u)
    {
        if (operation == 0u) imageStore(directIrradiance, address, vec4(0,0,0,1));
        if (operation != 2u) imageStore(indirectIrradiance, address, vec4(0,0,0,operation == 1u ? 1 : 0));
        else imageStore(nextOutgoing, address, vec4(0,0,0,1));
        return;
    }
    if (operation == 2u)
    {
        vec4 direct = imageLoad(directIrradiance, address);
        vec3 indirect = imageLoad(indirectIrradiance, address).rgb;
        vec3 outgoing = (direct.rgb + indirect) * albedo / 3.14159265359 + emission;
        imageStore(nextOutgoing, address, vec4(clamp(outgoing, vec3(0), vec3(65504)), direct.a));
        return;
    }
    if (operation == 0u)
    {
        uint geometry;
        if (lumonTraceSceneReadGeometry(cell, TRACE_SCENE_SURFACE, geometry) != TRACE_SCENE_READY || !finiteLight(emission))
        { atomicOr(work[wi].w, 0x80000000u); return; }
        uint light = texelFetch(traceSceneLegacy, lumonNearFieldWrap(cell, nearFieldOriginResolution.w), 0).r;
        float block = lighting.policy.x == 0u ? texelFetch(blockLevels, ivec2(int(min(light & 63u, 32u)),0),0).r : 0.0;
        float sun = texelFetch(sunLevels, ivec2(int(min((light >> 6u) & 63u,32u)),0),0).r;
        vec3 direct = 32.0 * (block * texelFetch(lightColors, ivec2(int((light >> 12u) & 63u),0),0).rgb + vec3(sun));
        if (!finiteLight(direct)) { atomicOr(work[wi].w, 0x80000000u); return; }
        imageStore(directIrradiance, address, vec4(clamp(direct, vec3(0), vec3(65504)),1));
        imageStore(indirectIrradiance, address, vec4(0));
        return;
    }
    vec3 tangent = normalize(cross(abs(n.z)<0.999 ? vec3(0,0,1) : vec3(0,1,0),n));
    vec3 bitangent = cross(n,tangent);
    vec3 sum = vec3(0);
    uint rays = max(1u,lighting.sampling.y);
    for (uint r=0u; r<rays; r++)
    {
        uint seed = Squirrel3HashU(id,linear,lighting.sampling.w);
        float u = clamp(Squirrel3HashF(seed,r,0u),0.000001,0.999999);
        float phi = 6.28318530718 * Squirrel3HashF(seed,r,1u);
        vec3 dir = tangent*(sqrt(u)*cos(phi)) + bitangent*(sqrt(u)*sin(phi)) + n*sqrt(1.0-u);
        LumonTraceSceneHit hit = lumonTraceScene(cell,fract(origin),dir,1e20,int(lighting.sampling.z),TRACE_SCENE_SURFACE);
        vec3 radiance; uint hitSurface;
        if (hit.outcome != LUMON_NEAR_FIELD_HIT ||
            !lumonTraceSceneReadSurface(hit.cell,faceOf(hit.normal),hitSurface) ||
            !sampleSurfaceLighting(hit.cell,hit.normal,hit.fraction,hitSurface,radiance))
        { atomicOr(work[wi].w,0x80000000u); return; }
        sum += radiance * 3.14159265359;
    }
    vec4 previous = imageLoad(indirectIrradiance,address);
    if (!finiteLight(previous.rgb) || isnan(previous.a) || isinf(previous.a)) previous = vec4(0);
    float weight = min(max(0.0,previous.a)+1.0,1024.0);
    imageStore(indirectIrradiance,address,vec4(clamp(mix(previous.rgb,sum/float(rays),1.0/weight),vec3(0),vec3(65504)),weight));
}
