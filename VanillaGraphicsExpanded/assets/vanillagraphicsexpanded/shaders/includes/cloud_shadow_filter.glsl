#ifndef VGE_CLOUD_SHADOW_FILTER
#define VGE_CLOUD_SHADOW_FILTER
@import "./cloud_shadow_segments.glsl"
/** Filters finite-window transmission, retaining discrete validity and independent octave extinction. */
bool CloudShadowTransmission(sampler2D cache,vec2 uv,int source,float depth,float distance,
    float octaveExtinction,out vec2 transmission)
{
    transmission=vec2(0);
    if(any(isnan(uv))||any(isinf(uv))||any(lessThan(uv,vec2(0)))||any(greaterThan(uv,vec2(1)))
        ||isnan(octaveExtinction)||isinf(octaveExtinction)||octaveExtinction<0.0) return false;
    int width=textureSize(cache,0).x;
    vec2 position=uv*float(width)-.5;
    ivec2 first=ivec2(floor(position));
    vec2 fraction=fract(position);
    // Match texel-center bilinear weights without clamping edge taps or averaging
    // depth records. Every contributing ray must certify its complete light window.
    for(int y=0;y<2;y++) for(int x=0;x<2;x++)
    {
        float weight=(x==0?1.0-fraction.x:fraction.x)*(y==0?1.0-fraction.y:fraction.y);
        if(weight<=0.0) continue;
        ivec2 pixel=first+ivec2(x,y);
        if(any(lessThan(pixel,ivec2(0)))||any(greaterThanEqual(pixel,ivec2(width)))) return false;
        float tau;
        if(!CloudShadowSegmentDepth(cache,pixel,source,depth,distance,tau)) return false;
        transmission+=weight*exp(-vec2(tau,octaveExtinction*tau));
    }
    return true;
}
#endif
