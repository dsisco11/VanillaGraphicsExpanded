#version 430 core
@import "./includes/pbr_frame_lighting.glsl"
@import "./includes/camera_exposure_inputs.glsl"
layout(local_size_x=64) in;
uniform sampler2D sceneRadiance;
layout(std430, binding=0) writeonly buffer CameraHistogram { uvec2 bins[]; };
shared uint counts[64];
shared uint sums[64];
/** Classifies each grid sample once; shared integer atomics have bounded local contention. */
void main()
{
    uint lane=gl_LocalInvocationIndex;
    counts[lane]=0u; sums[lane]=0u;
    barrier();
    vec2 uv=(vec2(lane,gl_WorkGroupID.x)+.5)/vec2(64,36);
    ivec2 size=textureSize(sceneRadiance,0);
    vec3 color=VgePhysicalFrameLight(texelFetch(sceneRadiance,min(ivec2(uv*vec2(size)),size-1),0).rgb);
    if (!any(isnan(color)) && !any(isinf(color)))
    {
        float luminance=dot(max(color,vec3(0)),vec3(.2126,.7152,.0722));
        if (luminance>exp2(meterRange.x) && luminance<exp2(meterRange.y))
        {
            float level=log2(luminance);
            uint bin=uint(clamp(int((level-meterRange.x)*64.0/(meterRange.y-meterRange.x)),0,63));
            vec2 centered=uv*2.0-1.0;
            float weight=mix(1.0,.25+.75*exp(-2.0*dot(centered,centered)),meterRange.z);
            // Quantize weight first; shifted levels keep every atomic sum unsigned.
            uint encodedWeight=uint(round(weight*4096.0));
            // A 64-stop optional domain uses 128 units: the 64-lane maximum
            // is 2^31 rather than reaching uint32 overflow at the upper edge.
            float sumQuantization=meterRange.w>.5 ? 128.0 : 256.0;
            uint encodedSum=uint(round(float(encodedWeight)*(level-meterRange.x)*sumQuantization));
            atomicAdd(counts[bin],encodedWeight);
            atomicAdd(sums[bin],encodedSum);
        }
    }
    barrier();
    // Every group overwrites every bin in its own slice: no global clear or atomics.
    bins[gl_WorkGroupID.x*64u+lane]=uvec2(counts[lane],sums[lane]);
}

