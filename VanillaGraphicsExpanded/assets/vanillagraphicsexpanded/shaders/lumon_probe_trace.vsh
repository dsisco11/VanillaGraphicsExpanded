#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec2 uvIn;

out vec2 uv;

void main(void)
{
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
    uv = uvIn;
}
