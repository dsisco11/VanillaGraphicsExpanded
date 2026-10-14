#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/celestial_draw.glsl"
layout(location=0) flat in vec2 starCentre;
layout(location=1) flat in vec3 starPower;
layout(location=2) flat in vec3 starWorldDirection;
layout(location=0) out vec4 outColor;
layout(location=1) out vec4 outGlow;
/** Integrates a normalized compact tent rather than sampling a moving point or adding a halo. */
float starCdf(float x)
{
    x = clamp(x,-1.0,1.0);
    return x < 0.0 ? .5*(x+1.0)*(x+1.0) : 1.0-.5*(1.0-x)*(1.0-x);
}
/** Keeps full-footprint normalization at clipping/occlusion boundaries; transmission reduces received flux once. */
void main()
{
    vec2 d = gl_FragCoord.xy-starCentre;
    float weight = (starCdf(d.x+.5)-starCdf(d.x-.5))*(starCdf(d.y+.5)-starCdf(d.y-.5));
    if (weight <= 0.0) discard;
    vec3 radiance = starPower * (weight/celestialPixelArea(gl_FragCoord.xy)) * celestialTransmission(starWorldDirection);
    outColor = vec4(VgeStoreSceneLighting(radiance),0);
    outGlow = vec4(0);
}
