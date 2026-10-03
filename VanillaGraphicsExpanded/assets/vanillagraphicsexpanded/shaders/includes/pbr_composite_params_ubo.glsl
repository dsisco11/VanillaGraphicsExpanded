// PBR composite params UBO
//
// Non-opaque uniforms for pbr_composite.fsh.

#ifndef PBR_COMPOSITE_PARAMS_UBO_GLSL
#define PBR_COMPOSITE_PARAMS_UBO_GLSL

@import "./vge_ubo_layout.glsl"

VGE_UBO_LAYOUT(VGE_UBO_OBJECT_BINDING) uniform VgePbrCompositeParamsUBO
{
    mat4 invProjectionMatrix;
    mat4 viewMatrix;

    // rgbaFogIn
    vec4 fogColor;

    // fogDensityIn.x, fogMinIn.y, underwater.z, optional refraction publication.w
    vec4 fogFloats0;

    // indirectTint.xyz, indirectIntensity.w
    vec4 indirectTint_intensity;

    // diffuseAOStrength.x, specularAOStrength.y, current-frame pre-overlay source.z, reserved.w
    vec4 aoStrengths;
    vec4 atmosphereAerial; // admitted altitude (km), horizon elevation, reserved.zw
    vec4 atmosphereSun; // admitted solar direction, reserved.w
    vec4 waterAbsorption; // camera absorption (m^-1), capture enabled
    vec4 waterScattering; // camera scattering (m^-1), starts in water
    vec4 waterCameraSource; // camera block/environment source times scattering coefficient, reserved.w
} vgePbrCompositeParams;

// Matrices
#define invProjectionMatrix (vgePbrCompositeParams.invProjectionMatrix)
#define viewMatrix (vgePbrCompositeParams.viewMatrix)

#define rgbaFogIn (vgePbrCompositeParams.fogColor)
#define fogDensityIn (vgePbrCompositeParams.fogFloats0.x)
#define fogMinIn (vgePbrCompositeParams.fogFloats0.y)

#define indirectTint (vgePbrCompositeParams.indirectTint_intensity.xyz)
#define indirectIntensity (vgePbrCompositeParams.indirectTint_intensity.w)
#define diffuseAOStrength (vgePbrCompositeParams.aoStrengths.x)
#define specularAOStrength (vgePbrCompositeParams.aoStrengths.y)

#endif // PBR_COMPOSITE_PARAMS_UBO_GLSL
