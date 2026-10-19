#ifndef VGE_CLOUD_SHADOW_SUPPORT
#define VGE_CLOUD_SHADOW_SUPPORT
/** Clips finite cache rays without classifying small nonzero directions as parallel. */
bool CloudShadowClipAxis(float origin,float direction,float lower,float upper,inout float near,inout float far)
{
    if(direction==0.0) return origin>=lower&&origin<=upper&&far>near;
    vec2 crossings=vec2(lower-origin,upper-origin)/direction;
    near=max(near,min(crossings.x,crossings.y));
    far=min(far,max(crossings.x,crossings.y));
    return far>near;
}
/** Restricts cache support walks to the finite native domain using exact-zero slab semantics. */
bool CloudShadowDomainSpan(vec3 origin,vec3 direction,float limit,out vec2 span)
{
    float near=0.0,far=limit,extent=volumeOffsetWidth.w*50.0;
    span=vec2(0);
    if(!CloudShadowClipAxis(origin.x,direction.x,0.0,extent,near,far)
        ||!CloudShadowClipAxis(origin.z,direction.z,0.0,extent,near,far)) return false;
    span=vec2(near,far);return true;
}
/** Walks finite native columns once, independently of density samples and empty procedural evaluations. */
struct CloudShadowSupport
{
    ivec2 cell,step;
    vec2 crossing,delta;
    float near,far;
    int remaining;
    bool valid;
};
/** Starts a bounded column walk on an already domain/root-clipped ray. */
CloudShadowSupport CloudShadowSupportStart(vec3 origin,vec3 direction,vec2 span)
{
    vec2 point=(origin+direction*span.x).xz;
    ivec2 cell=ivec2(floor(point/50.0)),step=ivec2(sign(direction.xz));
    for(int axis=0;axis<2;axis++)
        if(step[axis]<0&&point[axis]==float(cell[axis])*50.0) cell[axis]--;
    int width=int(volumeOffsetWidth.w);
    bool valid=width>0&&width<=200;
    for(int axis=0;axis<2;axis++)
        if(step[axis]==0&&(cell[axis]<0||cell[axis]>=width)) valid=false;
    cell=clamp(cell,ivec2(0),ivec2(max(0,width-1)));
    vec2 crossing=vec2(1e30),delta=vec2(1e30);
    for(int axis=0;axis<2;axis++) if(step[axis]!=0)
    {
        float boundary=float(cell[axis]+(step[axis]>0?1:0))*50.0;
        crossing[axis]=max(span.x,(boundary-origin.xz[axis])/direction.xz[axis]);
        delta[axis]=50.0/abs(direction.xz[axis]);
    }
    return CloudShadowSupport(cell,step,crossing,delta,span.x,span.y,401,valid);
}
/** Returns the next positive native column interval, never inferring support from sampled detail. */
bool CloudShadowSupportNext(vec3 origin,vec3 direction,inout CloudShadowSupport walk,
    out vec2 span,out ivec2 cell,out vec4 data)
{
    span=vec2(0);cell=ivec2(0);data=vec4(0);
    int width=int(volumeOffsetWidth.w);
    for(int visit=0;visit<401;visit++)
    {
        if(!walk.valid||walk.near>=walk.far||any(lessThan(walk.cell,ivec2(0)))
            ||any(greaterThanEqual(walk.cell,ivec2(width)))) return false;
        if(walk.remaining<=0){walk.valid=false;return false;}
        walk.remaining--;
        cell=walk.cell;data=CloudColumn(cell);
        float near=walk.near,far=min(walk.far,min(walk.crossing.x,walk.crossing.y));
        walk.near=far;
        // Advancing every tied axis handles corners without revisiting cells.
        for(int axis=0;axis<2;axis++) if(walk.crossing[axis]<=far)
        {walk.cell[axis]+=walk.step[axis];walk.crossing[axis]+=walk.delta[axis];}
        if(data.r>0.0&&CloudShadowClipAxis(origin.y,direction.y,data.b*50.0,data.a*50.0,near,far))
        {span=vec2(near,far);return true;}
    }
    walk.valid=false;return false;
}
/** Measures occupied geometric arc length and its containing chord with bounded support-only queries. */
bool CloudShadowSupportMeasure(vec3 origin,vec3 direction,vec2 clipped,out vec2 chord,out float length)
{
    chord=vec2(0);length=0.0;
    CloudShadowSupport walk=CloudShadowSupportStart(origin,direction,clipped);
    vec2 span;ivec2 cell;vec4 data;
    for(int visit=0;visit<401;visit++)
    {
        if(!CloudShadowSupportNext(origin,direction,walk,span,cell,data)) return walk.valid;
        if(length==0.0) chord.x=span.x;
        chord.y=span.y;length+=span.y-span.x;
    }
    return walk.near>=walk.far;
}
/** Allocates at most the supplied whole-ray budget with a fifty-world-unit target spacing. */
int CloudShadowSupportCount(float length,int budget)
{
    return length>0.0?min(budget,max(1,int(ceil(length/50.0)))):0;
}
/** Distributes one whole-ray budget over occupied arc length, splitting weights across gaps and storage bins. */
bool CloudShadowSupportIntegrate(vec3 origin,vec3 direction,vec2 clipped,vec2 chord,float length,
    int budget,int segments,float areaWeight,inout vec4 opticalDepth[4],inout float tau)
{
    if(length<=0.0) return true;
    int count=CloudShadowSupportCount(length,budget);
    float ds=length/float(count),arc=0.0;
    CloudShadowSupport walk=CloudShadowSupportStart(origin,direction,clipped);
    vec2 span;ivec2 cell;vec4 data;
    if(!CloudShadowSupportNext(origin,direction,walk,span,cell,data)) return false;
    float supportStart=0.0,supportEnd=span.y-span.x;
    for(int sampleIndex=0;sampleIndex<128;sampleIndex++)
    {
        if(sampleIndex>=count) break;
        float end=sampleIndex==count-1?length:float(sampleIndex+1)*ds;
        float middle=(float(sampleIndex)+.5)*ds;
        vec4 weights[4];for(int group=0;group<4;group++) weights[group]=vec4(0);
        float sigma=0.0;bool sampled=false;
        // Each pass visits at most 401 columns per ray; subdivision never adds
        // density evaluations. A stratum may represent several disjoint intervals.
        for(int piece=0;piece<402;piece++)
        {
            float nextArc=min(end,supportEnd);
            float near=span.x+clamp(arc-supportStart,0.0,span.y-span.x);
            float far=span.x+clamp(nextArc-supportStart,0.0,span.y-span.x);
            if(!sampled&&middle<=nextArc)
            {
                vec3 point=origin+direction*(span.x+clamp(middle-supportStart,0.0,span.y-span.x));
                sigma=CloudDensity(point,cell,data);sampled=true;
            }
            for(int group=0;group<4;group++)
            {
                if(group>=segments/4) break;
                weights[group]+=CloudShadowSegmentOverlap(vec2(near,far),chord,group,segments);
            }
            arc=nextArc;
            if(arc>=end) break;
            if(!CloudShadowSupportNext(origin,direction,walk,span,cell,data)) return false;
            supportStart=supportEnd;supportEnd+=span.y-span.x;
        }
        if(!sampled||arc<end) return false;
        for(int group=0;group<4;group++)
        {
            if(group>=segments/4) break;
            opticalDepth[group]=min(vec4(1e30),opticalDepth[group]+weights[group]*sigma*volumeAnchorUnits.z*areaWeight);
        }
        tau=min(1e30,tau+sigma*(end-float(sampleIndex)*ds)*volumeAnchorUnits.z*areaWeight);
    }
    return true;
}
#endif
