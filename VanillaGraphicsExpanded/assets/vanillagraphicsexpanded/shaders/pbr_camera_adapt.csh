#version 430 core
@import "./includes/camera_exposure_inputs.glsl"
@import "./includes/vge_frame_ubo.glsl"
layout(local_size_x=64) in;
layout(std430, binding=0) readonly buffer CameraHistogram { uvec2 bins[]; };
layout(std430, binding=1) readonly buffer PreviousExposure {
    float previousEV; float previousLogLuminance; float previousMetered;
    float previousValidity;
};
layout(std430, binding=2) writeonly buffer OutputExposure {
    float exposureEV; float adaptedLogLuminance; float meteredLogLuminance;
    float adaptationValidity; float uncappedTarget; float exposureCapHit;
    float reserved0; float reserved1;
};
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
        float divisor=meterRange.w>.5 ? 524288.0 : 1048576.0;
        entry+=vec2(weight,encoded.y/divisor+weight*meterRange.x);
    }
    histogram[lane]=entry;
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
    // Publish complete metadata even when disabled. Camera gain and the physical
    // viewing estimate adapt independently, so a cap cannot masquerade as luminance.
    adaptedLogLuminance=0.0; meteredLogLuminance=0.0; adaptationValidity=0.0;
    uncappedTarget=rawTarget; exposureCapHit=rawTarget!=target ? 1.0 : 0.0;
    reserved0=0.0; reserved1=0.0;
    if(meterRange.w>.5 && accepted>0.0)
    {
        float measured=sum/accepted;
        meteredLogLuminance=measured;
        // Select the completed result explicitly. A reset never needs to evaluate
        // temporal history, and invalid history cannot participate in arithmetic.
        if(!(adaptation.w<.5) || !(previousValidity>.5) || isnan(previousValidity) || isinf(previousValidity)
            || isnan(previousLogLuminance) || isinf(previousLogLuminance))
            adaptedLogLuminance=measured;
        else
            adaptedLogLuminance=-adaptEV(-previousLogLuminance,-measured,max(vgeFrame.deltaTime,0.0));
        adaptationValidity=1.0;
    }
}


