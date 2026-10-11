#version 330 core
/** Generates a fullscreen triangle for bounded histogram and history targets. */
void main()
{
    vec2 p = gl_VertexID == 1 ? vec2(3,-1) : gl_VertexID == 2 ? vec2(-1,3) : vec2(-1,-1);
    gl_Position = vec4(p,0,1);
}
