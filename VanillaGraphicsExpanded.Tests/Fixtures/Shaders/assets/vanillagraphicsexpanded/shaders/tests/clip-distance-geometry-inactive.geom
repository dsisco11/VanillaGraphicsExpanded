#version 450 core
layout(triangles) in;
layout(triangle_strip, max_vertices=3) out;
void main()
{
    for (int vertex=0; vertex<3; ++vertex)
    {
        gl_Position = gl_in[vertex].gl_Position;
        EmitVertex();
    }
    EndPrimitive();
}
