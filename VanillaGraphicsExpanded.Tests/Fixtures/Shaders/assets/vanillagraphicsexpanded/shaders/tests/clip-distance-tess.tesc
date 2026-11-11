#version 450 core
layout(vertices=3) out;
in gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[3]; } gl_in[];
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[3]; } gl_out[];
/** Forwards matching incoming clip values; GLSL requires tessellation outputs to use the invocation built-in directly. */
void main()
{
    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
    for (int plane=0; plane<3; ++plane)
        gl_out[gl_InvocationID].gl_ClipDistance[plane] = gl_in[gl_InvocationID].gl_ClipDistance[plane];
    if (gl_InvocationID == 0u)
    {
        gl_TessLevelInner[0] = 1.0;
        gl_TessLevelInner[1] = 1.0;
        gl_TessLevelOuter[0] = 1.0;
        gl_TessLevelOuter[1] = 1.0;
        gl_TessLevelOuter[2] = 1.0;
        gl_TessLevelOuter[3] = 1.0;
    }
}
