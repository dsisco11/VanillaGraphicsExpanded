#ifndef VGE_LIGHTING_VOLUME_LOOKUP
#define VGE_LIGHTING_VOLUME_LOOKUP
uniform sampler3D candidateDirect;
uniform sampler3D candidateDiffuse;
/** Admits all eight exact mask values before one hardware-filtered lighting lookup. */
bool CandidateFootprint(vec3 q,uint channel,out vec3 uv)
{
    vec3 node=(q-candidateFirstNodeScale.xyz)/vec3(100,25,100);
    ivec3 lo=ivec3(floor(node)),size=ivec3(candidateGridCap.xyz);
    uv=(node+0.5)/candidateGridCap.xyz;
    if(any(isnan(node)) || any(isinf(node)) || any(lessThan(lo,ivec3(0))) ||
        any(greaterThanEqual(lo+1,size))) return false;
    for(int z=0;z<2;z++) for(int y=0;y<2;y++) for(int x=0;x<2;x++)
    {
        uint mask=uint(texelFetch(candidateDiffuse,lo+ivec3(x,y,z),0).a);
        if((mask&(channel|1u))!=(channel|1u)) return false;
    }
    return true;
}
/** Returns an independent source channel, using unshadowed light outside valid coverage. */
float CandidateTransmission(vec3 q,int source)
{
    vec3 uv;
    if(!CandidateFootprint(q,source==0?2u:4u,uv)) return 1.0;
    return clamp(textureLod(candidateDirect,uv,0)[source],0.0,1.0);
}
/** Decodes only RGB; alpha remains an exact, unscaled metadata field. */
vec3 CandidateDiffuse(vec3 q,vec3 environment)
{
    vec3 uv;
    if(!CandidateFootprint(q,8u,uv)) return 0.9*environment;
    return clamp(textureLod(candidateDiffuse,uv,0).rgb*candidateFirstNodeScale.w,
        vec3(0),vec3(candidateGridCap.w));
}
#endif
