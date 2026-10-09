// PBR direct lighting params UBO
//
// Non-opaque uniforms for pbr_direct_lighting.fsh.

#ifndef PBR_DIRECT_LIGHTING_PARAMS_UBO_GLSL
#define PBR_DIRECT_LIGHTING_PARAMS_UBO_GLSL

@import "./vge_frame_ubo.glsl"

@import "./vge_lights_ubo.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgePbrDirectLightingParamsUBO
{
    mat4 toShadowMapSpaceMatrixNear;
    mat4 toShadowMapSpaceMatrixFar;

    // shadowRangeNear.x, shadowRangeFar.y, reserved.zw
    vec4 shadowRanges;

    // shadowZExtendNear.x, shadowZExtendFar.y, dropShadowIntensity.z, reserved.w
    vec4 shadowFloats0;

    // lightDirection.xyz
    vec4 lightDir0;

    // rgbaAmbientIn.xyz
    vec4 ambient0;

    // Solar irradiance in xyz; w reserved
    vec4 light0;

} vgePbrDirect;

// Matrices
#define invProjectionMatrix (vgeFrame.invProjectionMatrix)
#define invModelViewMatrix (vgeFrame.invViewMatrix)
#define toShadowMapSpaceMatrixNear (vgePbrDirect.toShadowMapSpaceMatrixNear)
#define toShadowMapSpaceMatrixFar (vgePbrDirect.toShadowMapSpaceMatrixFar)

#define zNear (vgeFrame.clipPlanes.x)
#define zFar (vgeFrame.clipPlanes.y)

#define shadowRangeNear (vgePbrDirect.shadowRanges.x)
#define shadowRangeFar (vgePbrDirect.shadowRanges.y)

#define shadowZExtendNear (vgePbrDirect.shadowFloats0.x)
#define shadowZExtendFar (vgePbrDirect.shadowFloats0.y)
#define dropShadowIntensity (vgePbrDirect.shadowFloats0.z)


#define lightDirection (vgePbrDirect.lightDir0.xyz)
#define rgbaAmbientIn (vgePbrDirect.ambient0.xyz)
#define rgbaLightIn (vgePbrDirect.light0.xyz)

#define pointLightsCount (int(vgeLights.lightCount))

// Point-light positions and colors come from the universal light snapshot.

#endif // PBR_DIRECT_LIGHTING_PARAMS_UBO_GLSL
