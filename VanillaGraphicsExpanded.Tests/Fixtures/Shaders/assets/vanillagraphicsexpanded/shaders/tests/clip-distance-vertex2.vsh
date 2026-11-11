#version 450 core
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[2]; };
void main()
{
    vec2 positions[3] = vec2[3](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
    gl_Position = vec4(positions[gl_VertexID], 0.0, 1.0);
    gl_ClipDistance[0] = gl_Position.x;
    gl_ClipDistance[1] = 1.0;
}
