#ifndef VGE_DIRECT_LIGHTING_INPUTS_GLSL
#define VGE_DIRECT_LIGHTING_INPUTS_GLSL
@import "./vge_frame_ubo.glsl"
@import "./vge_lights_ubo.glsl"
@import "./shadows/surface.glsl"
#define invProjectionMatrix (vgeFrame.invProjectionMatrix)
#define invModelViewMatrix (vgeFrame.invViewMatrix)
#define zNear (vgeFrame.clipPlanes.x)
#define zFar (vgeFrame.clipPlanes.y)
#define pointLightsCount (int(vgeLights.lightCount))
#endif
