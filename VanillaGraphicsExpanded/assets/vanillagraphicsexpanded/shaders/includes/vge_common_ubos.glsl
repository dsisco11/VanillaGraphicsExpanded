// ============================================================================
// VGE Common UBO Schemas (std140)
//
// These blocks define the long-term "UBO-only" interface for non-opaque shader
// parameters.
//
// Notes:
// - GLSL 330 consumers require 420pack for explicit uniform-block bindings.
// - CPU packing follows std140 offsets, including typed scalar frame fields.
// ============================================================================

#ifndef VGE_COMMON_UBOS_GLSL
#define VGE_COMMON_UBOS_GLSL

#extension GL_ARB_shading_language_420pack : require

@import "./vge_ubo_bindings.glsl"

// ---------------------------------------------------------------------------
// Frame/View block (per-frame constants)
// ---------------------------------------------------------------------------

@import "./vge_frame_ubo.glsl"

// ---------------------------------------------------------------------------
// Object block (per-draw / per-dispatch constants)
// ---------------------------------------------------------------------------

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeObjectUBO
{
    mat4 modelMatrix;

    // objectFlags.x, reserved.yzw
    uvec4 object0;
} vgeObject;

// ---------------------------------------------------------------------------
// Material block (material parameters)
// ---------------------------------------------------------------------------

layout(std140, binding = VGE_UBO_MATERIAL_BINDING) uniform VgeMaterialUBO
{
    // baseColor.rgb, alpha.w
    vec4 baseColor;

    // pbr: roughness.x, metallic.y, emissive.z, reserved.w
    vec4 pbr0;
} vgeMaterial;

// ---------------------------------------------------------------------------
// Lights block (dynamic light lists)
// ---------------------------------------------------------------------------

#ifndef VGE_MAX_LIGHTS
  #define VGE_MAX_LIGHTS 64
#endif

layout(std140, binding = VGE_UBO_LIGHTS_BINDING) uniform VgeLightsUBO
{
    // x = lightCount
    ivec4 lights0;

    // posWS.xyz, intensity.w
    vec4 lightPosIntensity[VGE_MAX_LIGHTS];

    // color.rgb, reserved.w
    vec4 lightColor[VGE_MAX_LIGHTS];
} vgeLights;



#endif // VGE_COMMON_UBOS_GLSL
