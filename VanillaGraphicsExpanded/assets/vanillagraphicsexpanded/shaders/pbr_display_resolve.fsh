#version 330 core
@import "./includes/pbr_color.glsl"
@import "./includes/lumon_common.glsl"

uniform sampler2D primaryScene;
uniform sampler2D primaryDepth;
layout(location = 0) out vec4 outColor;

/** Converts deferred geometry; the legacy sky is already display-referred. */
void main()
{
    ivec2 pixel = ivec2(gl_FragCoord.xy);
    vec4 scene = texelFetch(primaryScene, pixel, 0);
    float depth = texelFetch(primaryDepth, pixel, 0).r;
    outColor = lumonIsSky(depth) ? scene : vec4(VgeResolveDisplay(scene.rgb), scene.a);
}
