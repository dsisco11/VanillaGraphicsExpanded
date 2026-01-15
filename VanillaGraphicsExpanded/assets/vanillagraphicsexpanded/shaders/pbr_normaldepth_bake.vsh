#version 330 core

// Fullscreen quad passthrough used for atlas bake blits.

layout (location = 0) in vec3 vertex;
layout (location = 1) in vec2 uv;

out vec2 v_uv;

void main()
{
    v_uv = uv;
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
}
