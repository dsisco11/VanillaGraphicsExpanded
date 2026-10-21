#version 430 core
@import "../cloud_volume/sampling.glsl"
layout(local_size_x=64) in;
layout(std430) readonly buffer SamplingQueries{uvec4 queries[];};
layout(std430) writeonly buffer SamplingResults{vec4 results[];};
uniform sampler2D pmjJitter;
/** Evaluates the production helper for each independent immutable query. */
void main()
{
    uint i=gl_GlobalInvocationID.x;if(i>=uint(results.length()))return;
    uvec4 a=queries[i*2u],b=queries[i*2u+1u];
    float rank=(float(a.x)+.5)/4.0;ivec2 pixel=ivec2(a.yz),size=ivec2(a.w,b.x);
    uint observation=InterleavedObservationIndex2x2(rank,pixel,size,b.y,b.z);
    bool selected=all(equal(pixel,InterleavedSampleLocation2x2(rank,pixel/2,b.y,size)));
    vec2 point=b.w==1u?StoredVolumeSampleUv(pmjJitter,rank,pixel,size,b.y,b.z):PmjPixelOffset(pmjJitter,pixel,observation);
    results[i]=vec4(float(observation),point,selected?1:0);
}
