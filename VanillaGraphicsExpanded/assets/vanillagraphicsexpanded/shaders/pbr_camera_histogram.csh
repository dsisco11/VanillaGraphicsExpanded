#version 430 core
@import "./includes/pbr_frame_lighting.glsl"
@import "./includes/camera_exposure_inputs.glsl"
layout(local_size_x=64) in;
uniform sampler2D sceneRadiance;
layout(std430, binding=0) writeonly buffer CameraHistogram { uvec2 bins[]; };
shared uint counts[64];
shared uint sums[64];
/** Averages one half-resolution footprint without reading outside odd-sized scene edges. */
bool downscaledLuminance(ivec2 origin, ivec2 size, out float luminance)
{
    vec3 average=vec3(0);
    int count=0;
    for(int y=0;y<2;++y) for(int x=0;x<2;++x)
    {
        ivec2 pixel=origin+ivec2(x,y);
        if(any(greaterThanEqual(pixel,size))) continue;
        vec3 value=texelFetch(sceneRadiance,pixel,0).rgb;
        if(any(isnan(value)) || any(isinf(value))) { luminance=0.0; return false; }
        // Scale before addition: finite stored values must not overflow the box sum.
        average+=max(value,vec3(0))*.25;
        ++count;
    }
    vec3 physical=VgePhysicalFrameLight(average*(4.0/float(count)));
    luminance=dot(physical,vec3(.2126,.7152,.0722));
    return !isnan(luminance) && !isinf(luminance);
}

/** Visits every half-resolution footprint while retaining bounded, disjoint group histograms. */
void main()
{
    uint lane=gl_LocalInvocationIndex;
    counts[lane]=0u; sums[lane]=0u;
    barrier();
    ivec2 size=textureSize(sceneRadiance,0);
    uvec2 extent=uvec2((size+1)/2);
    uint total=extent.x*extent.y;
    uint stride=gl_NumWorkGroups.x*64u;
    // Each lane visits at most ceil(total/stride) samples. A normalized log sum
    // costs at most 128 encoded units per weight. Reserve at least half uint range
    // even if every sample lands in one bin; the common scale cancels in reduction.
    uint groupSamples=((total+stride-1u)/stride)*64u;
    float weightScale=min(4096.0,floor(1073741824.0/(float(groupSamples)*128.0)));
    for(uint index=gl_WorkGroupID.x*64u+lane;index<total;index+=stride)
    {
        ivec2 pixel=ivec2(index%extent.x,index/extent.x)*2;
        // Use the actual footprint centre, including a final one-pixel edge.
        vec2 uv=(vec2(pixel)+vec2(min(size-pixel,ivec2(2)))*.5)/vec2(size);
        float luminance;
        if(!downscaledLuminance(pixel,size,luminance)) continue;
        if (luminance>exp2(meterRange.x) && luminance<exp2(meterRange.y))
        {
            float level=log2(luminance);
            float position=(level-meterRange.x)/(meterRange.y-meterRange.x);
            uint bin=uint(clamp(int(position*64.0),0,63));
            vec2 centered=uv*2.0-1.0;
            float weight=mix(1.0,.25+.75*exp(-2.0*dot(centered,centered)),meterRange.z);
            uint encodedWeight=uint(round(weight*weightScale));
            uint encodedSum=uint(round(float(encodedWeight)*position*128.0));
            atomicAdd(counts[bin],encodedWeight);
            atomicAdd(sums[bin],encodedSum);
        }
    }
    barrier();
    // Every group overwrites every bin in its own slice: no global clear or atomics.
    bins[gl_WorkGroupID.x*64u+lane]=uvec2(counts[lane],sums[lane]);
}

