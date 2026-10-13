// PBR composite params UBO
//
// Non-opaque uniforms for pbr_composite.fsh.

#ifndef PBR_COMPOSITE_PARAMS_UBO_GLSL
#define PBR_COMPOSITE_PARAMS_UBO_GLSL

@import "./vge_frame_ubo.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgePbrCompositeParamsUBO
{

    // underwater.x, optional refraction publication.y, raw camera-light adapter.z, reserved.w
    vec4 mediumFlags;

    // indirectTint.xyz, indirectIntensity.w
    vec4 indirectTint_intensity;

    // diffuseAOStrength.x, specularAOStrength.y, current-frame pre-overlay source.z, current-frame ambient visibility.w
    vec4 aoStrengths;
    vec4 atmosphereAerial; // altitude (km), horizon elevation, reserved.z, inverse lookup scale.w
    vec4 atmosphereSun; // admitted solar direction, reserved.w
    vec4 waterAbsorption; // camera absorption (m^-1), capture enabled
    vec4 waterScattering; // camera scattering (m^-1), starts in water
    vec4 waterCameraSource; // raw accessor RGBA when mediumFlags.z, otherwise physical camera source
    vec4 waterSourceUpper; // source envelope covering all boundaries and the camera, metadata validity.w
    vec4 waterCameraEnvironment; // physical E/pi for camera-medium shader evaluation
    vec4 scatteringSun;
    vec4 scatteringMoon;
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
#define VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
#define vge_atmosphereScatteringSun (vgePbrCompositeParams.scatteringSun.xyz)
#define vge_atmosphereScatteringMoon (vgePbrCompositeParams.scatteringMoon.xyz)

#endif // PBR_COMPOSITE_PARAMS_UBO_GLSL
