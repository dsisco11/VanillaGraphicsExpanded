#ifndef VGE_CLOUD_SHADOW_SEGMENTS
#define VGE_CLOUD_SHADOW_SEGMENTS
/** Returns world-unit overlap with four consecutive equal-depth segments of a supported chord. */
vec4 CloudShadowSegmentOverlap(vec2 window,vec2 span,int group,int count)
{
    float width=(span.y-span.x)/float(count);
    vec4 start=span.x+(vec4(0,1,2,3)+float(group*4))*width;
    return max(vec4(0),min(vec4(window.y),start+width)-max(vec4(window.x),start));
}
/** Reads a finite-window optical depth from discrete metadata and packed nearest-filtered segments. */
bool CloudShadowSegmentDepth(sampler2D cache,ivec2 pixel,int source,float depth,float distance,out float tau)
{
    tau=0.0;
    ivec2 size=textureSize(cache,0);
    int groups=size.y/size.x-1;
    if((groups!=2&&groups!=4)||size.y!=size.x*(groups+1)) return false;
    vec4 record=texelFetch(cache,pixel,0);
    if(any(isnan(record))||any(isinf(record))||record.x<0.0||record.y<record.x||record.z<0.0
        ||record.w!=float(source+1)||isnan(depth)||isinf(depth)||isnan(distance)||isinf(distance)||distance<0.0) return false;
    if(record.y==record.x) return record.z==0.0;
    float width=(record.y-record.x)/float(groups*4);
    if(width<=0.0) return false;
    vec2 window=vec2(depth-distance,depth);
    for(int group=0;group<4;group++)
    {
        if(group>=groups) break;
        vec4 fractions=clamp(CloudShadowSegmentOverlap(window,record.xy,group,groups*4)/width,0.0,1.0);
        // A finite shadow window often touches only one packed group. Avoid fetching
        // unrelated depths, while still validating every contributing storage record.
        if(!any(greaterThan(fractions,vec4(0)))) continue;
        vec4 values=texelFetch(cache,pixel+ivec2(0,(group+1)*size.x),0);
        if(any(isnan(values))||any(isinf(values))||any(lessThan(values,vec4(0)))) return false;
        tau=min(1e30,tau+dot(values,fractions));
    }
    return true;
}
#endif
