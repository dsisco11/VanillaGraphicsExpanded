#version 430 core
@import "./includes/camera_exposure_inputs.glsl"
@import "./includes/vge_frame_ubo.glsl"
layout(local_size_x=64) in;
layout(std430, binding=0) readonly buffer CameraHistogram { uvec2 bins[]; };
layout(std430, binding=1) readonly buffer PreviousExposure { float previousEV; };
layout(std430, binding=2) writeonly buffer OutputExposure { float exposureEV; };
shared vec2 histogram[64];
/** Integrates constant-target adaptation exactly: linear outside one stop, exponential inside. */
float adaptEV(float previous, float target, float dt)
{
    float error = target-previous;
    float distance = abs(error);
    float rate = error>0.0 ? adaptation.y : adaptation.z;
    float linearTime = max(distance-1.0,0.0)/rate;
    float remaining = dt <= linearTime ? distance-rate*dt
        : min(distance,1.0)*exp(-rate*(dt-linearTime));
    return target-sign(error)*remaining;
}
/** Computes a percentile-trimmed geometric mean and publishes exposure history in stops. */
void main()
{
    uint lane=gl_LocalInvocationIndex;
    vec2 entry=vec2(0);
    // Decode before summing groups so the complete 2304-sample sum cannot overflow uint.
    for(uint group=0u;group<36u;++group)
    {
        vec2 encoded=vec2(bins[group*64u+lane]);
        float weight=encoded.x/4096.0;
        entry+=vec2(weight,encoded.y/1048576.0+weight*meterRange.x);
    }
    histogram[lane]=entry;
    barrier();
    if(lane!=0u) return;
    float manual = clamp(percentiles.z+exposureRange.z,exposureRange.x,exposureRange.y);
    if (percentiles.w<.5) { exposureEV=manual; return; }
    float previous = adaptation.w>.5 ? manual : previousEV;
    if (isnan(previous) || isinf(previous)) previous=manual;
    float count=0.0;
    for(int i=0;i<64;++i) count+=histogram[i].x;
    float low=count*percentiles.x, high=count*percentiles.y;
    float prefix=0.0, sum=0.0, accepted=0.0;
    for(int i=0;i<64;++i)
    {
        vec2 entry=histogram[i];
        float weight=max(0.0,min(prefix+entry.x,high)-max(prefix,low));
        if(entry.x>0.0) { sum+=weight*entry.y/entry.x; accepted+=weight; }
        prefix+=entry.x;
    }
    float target=accepted>0.0 ? clamp(log2(exposureRange.w)-sum/accepted+exposureRange.z,
        exposureRange.x,exposureRange.y) : previous;
    exposureEV=clamp(adaptation.w>.5 ? target : adaptEV(previous,target,max(vgeFrame.deltaTime,0.0)),
        exposureRange.x,exposureRange.y);
}


