#version 330 core

in vec3 vertexPosition;
out vec2 uv;

void main()
{
    gl_Position = vec4(vertexPosition.xy, 0.0, 1.0);
    uv = vertexPosition.xy * 0.5 + 0.5;
}
