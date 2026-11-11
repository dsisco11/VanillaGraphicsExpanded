#version 450 core
layout(std430,binding=0) readonly buffer ShadowWriteEntries { uint shadowWriteEntries[]; };
layout(std430,binding=1) readonly buffer ShadowWritableAssignments { uvec4 shadowWritableAssignments[]; };
layout(std140,binding=28) uniform ShadowAtomicWriterInputs {
    uvec4 shadowKey[5];
    uvec4 shadowConfiguration;
    uvec4 shadowBatch;
};
layout(location=0) flat in float atomicDepth;
layout(r32ui,binding=0) uniform coherent uimage2D shadowBank0;
layout(r32ui,binding=1) uniform coherent uimage2D shadowBank1;
layout(r32ui,binding=2) uniform coherent uimage2D shadowBank2;
layout(r32ui,binding=3) uniform coherent uimage2D shadowBank3;
@import "../includes/shadows/writing.glsl"

void main()
{
    ShadowWriteView view;
    for (int index=0; index<17; index++) view.key[index]=shadowKey[index/4][index%4];
    view.capacity=shadowConfiguration.x;
    view.probes=shadowConfiguration.y;
    view.pageSize=shadowConfiguration.z;
    view.residentBanks=shadowConfiguration.w;
    view.batch=shadowBatch.xy;
    shadowAtomicDepth(view,ivec2(gl_FragCoord.xy),atomicDepth);
}
