#ifndef VGE_SURFACE_SHADOWS_GLSL
#define VGE_SURFACE_SHADOWS_GLSL
layout(std140,binding=18) uniform SurfaceShadowParameters {
    uvec4 vgeShadowControl;
    vec4 vgeShadowOriginHigh;
    vec4 vgeShadowOriginLow;
    vec4 vgeShadowSunDirectionRadius;
    vec4 vgeShadowSolar;
    vec4 vgeShadowMoonDirectionRadius;
    vec4 vgeShadowLunar;
    vec4 vgeShadowBias;
    uvec4 vgeShadowSourceSlots[26];
};
#define vgeSurfaceSunDirection vgeShadowSunDirectionRadius.xyz
#define vgeSurfaceSunRadius vgeShadowSunDirectionRadius.w
#define vgeSurfaceSolar vgeShadowSolar.xyz
#define vgeSurfaceMoonDirection vgeShadowMoonDirectionRadius.xyz
#define vgeSurfaceMoonRadius vgeShadowMoonDirectionRadius.w
#define vgeSurfaceLunar vgeShadowLunar.xyz
layout(std430,binding=0) readonly buffer SurfaceShadowRequests { uint sampleInputs[]; };
layout(std430,binding=1) readonly buffer PageEntries { uint entries[]; };
layout(std430,binding=2) readonly buffer SurfaceShadowReadiness { uint vgeShadowReadiness[]; };
layout(std430,binding=5) buffer SurfaceShadowDiagnostics { uint vgeShadowFailures[]; };
layout(binding=33) uniform usampler2DArray shadowDepthPool;
uint vgeShadowRequestBase;
vec3 vgeShadowReceiver,vgeShadowNormal;
#define S(i) sampleInputs[vgeShadowRequestBase+uint(i)]
#define VGE_SHADOW_SURFACE_RECEIVER
@import "./sampling.glsl"
#undef VGE_SHADOW_SURFACE_RECEIVER
#undef S

/** Samples only the GPU-selected coherent projection set for this exact current-light interval. */
float vgeSurfaceShadowVisibility(int sourceSlot,vec3 originRelativePosition,vec3 worldNormal) {
    if(vgeShadowControl.x==0u || sourceSlot<0 || sourceSlot>=102) return 1.0;
    uint light=vgeShadowSourceSlots[sourceSlot/4][sourceSlot%4];
    if(light>=vgeShadowControl.y) return 1.0;
    uint o=light*32u;
    if(vgeShadowReadiness[o+12u]==0u ||
        any(notEqual(uvec2(vgeShadowReadiness[o+19u],vgeShadowReadiness[o+20u]),vgeShadowControl.zw))) return 1.0;
    vgeShadowReceiver=originRelativePosition;
    vgeShadowNormal=normalize(worldNormal);
    uint first=vgeShadowReadiness[o+21u],count=vgeShadowReadiness[o+22u];
    if(count==0u || first>sampleInputs[1] || count>sampleInputs[1]-min(first,sampleInputs[1])) {
        atomicAdd(vgeShadowFailures[light*4u],1u);return 1.0;
    }
    // Native admission selects one directional level or one coherent six-face
    // point mapping. Resolve taps directly through the bounded page-table hash;
    // never linearly search all demanded pages for every shaded pixel.
    vgeShadowRequestBase=sampleInputs[2]+first*192u;
    uint failures,stale;ivec3 addresses[9];float references[9];
    float result=shadowPcf(failures,stale,addresses,references);
    if(failures!=0u) { atomicAdd(vgeShadowFailures[light*4u],1u);atomicOr(vgeShadowFailures[light*4u+1u],failures); }
    return result;
}
#endif
