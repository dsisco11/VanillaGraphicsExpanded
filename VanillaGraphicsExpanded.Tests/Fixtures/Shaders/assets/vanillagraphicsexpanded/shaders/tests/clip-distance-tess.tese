#version 450 core
layout(triangles, equal_spacing, ccw) in;
in gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[3]; } gl_in[];
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[3]; };
void main()
{
    gl_Position = gl_in[0].gl_Position * gl_TessCoord.x +
        gl_in[1].gl_Position * gl_TessCoord.y + gl_in[2].gl_Position * gl_TessCoord.z;
    for (int plane=0; plane<3; ++plane)
        gl_ClipDistance[plane] = gl_in[0].gl_ClipDistance[plane] * gl_TessCoord.x +
            gl_in[1].gl_ClipDistance[plane] * gl_TessCoord.y + gl_in[2].gl_ClipDistance[plane] * gl_TessCoord.z;
}
