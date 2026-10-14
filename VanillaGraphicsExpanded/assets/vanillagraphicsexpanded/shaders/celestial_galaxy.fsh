#version 330 core
#extension GL_ARB_separate_shader_objects : require
@import "./includes/celestial_draw.glsl"
layout(location=0) out vec4 outColor;
layout(location=1) out vec4 outGlow;
/** Decodes generated HDR once, then applies the same external transmission used by individual stars. */
void main()
{
    vec2 ndc = gl_FragCoord.xy/vgeFrame.screenSize*2.0-1.0;
    vec4 a = vgeFrame.invCurrViewProjMatrix*vec4(ndc,-1,1);
    vec4 b = vgeFrame.invCurrViewProjMatrix*vec4(ndc,0,1);
    vec3 direction = normalize(b.xyz/b.w-a.xyz/a.w);
    vec3 celestial = transpose(mat3(celestialToWorld))*direction;
    vec3 radiance = texture(galaxyMap,celestial).rgb * celestialEncoding.x * celestialTransmission(direction);
    outColor = vec4(VgeStoreSceneLighting(radiance),0);
    outGlow = vec4(0);
}
