#ifndef VGE_CELESTIAL_DRAW
#define VGE_CELESTIAL_DRAW
@import "./vge_frame_ubo.glsl"
@import "./pbr_scene_storage.glsl"
@import "./atmosphere_sky_mapping.glsl"
layout(std140, binding=28) uniform CelestialDrawInputs { mat4 celestialToWorld; vec4 celestialEncoding; };
uniform samplerCube galaxyMap;
uniform sampler2D externalTransmission;
/** Reads extinction from the existing atmospheric model, rejecting planet-occluded directions. */
vec3 celestialTransmission(vec3 direction)
{
    float elevation = asin(clamp(direction.y,-1.0,1.0));
    if (elevation < celestialEncoding.y) return vec3(0);
    float row = atmSkyCoordinate(elevation, celestialEncoding.y);
    float count = float(textureSize(externalTransmission,0).x);
    return clamp(texture(externalTransmission,vec2((row*(count-1.0)+.5)/count,.5)).rgb,0.0,1.0);
}
/** Uses the perspective pixel's solid angle rather than treating every screen location equally. */
float celestialPixelArea(vec2 pixel)
{
    vec2 ndc = pixel / vgeFrame.screenSize * 2.0 - 1.0;
    vec2 slope = (ndc + vec2(vgeFrame.projectionMatrix[2].x,vgeFrame.projectionMatrix[2].y))
        / vec2(vgeFrame.projectionMatrix[0].x,vgeFrame.projectionMatrix[1].y);
    return 4.0 / (vgeFrame.screenSize.x*vgeFrame.screenSize.y
        * vgeFrame.projectionMatrix[0].x*vgeFrame.projectionMatrix[1].y*pow(1.0+dot(slope,slope),1.5));
}
#endif
