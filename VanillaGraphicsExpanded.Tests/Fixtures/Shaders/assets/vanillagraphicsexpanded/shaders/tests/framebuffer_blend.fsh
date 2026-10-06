#version 430 core
layout(location=0) out vec4 o0;
layout(location=1) out vec4 o1;
void main()
{
    vec4 c = vec4(1.0, 0.0, 0.0, 0.5);
    o0 = c;
    o1 = c;
}