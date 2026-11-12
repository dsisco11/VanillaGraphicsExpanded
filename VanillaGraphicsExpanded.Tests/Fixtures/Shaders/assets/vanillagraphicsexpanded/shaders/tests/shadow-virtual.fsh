#version 450 core
@import "../includes/shadows/fragment-interface.glsl"
@import "../includes/shadows/views.glsl"
@import "../includes/shadows/writing.glsl"

void main()
{
    float materialAlpha = gl_FragCoord.x < 6.0 ? 1.0 : 0.0;
    if (materialAlpha < 0.5)
        discard;
}
