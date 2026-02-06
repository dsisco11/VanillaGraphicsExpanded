// ============================================================================
// VGE Common UBO Schemas (std140)
//
// These blocks define the long-term "UBO-only" interface for non-opaque shader
// parameters.
//
// Notes:
// - GLSL < 420 cannot compile layout(binding=...) without 420pack.
// - For GLSL fallback paths, bindings are assigned from C# via glUniformBlockBinding.
// - Keep fields vec4/ivec4-aligned to simplify CPU-side packing.
// ============================================================================

#ifndef VGE_COMMON_UBOS_GLSL
#define VGE_COMMON_UBOS_GLSL

@import "./vge_ubo_bindings.glsl"

#if __VERSION__ >= 420
  #define VGE_UBO_LAYOUT(BINDING) layout(std140, binding = BINDING)
#else
  #define VGE_UBO_LAYOUT(BINDING) layout(std140)
#endif

// ---------------------------------------------------------------------------
// Frame/View block (per-frame constants)
// ---------------------------------------------------------------------------

VGE_UBO_LAYOUT(VGE_UBO_FRAME_BINDING) uniform VgeFrameUBO
{
    mat4 projectionMatrix;
    mat4 viewMatrix;
    mat4 invProjectionMatrix;
    mat4 invViewMatrix;

    mat4 prevViewProjMatrix;
    mat4 currViewProjMatrix;

    // screenSize.xy, timeSeconds.z, frameIndex.w
    vec4 frame0;

    // cameraPosWS.xyz, reserved.w
    vec4 cameraPosWS;

    // fogColor.xyz, fogDensity.w
    vec4 fog0;
} vgeFrame;

// ---------------------------------------------------------------------------
// Object block (per-draw / per-dispatch constants)
// ---------------------------------------------------------------------------

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeObjectUBO
{
    mat4 modelMatrix;

    // objectFlags.x, reserved.yzw
    uvec4 object0;
} vgeObject;

// ---------------------------------------------------------------------------
// Material block (material parameters)
// ---------------------------------------------------------------------------

VGE_UBO_LAYOUT(VGE_UBO_MATERIAL_BINDING) uniform VgeMaterialUBO
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

VGE_UBO_LAYOUT(VGE_UBO_LIGHTS_BINDING) uniform VgeLightsUBO
{
    // x = lightCount
    ivec4 lights0;

    // posWS.xyz, intensity.w
    vec4 lightPosIntensity[VGE_MAX_LIGHTS];

    // color.rgb, reserved.w
    vec4 lightColor[VGE_MAX_LIGHTS];
} vgeLights;

#undef VGE_UBO_LAYOUT

#endif // VGE_COMMON_UBOS_GLSL
