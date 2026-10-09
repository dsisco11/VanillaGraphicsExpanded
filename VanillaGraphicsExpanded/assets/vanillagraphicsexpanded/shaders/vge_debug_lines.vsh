#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec4 color;

@import "./includes/vge_debug_lines_params_ubo.glsl"

out vec4 vColor;

/** Projects camera-relative debug geometry using the universal frame snapshot. */
void main(void)
{
    vColor = color;
    gl_Position = vgeFrame.currViewProjMatrix * vec4(vertex + worldOffset, 1.0);
}
