#version 330 core
/** Covers the directional lunar disk without engine mesh or texture ownership. */
void main()
{
    vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);
    gl_Position=vec4(p*2.0-1.0,1.0,1.0);
}
