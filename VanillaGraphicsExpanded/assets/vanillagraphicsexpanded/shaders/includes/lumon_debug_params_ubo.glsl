// LumOn debug params UBO
//
// Non-opaque uniforms previously declared in lumon_debug_uniforms.glsl.

#ifndef LUMON_DEBUG_PARAMS_UBO_GLSL
#define LUMON_DEBUG_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeLumOnDebugParamsUBO
{
    // x=enabled, y=tileSizeTexels, z=tilesPerAxis, w=tilesPerAtlas
    ivec4 lumonSceneInts0;

    // Reserved padding preserves the existing parameter layout; geometry uses its shared UBO.
    ivec4 reservedGeometry[3];

    // temporalAlpha.x, depthRejectThreshold.y, normalRejectThreshold.z, reserved.w
    vec4 temporalFloats0;

    // debugMode.x, gatherAtlasSource.y, reserved.zw
    ivec4 debugInts0;

    // indirectTint.xyz, indirectIntensity.w
    vec4 compositeTint_intensity;

    // diffuseAOStrength.x, specularAOStrength.y, worldProbeComparisonReady.z, worldProbeEffectGain.w
    vec4 aoStrengths;
} vgeLumOnDebugParams;

// Phase 22: LumonScene surface cache debug inputs
#define vge_lumonSceneEnabled (vgeLumOnDebugParams.lumonSceneInts0.x)
#define vge_lumonSceneTileSizeTexels (vgeLumOnDebugParams.lumonSceneInts0.y)
#define vge_lumonSceneTilesPerAxis (vgeLumOnDebugParams.lumonSceneInts0.z)
#define vge_lumonSceneTilesPerAtlas (vgeLumOnDebugParams.lumonSceneInts0.w)



// Temporal config
#define temporalAlpha (vgeLumOnDebugParams.temporalFloats0.x)
#define depthRejectThreshold (vgeLumOnDebugParams.temporalFloats0.y)
#define normalRejectThreshold (vgeLumOnDebugParams.temporalFloats0.z)

// Debug selection
#define debugMode (vgeLumOnDebugParams.debugInts0.x)
#define gatherAtlasSource (vgeLumOnDebugParams.debugInts0.y)

// Composite parameters
#define indirectIntensity (vgeLumOnDebugParams.compositeTint_intensity.w)
#define indirectTint (vgeLumOnDebugParams.compositeTint_intensity.xyz)
#define worldProbeEffectGain (vgeLumOnDebugParams.aoStrengths.w)
#define worldProbeComparisonReady (vgeLumOnDebugParams.aoStrengths.z > 0.5)
#define diffuseAOStrength (vgeLumOnDebugParams.aoStrengths.x)
#define specularAOStrength (vgeLumOnDebugParams.aoStrengths.y)

#endif // LUMON_DEBUG_PARAMS_UBO_GLSL
