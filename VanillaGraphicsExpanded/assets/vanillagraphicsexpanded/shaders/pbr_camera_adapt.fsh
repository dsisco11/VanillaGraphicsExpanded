#version 330 core
@import "./includes/camera_exposure_inputs.glsl"
uniform sampler2D histogram;
uniform sampler2D previousExposure;
layout(location=0) out float exposureEV;
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
    float manual = clamp(percentiles.z+exposureRange.z,exposureRange.x,exposureRange.y);
    if (percentiles.w<.5) { exposureEV=manual; return; }
    float previous = adaptation.w>.5 ? manual : texelFetch(previousExposure,ivec2(0),0).r;
    if (isnan(previous) || isinf(previous)) previous=manual;
    float count=0.0;
    for(int i=0;i<64;++i) count+=texelFetch(histogram,ivec2(i,0),0).r;
    float low=count*percentiles.x, high=count*percentiles.y;
    float prefix=0.0, sum=0.0, accepted=0.0;
    for(int i=0;i<64;++i)
    {
        vec2 entry=texelFetch(histogram,ivec2(i,0),0).rg;
        float weight=max(0.0,min(prefix+entry.x,high)-max(prefix,low));
        if(entry.x>0.0) { sum+=weight*entry.y/entry.x; accepted+=weight; }
        prefix+=entry.x;
    }
    float target=accepted>0.0 ? clamp(log2(exposureRange.w)-sum/accepted+exposureRange.z,
        exposureRange.x,exposureRange.y) : previous;
    exposureEV=clamp(adaptation.w>.5 ? target : adaptEV(previous,target,max(adaptation.x,0.0)),
        exposureRange.x,exposureRange.y);
}
