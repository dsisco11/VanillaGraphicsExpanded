#version 450 core
layout(std430,binding=0) readonly buffer ShadowWriteEntries { uint shadowWriteEntries[]; };
layout(std430,binding=1) readonly buffer ShadowWritableAssignments { uvec4 shadowWritableAssignments[]; };
layout(std140,binding=28) uniform ShadowAtomicWriterInputs {
    uvec4 shadowKey[5];
    uvec4 shadowConfiguration;
    uvec4 shadowBatch;
    uvec4 shadowTexelOffset;
};
layout(location=0) flat in float atomicDepth;
layout(r32ui,binding=0) uniform coherent uimage2DArray shadowDepthPool;
@import "../includes/shadows/writing.glsl"

void main()
{
    ShadowWriteView view;
    for (int index=0; index<17; index++) view.key[index]=shadowKey[index/4][index%4];
    view.capacity=shadowConfiguration.x;
    view.probes=shadowConfiguration.y;
    view.pageSize=shadowConfiguration.z;
    view.arrayLayers=shadowConfiguration.w;
    view.batch=shadowBatch.xy;
    shadowAtomicDepth(view,ivec2(gl_FragCoord.xy)+ivec2(shadowTexelOffset.xy),atomicDepth);
}
