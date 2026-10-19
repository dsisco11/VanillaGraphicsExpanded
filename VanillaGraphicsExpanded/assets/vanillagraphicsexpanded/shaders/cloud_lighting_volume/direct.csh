#version 430 core
@import "./parameters.glsl"
@import "./density.glsl"
layout(local_size_x=64) in;
layout(std140) uniform LightingDirectInputs { vec4 directDirectionSource; ivec4 directRegionAlignment; };
layout(rg16f) uniform image3D directTarget;
layout(rgba16f) uniform image3D directNodes;
layout(std430) coherent buffer DirectRegions { uint directRegions[]; };
layout(std430) readonly buffer DirectJobs { uint directJobs[]; };

/** Clips only to native horizontal support; zero horizontal components have no exit before the ray cap. */
float DirectLength(vec3 q,vec3 direction)
{
    float limit=1500.0, width=candidateOffsetWidth.w*50.0;
    if(direction.x>0.0) limit=min(limit,(width-q.x)/direction.x);
    else if(direction.x<0.0) limit=min(limit,-q.x/direction.x);
    if(direction.z>0.0) limit=min(limit,(width-q.z)/direction.z);
    else if(direction.z<0.0) limit=min(limit,-q.z/direction.z);
    return max(0.0,limit);
}
/** Integrates exactly 32 uniform midpoints with physical weights and nonnegative finite optical-depth saturation. */
float DirectTransmission(vec3 q,vec3 direction,out bool valid)
{
    float distance=DirectLength(q,direction), stepWorld=distance/32.0;
    float stepMetres=stepWorld*candidateAnchorUnits.z, tau=0.0;
    valid=true;
    CandidateDensityCache cache=CandidateDensityCacheStart();
    for(int i=0;i<32;i++)
    {
        vec3 p=q+direction*(distance*(float(i)+0.5)/32.0);
        ivec2 cell=ivec2(floor(p.xz/50.0));
        vec4 column=CandidateDensityColumn(cell,cache);
        float sigma=CandidateDensity(p,cell,column,cache);
        if(isnan(sigma)||isinf(sigma)||sigma<0.0) {valid=false;return 1.0;}
        // Clamp before multiplication, so large finite extinction cannot overflow the optical-depth arithmetic.
        float bounded=stepMetres>0.0?min(sigma,1e30/stepMetres):0.0;
        tau=min(1e30,tau+bounded*stepMetres);
    }
    return clamp(exp(-tau),0.0,1.0);
}
/** Gives one workgroup each selected region-source job and one invocation each admitted lattice node. */
void main()
{
    uint slot=gl_WorkGroupID.x, channel=uint(directDirectionSource.w), bit=1u<<channel;
    if(slot>=min(directJobs[4],512u)) return;
    uint job=directJobs[64u+slot];
    if((job&1u)!=channel) return;
    uint region=job/2u, base=region*16u;
    if((directRegions[base+1u]&(bit<<16u))==0u) return;
    if(gl_LocalInvocationIndex==0u) atomicAnd(directRegions[base+15u],~bit);
    barrier();
    int side=directRegionAlignment.w;
    ivec3 r=ivec3(int(region)%side,(int(region)/side)%8,int(region)/(side*8));
    uint lane=gl_LocalInvocationIndex;
    ivec3 p=r*4-directRegionAlignment.xyz+ivec3(int(lane&3u),int((lane>>2u)&3u),int(lane>>4u))+1;
    ivec3 size=ivec3(candidateGridCap.xyz);
    bool admitted=all(greaterThan(p,ivec3(0)))&&all(lessThan(p,size-1));
    vec4 node=vec4(0), direct=vec4(1,1,0,0);
    uint mask=0u;
    float transmission=1.0;
    if(admitted)
    {
        node=imageLoad(directNodes,p);mask=uint(node.a);
        direct=imageLoad(directTarget,p);
        if((mask&1u)!=0u)
        {
            bool valid;
            vec3 q=candidateFirstNodeScale.xyz+vec3(p)*vec3(100,25,100);
            transmission=DirectTransmission(q,directDirectionSource.xyz,valid);
            if(!valid) atomicOr(directRegions[base+15u],bit);
        }
    }
    // Region failure is collective; no node in an incomplete region may advertise this channel as valid.
    memoryBarrierBuffer();barrier();
    if(admitted)
    {
        bool success=(directRegions[base+15u]&bit)==0u;
        direct[int(channel)]=success?transmission:1.0;
        mask&=~(bit<<1u);
        if(success && (mask&1u)!=0u) mask|=bit<<1u;
        node.a=float(mask);
        imageStore(directTarget,p,direct);imageStore(directNodes,p,node);
    }
}
