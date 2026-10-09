// PBR composite params UBO
//
// Non-opaque uniforms for pbr_composite.fsh.

#ifndef PBR_COMPOSITE_PARAMS_UBO_GLSL
#define PBR_COMPOSITE_PARAMS_UBO_GLSL

@import "./vge_frame_ubo.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgePbrCompositeParamsUBO
{

    // underwater.x, optional refraction publication.y, reserved.zw
    vec4 mediumFlags;

    // indirectTint.xyz, indirectIntensity.w
    vec4 indirectTint_intensity;

    // diffuseAOStrength.x, specularAOStrength.y, current-frame pre-overlay source.z, current-frame ambient visibility.w
    vec4 aoStrengths;
    vec4 atmosphereAerial; // admitted altitude (km), horizon elevation, reserved.zw
    vec4 atmosphereSun; // admitted solar direction, reserved.w
    vec4 waterAbsorption; // camera absorption (m^-1), capture enabled
    vec4 waterScattering; // camera scattering (m^-1), starts in water
    vec4 waterCameraSource; // camera block/environment source times scattering coefficient, reserved.w
} vgePbrCompositeParams;

// Matrices
#define invProjectionMatrix (vgeFrame.invProjectionMatrix)
#define viewMatrix (vgeFrame.viewMatrix)

#define rgbaFogIn (vgeFrame.fog0)
#define fogDensityIn (vgeFrame.fog0.w)
#define fogMinIn (vgeFrame.fogMinimum)

#define indirectTint (vgePbrCompositeParams.indirectTint_intensity.xyz)
#define indirectIntensity (vgePbrCompositeParams.indirectTint_intensity.w)
#define diffuseAOStrength (vgePbrCompositeParams.aoStrengths.x)
#define specularAOStrength (vgePbrCompositeParams.aoStrengths.y)

#endif // PBR_COMPOSITE_PARAMS_UBO_GLSL
