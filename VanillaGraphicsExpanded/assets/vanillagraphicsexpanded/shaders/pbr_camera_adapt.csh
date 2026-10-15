#version 430 core
@import "./includes/camera_exposure_inputs.glsl"
@import "./includes/vge_frame_ubo.glsl"
layout(local_size_x=256) in;
layout(std430, binding=0) readonly buffer CameraHistogram { uvec2 bins[]; };
layout(std430, binding=1) readonly buffer PreviousExposure {
    float previousEV;
};
layout(std430, binding=2) writeonly buffer OutputExposure {
    float exposureEV; float reservedAdaptation; float meteredLogLuminance;
    float meteringValidity; float uncappedTarget; float exposureCapHit;
    float reserved0; float reserved1;
};
shared vec2 histogram[64];
shared vec2 partialHistogram[256];
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
    uint bin=lane%64u;
    vec2 entry=vec2(0);
    // Decode before merging groups. The shared fixed-point weight scale cancels
    // from the percentile/geometric mean, including when resolution reduces it.
    // Buffer extent is the dispatch contract; every partial is overwritten even
    // for empty tiles, so resize cannot expose a previous frame's population.
    uint groupCount=uint(bins.length())/64u;
    // Four lanes cooperate per bin to shorten dependent memory-load chains.
    // Lanes in each 64-wide stripe still fetch consecutive bins for coalescing.
    for(uint group=lane/64u;group<groupCount;group+=4u)
    {
        vec2 encoded=vec2(bins[group*64u+bin]);
        float weight=encoded.x;
        entry+=vec2(weight,encoded.y/128.0*(meterRange.y-meterRange.x)+weight*meterRange.x);
    }
    partialHistogram[lane]=entry;
    barrier();
    if(lane<64u)
        histogram[lane]=(partialHistogram[lane]+partialHistogram[lane+64u])
            +(partialHistogram[lane+128u]+partialHistogram[lane+192u]);
    barrier();
    if(lane!=0u) return;
    float manual = clamp(percentiles.z+exposureRange.z,exposureRange.x,exposureRange.y);
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
    float rawTarget=accepted>0.0 ? log2(exposureRange.w)-sum/accepted+exposureRange.z : previous;
    float target=clamp(rawTarget,exposureRange.x,exposureRange.y);
    exposureEV=percentiles.w<.5 ? manual : clamp(adaptation.w>.5 ? target
        : adaptEV(previous,target,max(vgeFrame.deltaTime,0.0)),exposureRange.x,exposureRange.y);
    // Only camera gain has temporal adaptation. Retain instantaneous physical
    // metering for diagnostics without introducing another display reference.
    reservedAdaptation=0.0;
    meteredLogLuminance=accepted>0.0 ? sum/accepted : 0.0;
    meteringValidity=accepted>0.0 ? 1.0 : 0.0;
    uncappedTarget=rawTarget; exposureCapHit=rawTarget!=target ? 1.0 : 0.0;
    reserved0=0.0; reserved1=0.0;
}


