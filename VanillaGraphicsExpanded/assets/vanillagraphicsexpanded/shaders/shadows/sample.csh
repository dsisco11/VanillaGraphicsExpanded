#version 450 core
layout(local_size_x=1,local_size_y=1,local_size_z=1) in;
layout(std430,binding=0) readonly buffer ShadowSampleInputs { uint sampleInputs[]; };
layout(std430,binding=1) readonly buffer PageEntries { uint entries[]; };
layout(std430,binding=2) buffer ShadowSampleResults { uint sampleResults[]; };
layout(binding=0) uniform sampler2DArray shadowDepth0;
layout(binding=1) uniform sampler2DArray shadowDepth1;
layout(binding=2) uniform sampler2DArray shadowDepth2;
layout(binding=3) uniform sampler2DArray shadowDepth3;
@import "../includes/shadows/sampling.glsl"
/// Runs the shared sampler and retains tap addresses for GPU demand producers and controlled qualification.
void main() {
    uint mask; ivec3 addresses[9]; float references[9];
    sampleResults[0]=floatBitsToUint(shadowPcf(mask,addresses,references));
    sampleResults[1]=mask; sampleResults[2]=9u; sampleResults[3]=0u;
    for(int i=0;i<9;i++) {
        sampleResults[4+4*i]=uint(addresses[i].x); sampleResults[5+4*i]=uint(addresses[i].y);
        sampleResults[6+4*i]=uint(addresses[i].z); sampleResults[7+4*i]=floatBitsToUint(references[i]);
    }
}
