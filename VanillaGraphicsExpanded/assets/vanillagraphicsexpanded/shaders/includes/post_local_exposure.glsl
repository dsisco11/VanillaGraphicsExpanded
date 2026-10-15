#ifndef VGE_POST_LOCAL_EXPOSURE
#define VGE_POST_LOCAL_EXPOSURE

/** Classifies physical RGB without turning black, negative or invalid samples into illumination. */
bool VgeLocalLogLuminance(vec3 physical, out float logLuminance)
{
    logLuminance=0.0;
    if(any(isnan(physical)) || any(isinf(physical)) || any(lessThan(physical,vec3(0)))) return false;
    float luminance=dot(physical,vec3(.2126,.7152,.0722));
    if(luminance<=0.0 || isnan(luminance) || isinf(luminance)) return false;
    logLuminance=log2(luminance);
    return !isnan(logLuminance) && !isinf(logLuminance);
}

/** Rejects missing or invalid log-luminance texels before filtering. */
bool VgeLocalBaseValid(vec2 sampleValue)
{
    // Positive Float32 physical luminance cannot produce a logarithm outside
    // this interval. Reject corrupted metadata before weighted sums overflow.
    return sampleValue.y>.5 && !any(isnan(sampleValue)) && !any(isinf(sampleValue))
        && sampleValue.x>=-150.0 && sampleValue.x<=128.0;
}

/** Measures similarity in physical log luminance; width is an explicit candidate parameter in stops. */
float VgeLocalRangeWeight(float difference, float width)
{
    if(width<=0.0 || isnan(width) || isinf(width)) return 0.0;
    float normalized=difference/width;
    return exp(-.5*normalized*normalized);
}

/** Produces a normalized edge-preserving base over a bounded current-frame 3x3 footprint. */
vec2 VgeFilterLocalBase(sampler2D logImage, ivec2 pixel, float rangeWidth)
{
    ivec2 size=textureSize(logImage,0);
    if(any(lessThan(pixel,ivec2(0))) || any(greaterThanEqual(pixel,size))) return vec2(0);
    vec2 centre=texelFetch(logImage,pixel,0).rg;
    if(!VgeLocalBaseValid(centre)) return vec2(0);
    float sum=0.0, weightSum=0.0;
    for(int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
    {
        ivec2 neighbour=pixel+ivec2(x,y);
        if(any(lessThan(neighbour,ivec2(0))) || any(greaterThanEqual(neighbour,size))) continue;
        vec2 sampleValue=texelFetch(logImage,neighbour,0).rg;
        if(!VgeLocalBaseValid(sampleValue)) continue;
        // Spatial binomial weights retain the centre and avoid duplicating border texels.
        float spatial=(x==0 ? 2.0 : 1.0)*(y==0 ? 2.0 : 1.0);
        float weight=spatial*VgeLocalRangeWeight(sampleValue.x-centre.x,rangeWidth);
        sum+=sampleValue.x*weight; weightSum+=weight;
    }
    return weightSum>0.0 ? vec2(sum/weightSum,1) : vec2(0);
}

/** Reconstructs the coarse base using full-resolution luminance guidance rather than plain bilinear leakage. */
bool VgeSampleLocalBase(sampler2D baseImage, vec2 coordinate, float guidance,
    float rangeWidth, out float base)
{
    base=guidance;
    if(isnan(guidance) || isinf(guidance)
        || any(isnan(coordinate)) || any(isinf(coordinate))
        || any(lessThan(coordinate,vec2(0))) || any(greaterThan(coordinate,vec2(1)))) return false;
    ivec2 size=textureSize(baseImage,0);
    vec2 grid=coordinate*vec2(size)-.5, fraction=fract(grid);
    ivec2 origin=ivec2(floor(grid));
    float sum=0.0, weightSum=0.0;
    for(int y=0;y<2;y++) for(int x=0;x<2;x++)
    {
        vec2 sampleValue=texelFetch(baseImage,clamp(origin+ivec2(x,y),ivec2(0),size-1),0).rg;
        if(!VgeLocalBaseValid(sampleValue)) continue;
        float spatial=(x==0 ? 1.0-fraction.x : fraction.x)*(y==0 ? 1.0-fraction.y : fraction.y);
        float weight=spatial*VgeLocalRangeWeight(sampleValue.x-guidance,rangeWidth);
        sum+=sampleValue.x*weight; weightSum+=weight;
    }
    if(weightSum<=0.0) return false;
    base=sum/weightSum;
    return !isnan(base) && !isinf(base);
}

/** Compresses the globally exposed base around display middle gray, with a smooth one-stop transition. */
float VgeLocalExposureStops(float base, float globalStops, float logMiddleGray, vec2 strengths, vec2 bounds)
{
    if(isnan(base) || isinf(base) || isnan(globalStops) || isinf(globalStops)
        || isnan(logMiddleGray) || isinf(logMiddleGray)
        || base< -150.0 || base>128.0 || globalStops< -48.0 || globalStops>24.0
        || any(isnan(strengths)) || any(isinf(strengths))
        || any(isnan(bounds)) || any(isinf(bounds))) return 0.0;
    float relative=base+globalStops-logMiddleGray;
    vec2 compression=clamp(strengths,0.0,1.0);
    float strength=mix(compression.x,compression.y,smoothstep(-1.0,1.0,relative));
    // Candidate correction is bounded independently of camera EV. These are display
    // safeguards, not a calibration of human contrast sensitivity or source power.
    vec2 admittedBounds=clamp(bounds,0.0,8.0);
    return clamp((strength-1.0)*relative,-admittedBounds.y,admittedBounds.x);
}
#endif
