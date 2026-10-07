#version 330 core
/** Generates one oversized fullscreen triangle without vertex or index streams. */
void main()
{
    vec2 position = gl_VertexID == 1 ? vec2(3, -1)
        : gl_VertexID == 2 ? vec2(-1, 3) : vec2(-1, -1);
    gl_Position = vec4(position, 1, 1);
}
