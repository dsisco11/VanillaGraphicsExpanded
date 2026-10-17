#version 430 core
layout(location = 0) out vec4 color;
layout(location = 1) out vec4 second;
layout(location = 2) out vec4 third;
/// Reports fragment facing even when the pipeline disables culling.
void main()
{
    color = gl_FrontFacing ? vec4(1, 0, 0, 1) : vec4(0, 1, 0, 1);
    second = vec4(0, 1, 0, 1);
    third = vec4(0, 0, 1, 1);
}
