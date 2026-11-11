#version 450 core
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
in gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[3]; } gl_in[];
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[4]; };
void main()
{
    for (int vertex=0; vertex<3; ++vertex)
    {
        gl_Position = gl_in[vertex].gl_Position;
        for (int plane=0; plane<3; ++plane)
            gl_ClipDistance[plane] = gl_in[vertex].gl_ClipDistance[plane];
        gl_ClipDistance[3] = 1.0;
        EmitVertex();
    }
    EndPrimitive();
}
