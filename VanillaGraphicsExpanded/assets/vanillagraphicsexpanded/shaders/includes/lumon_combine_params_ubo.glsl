// LumOn combine params UBO
//
// Non-opaque uniforms for lumon_combine.fsh.

#ifndef LUMON_COMBINE_PARAMS_UBO_GLSL
#define LUMON_COMBINE_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgeLumOnCombineParamsUBO
{
    // indirectTint.xyz, indirectIntensity.w
    vec4 indirectTint_intensity;

    // diffuseAOStrength.x, specularAOStrength.y, reserved.zw
    vec4 aoStrengths;
} vgeLumOnCombineParams;

#define indirectTint (vgeLumOnCombineParams.indirectTint_intensity.xyz)
#define indirectIntensity (vgeLumOnCombineParams.indirectTint_intensity.w)

#define diffuseAOStrength (vgeLumOnCombineParams.aoStrengths.x)
#define specularAOStrength (vgeLumOnCombineParams.aoStrengths.y)

#endif // LUMON_COMBINE_PARAMS_UBO_GLSL
