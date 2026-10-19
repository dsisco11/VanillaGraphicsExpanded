#version 430 core
@import "./parameters.glsl"
@import "./density.glsl"
@import "./lookup.glsl"
layout(local_size_x=64) in;
layout(std140) uniform LightingScatteringInputs
{
    vec4 scatteringSun,scatteringMoon,scatteringSolar,scatteringLunar,scatteringEnvironment;
    ivec4 scatteringAlignment;
    vec4 scatteringEncoding;
};
layout(rgba16f) uniform image3D scatteringTarget;
layout(std430) coherent buffer ScatteringRegions { uint scatteringRegions[]; };
layout(std430) readonly buffer ScatteringJobs { uint scatteringJobs[]; };

/** Clips to finite horizontal native support only; vertical lighting coverage never truncates density. */
float ScatteringLength(vec3 q,vec3 d)
{
    float distance=300.0,width=candidateOffsetWidth.w*50.0;
    if(d.x>0.0) distance=min(distance,(width-q.x)/d.x);
    else if(d.x<0.0) distance=min(distance,-q.x/d.x);
    if(d.z>0.0) distance=min(distance,(width-q.z)/d.z);
    else if(d.z<0.0) distance=min(distance,-q.z/d.z);
    return max(0.0,distance);
}
/** Normalized Henyey-Greenstein per steradian with the outward receiver-to-emitter convention. */
float ScatteringPhase(float cosine)
{
    float denominator=1.36-1.2*clamp(cosine,-1.0,1.0);
    return 0.64/(12.566370614359172*denominator*sqrt(denominator));
}
/** Gathers directional first scattering exactly once, without a nested shadow ray march. */
vec3 ScatteringFirst(vec3 q,vec3 d)
{
    float sun=1.0,moon=1.0;
#if VGE_LIGHTING_VOLUME_DIRECT
    if(any(greaterThan(scatteringSolar.rgb,vec3(0)))) sun=CandidateTransmission(q,0);
    if(any(greaterThan(scatteringLunar.rgb,vec3(0)))) moon=CandidateTransmission(q,1);
#endif
    return 0.9*(scatteringSolar.rgb*(sun*ScatteringPhase(dot(d,scatteringSun.xyz)))+
        scatteringLunar.rgb*(moon*ScatteringPhase(dot(d,scatteringMoon.xyz))));
}
/** Integrates one declared twelve-midpoint segment, preserving transmission across empty gaps. */
vec3 ScatteringRay(vec3 q,vec3 d,inout bool valid)
{
    float distance=ScatteringLength(q,d),ds=distance/12.0*candidateAnchorUnits.z;
    float transmission=1.0;
    vec3 result=vec3(0);
    CandidateDensityCache cache=CandidateDensityCacheStart();
    for(int i=0;i<12;i++)
    {
        vec3 y=q+d*(distance*(float(i)+0.5)/12.0);
        ivec2 cell=ivec2(floor(y.xz/50.0));
        vec4 column=CandidateDensityColumn(cell,cache);
        float sigma=CandidateDensity(y,cell,column,cache);
        if(isnan(sigma)||isinf(sigma)||sigma<0.0) {valid=false;return vec3(0);}
        float tau=ds>0.0?min(sigma,1e30/ds)*ds:0.0;
        float stepT=exp(-min(tau,1e30)),alpha=1.0-stepT;
        if(sigma>0.0)
        {
            vec3 source=ScatteringFirst(y,d)+CandidateDiffuse(y,scatteringEnvironment.rgb);
            result+=transmission*alpha*source;
        }
        transmission*=stepT;
    }
    return result+transmission*scatteringEnvironment.rgb;
}
/** Refines one region per workgroup; all nodes read the old field and collectively reject nonfinite results. */
void main()
{
    uint slot=gl_WorkGroupID.x;
    if(slot>=min(scatteringJobs[5],256u)) return;
    uint region=scatteringJobs[576u+slot],base=region*16u;
    if((scatteringRegions[base+1u]&0x40000u)==0u) return;
    if(gl_LocalInvocationIndex==0u) atomicAnd(scatteringRegions[base+15u],~4u);
    barrier();
    int side=scatteringAlignment.w;
    ivec3 r=ivec3(int(region)%side,(int(region)/side)%8,int(region)/(side*8));
    uint lane=gl_LocalInvocationIndex;
    ivec3 p=r*4-scatteringAlignment.xyz+ivec3(int(lane&3u),int((lane>>2u)&3u),int(lane>>4u))+1;
    ivec3 size=ivec3(candidateGridCap.xyz);
    bool admitted=all(greaterThan(p,ivec3(0)))&&all(lessThan(p,size-1));
    vec4 node=vec4(0);uint mask=0u;vec3 result=vec3(0);
    if(admitted)
    {
        node=imageLoad(scatteringTarget,p);mask=uint(node.a);
        if((mask&1u)!=0u)
        {
            vec3 q=candidateFirstNodeScale.xyz+vec3(p)*vec3(100,25,100);
            bool valid=true;
            // Divide each contribution before summation to keep the angular average within admitted bounds.
            for(int axis=0;axis<3;axis++)
            {
                vec3 d=vec3(0);d[axis]=1.0;
                result+=ScatteringRay(q,d,valid)*(0.9/6.0);
                result+=ScatteringRay(q,-d,valid)*(0.9/6.0);
            }
            if(!valid||any(isnan(result))||any(isinf(result))) atomicOr(scatteringRegions[base+15u],4u);
        }
    }
    memoryBarrierBuffer();barrier();
    if(admitted)
    {
        bool success=(scatteringRegions[base+15u]&4u)==0u;
        mask&=~8u;
        if(success&&(mask&1u)!=0u) mask|=8u;
        node.rgb=success?clamp(result,vec3(0),vec3(scatteringEncoding.y))/scatteringEncoding.x:vec3(0);
        node.a=float(mask);imageStore(scatteringTarget,p,node);
    }
}
