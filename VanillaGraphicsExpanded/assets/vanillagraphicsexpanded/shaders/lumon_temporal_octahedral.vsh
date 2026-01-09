#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertex;

void main(void)
{
    gl_Position = vec4(vertex.xy, 0.0, 1.0);
}
