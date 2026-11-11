#version 450 core
layout(location=11) in uint clipHalf;
layout(location=12) in float atomicDepthInput;
layout(location=0) flat out float atomicDepth;
out gl_PerVertex { vec4 gl_Position; float gl_ClipDistance[4]; };

void main()
{
    vec2 positions[3]=vec2[3](vec2(-1.0,-1.0),vec2(3.0,-1.0),vec2(-1.0,3.0));
    gl_Position=vec4(positions[gl_VertexID],0.0,1.0);
    gl_ClipDistance[0]=clipHalf!=0u?gl_Position.x:1.0;
    gl_ClipDistance[1]=1.0;
    gl_ClipDistance[2]=1.0;
    gl_ClipDistance[3]=1.0;
    atomicDepth=atomicDepthInput;
}
