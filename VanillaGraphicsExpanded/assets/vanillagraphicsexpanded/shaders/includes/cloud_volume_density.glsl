#ifndef VGE_CLOUD_VOLUME_DENSITY
#define VGE_CLOUD_VOLUME_DENSITY
uniform sampler2D cloudMap;

/** Rejects invalid extinction/bounds and never extends native clamp-to-edge storage. */
vec4 CloudColumn(ivec2 cell)
{
    int width=int(volumeOffsetWidth.w);
    if(any(lessThan(cell,ivec2(0)))||any(greaterThanEqual(cell,ivec2(width)))) return vec4(0);
    vec4 data=texelFetch(cloudMap,cell,0);
    if(any(isnan(data))||any(isinf(data))||data.r<0.0||data.a<=data.b) return vec4(0);
    return data;
}
/** Tests whether a neighbor covers this height, avoiding seams on occupied shared faces. */
bool CloudOccupied(ivec2 cell,float y)
{
    vec4 data=CloudColumn(cell);
    if(data.r<=0.0) return false;
    if(y<data.b*50.0) return false;
    if(y>data.a*50.0) return false;
    return true;
}
/** Hashes a periodic lattice; no time or independent wind translation enters the noise. */
float CloudHash(vec3 lattice,float period)
{
    vec3 p=mod(lattice,period);
    p=mod(p,289.0);
    return fract(sin(dot(p,vec3(17.13,91.71,43.19)))*43758.5453);
}
/** Returns bounded smooth value noise with a fixed world-space period and configurable integer frequency. */
float CloudNoise(vec3 material)
{
    float period=max(1.0,floor(volumeDetail.z*12800.0+.5));
    vec3 p=material*(period/12800.0), i=floor(p), f=fract(p);
    f=f*f*(3.0-2.0*f);
    return mix(mix(mix(CloudHash(i,period),CloudHash(i+vec3(1,0,0),period),f.x),
        mix(CloudHash(i+vec3(0,1,0),period),CloudHash(i+vec3(1,1,0),period),f.x),f.y),
        mix(mix(CloudHash(i+vec3(0,0,1),period),CloudHash(i+vec3(1,0,1),period),f.x),
        mix(CloudHash(i+vec3(0,1,1),period),CloudHash(i+vec3(1,1,1),period),f.x),f.y),f.z);
}
/** Evaluates inverse-metre extinction identically for view and directional-light samples. */
float CloudDensity(vec3 local,ivec2 cell,vec4 data)
{
    if(data.r<=0.0||local.y<data.b*50.0||local.y>data.a*50.0) return 0.0;
    float detail=1.0;
    if(volumeDetail.w>.5)
    {
        vec2 f=local.xz-vec2(cell)*50.0;
        float edge=min(local.y-data.b*50.0,data.a*50.0-local.y);
        if(!CloudOccupied(cell+ivec2(-1,0),local.y)) edge=min(edge,f.x);
        if(!CloudOccupied(cell+ivec2(1,0),local.y)) edge=min(edge,50.0-f.x);
        if(!CloudOccupied(cell+ivec2(0,-1),local.y)) edge=min(edge,f.y);
        if(!CloudOccupied(cell+ivec2(0,1),local.y)) edge=min(edge,50.0-f.y);
        vec3 material=local+vec3(volumeAnchorUnits.x,0,volumeAnchorUnits.y);
        detail=(1.0+volumeDetail.x*(2.0*CloudNoise(material)-1.0))*smoothstep(0.0,volumeDetail.y,edge);
    }
    // Native r is extinction per fifty world units, including producer fading/perception.
    return (data.r/50.0)/volumeAnchorUnits.z*detail;
}
/** Intersects one axis robustly, including rays exactly parallel to a column face. */
bool CloudSlab(float o,float d,float lo,float hi,inout float near,inout float far)
{
    if(abs(d)<1e-8)
    {
        if(o<lo) return false;
        return o<=hi;
    }
    vec2 t=vec2(lo-o,hi-o)/d;
    near=max(near,min(t.x,t.y)); far=min(far,max(t.x,t.y));
    return far>near;
}
/** Holds monotone DDA traversal in world units without sampling unavailable map cells. */
struct CloudRay { vec3 o; vec3 d; float t; float end; ivec2 cell; ivec2 stepCell; vec2 next; vec2 delta; };
/** Enters the finite horizontal map; vertical intervals are intersected against each actual column. */
bool CloudStart(vec3 o,vec3 d,float limit,out CloudRay ray)
{
    float near=0.0,far=limit,extent=volumeOffsetWidth.w*50.0;
    if(!CloudSlab(o.x,d.x,0.0,extent,near,far)) return false;
    if(!CloudSlab(o.z,d.z,0.0,extent,near,far)) return false;
    ray.o=o; ray.d=d; ray.t=near; ray.end=far;
    ray.cell=ivec2(floor((o+d*(near+1e-4)).xz/50.0));
    ray.cell=clamp(ray.cell,ivec2(0),ivec2(int(volumeOffsetWidth.w)-1));
    ray.stepCell=ivec2(sign(d.xz));
    ray.delta=vec2(1e30); ray.next=vec2(1e30);
    for(int axis=0;axis<2;axis++) if(abs(d.xz[axis])>=1e-8)
    {
        float boundary=float(ray.cell[axis]+(d.xz[axis]>0.0?1:0))*50.0;
        ray.next[axis]=(boundary-o.xz[axis])/d.xz[axis];
        ray.delta[axis]=50.0/abs(d.xz[axis]);
    }
    return far>near;
}
/** Advances both tied axes, visiting each supported column no more than once. */
bool CloudAdvance(inout CloudRay ray)
{
    float next=min(ray.next.x,ray.next.y);
    if(next>=ray.end) return false;
    bvec2 crossed=lessThanEqual(ray.next,vec2(next));
    if(crossed.x) { ray.cell.x+=ray.stepCell.x; ray.next.x+=ray.delta.x; }
    if(crossed.y) { ray.cell.y+=ray.stepCell.y; ray.next.y+=ray.delta.y; }
    ray.t=next;
    if(any(lessThan(ray.cell,ivec2(0)))) return false;
    return all(lessThan(ray.cell,ivec2(int(volumeOffsetWidth.w))));
}
/** Clips a DDA cell against its own vertical interval, including inside and horizontal rays. */
bool CloudInterval(CloudRay ray,vec4 data,out float near,out float far)
{
    near=ray.t; far=min(ray.end,min(ray.next.x,ray.next.y));
    if(data.r<=0.0) return false;
    if(!CloudSlab(ray.o.y,ray.d.y,data.b*50.0,data.a*50.0,near,far)) return false;
    return far>near;
}
/** Integrates a bounded secondary ray through the same density and supported map domain. */
float CloudLightDepth(vec3 local,vec3 direction,float jitter)
{
    CloudRay ray;
    if(!CloudStart(local,direction,1e7,ray)) return 0.0;
    float tau=0.0;
    for(int cellIndex=0;cellIndex<402;cellIndex++)
    {
        vec4 data=CloudColumn(ray.cell); float near,far;
        if(CloudInterval(ray,data,near,far))
        {
            int count=volumeDetail.w>.5?int(volumeSamples.y):1;
            float ds=(far-near)/float(count);
            float phase=volumeDetail.w>.5?CloudSegmentJitter(jitter,ray.cell,0,0x4C494748u):.5;
            for(int i=0;i<4;i++)
            {
                if(i>=count) break;
                tau=min(1e30,tau+CloudDensity(ray.o+ray.d*(near+(float(i)+phase)*ds),ray.cell,data)*(ds*volumeAnchorUnits.z));
            }
        }
        // Saturation must include the approximate octave, including its unoccluded endpoint.
        // Keep tau finite so a zero occlusion coefficient never forms zero times infinity.
        if(tau>=64.0&&(volumeSamples.w==0.0||tau*volumeSamples.w>=32.0)) return tau;
        if(!CloudAdvance(ray)) break;
    }
    return tau;
}
#endif
