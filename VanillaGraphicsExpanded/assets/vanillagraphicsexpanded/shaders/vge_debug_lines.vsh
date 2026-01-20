#version 330 core
#extension GL_ARB_explicit_attrib_location: enable

layout(location = 0) in vec3 vertex;
layout(location = 1) in vec4 color;

uniform mat4 modelViewProjectionMatrix;

out vec4 vColor;

void main(void)
{
    vColor = color;
    gl_Position = modelViewProjectionMatrix * vec4(vertex, 1.0);
}

