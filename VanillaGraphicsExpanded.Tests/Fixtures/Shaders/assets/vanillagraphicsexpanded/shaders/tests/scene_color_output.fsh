#version 450 core
@import "../includes/pbr_color.glsl"
layout(location = 0) out vec4 result;

/** Evaluates the same HDR input through linear and legacy output at neighboring pixels. */
void main()
{
    result = vec4(VgeSceneOutput(vec3(8.0, 2.0, -1.0), gl_FragCoord.xy,
        gl_FragCoord.x < 1.0), .375);
}
