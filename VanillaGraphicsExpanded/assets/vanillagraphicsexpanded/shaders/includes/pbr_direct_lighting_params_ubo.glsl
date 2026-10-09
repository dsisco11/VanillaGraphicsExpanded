// PBR direct lighting params UBO
//
// Non-opaque uniforms for pbr_direct_lighting.fsh.

#ifndef PBR_DIRECT_LIGHTING_PARAMS_UBO_GLSL
#define PBR_DIRECT_LIGHTING_PARAMS_UBO_GLSL

@import "./vge_frame_ubo.glsl"

#ifndef VGE_PBR_MAX_POINT_LIGHTS
  #define VGE_PBR_MAX_POINT_LIGHTS 100
#endif

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

    // pointLightsCount.x, reserved.yzw
    ivec4 pointLightsInts0;

    // View-space pointLight positions from the engine (xyz), w reserved
    vec4 pointLightPos[VGE_PBR_MAX_POINT_LIGHTS];

    // pointLight colors (rgb), w reserved
    vec4 pointLightColor[VGE_PBR_MAX_POINT_LIGHTS];
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

#define pointLightsCount (vgePbrDirect.pointLightsInts0.x)

vec3 VgePbrPointLightPos(int i) { return vgePbrDirect.pointLightPos[i].xyz; }
vec3 VgePbrPointLightColor(int i) { return vgePbrDirect.pointLightColor[i].xyz; }

#endif // PBR_DIRECT_LIGHTING_PARAMS_UBO_GLSL
