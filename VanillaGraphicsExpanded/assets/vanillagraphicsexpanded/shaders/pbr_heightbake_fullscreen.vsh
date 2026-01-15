#version 330 core

// Fullscreen triangle vertex shader for bake passes.
// No vertex buffers required; positions derived from gl_VertexID.

out vec2 v_uv;

void main()
{
    vec2 pos;

    // Fullscreen triangle: (-1,-1), (3,-1), (-1,3)
    if (gl_VertexID == 0) pos = vec2(-1.0, -1.0);
    else if (gl_VertexID == 1) pos = vec2( 3.0, -1.0);
    else pos = vec2(-1.0,  3.0);

    v_uv = pos * 0.5 + 0.5;
    gl_Position = vec4(pos, 0.0, 1.0);
}
