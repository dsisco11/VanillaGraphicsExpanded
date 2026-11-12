#version 450 core
in vec2 uv;
out vec4 outColor;
layout(std430,binding=0) readonly buffer ShadowReadinessResults { uint readiness[]; };
layout(std430,binding=1) readonly buffer ShadowCandidateHeaders { uint candidateHeaders[]; };
layout(std430,binding=2) readonly buffer PageEntries { uint pageEntries[]; };
/// Shows fixed source columns; rows encode readiness, detail, misses, stale fraction and recovery progress.
void main() {
#if VGE_SHADOW_DEBUG_PAGES == 1
    uint capacity=uint(pageEntries.length())/128u;
    uint slot=min(uint(clamp(uv.x,0.0,0.999999)*float(capacity)),max(1u,capacity)-1u),at=slot*128u;
    uint row=uint(clamp(uv.y,0.0,0.999999)*5.0),state=pageEntries[at+20u];
    vec3 color=state==2u?vec3(0.1,0.8,0.2):state==1u?vec3(1.0,0.6,0.1):state==3u?vec3(0.7,0.1,0.2):vec3(0.03);
    if(row==1u)color=pageEntries[at+110u]!=0u?vec3(1.0,0.5,0.1):vec3(0.1,0.5,0.2);
    if(row==2u)color=vec3(float(pageEntries[at+17u]%16u)/15.0,float(pageEntries[at+18u]%16u)/15.0,float(pageEntries[at+19u]%16u)/15.0);
    if(row==3u)color=vec3(float(pageEntries[at+21u]%256u)/255.0,0.2,0.5);
    if(row==4u)color=vec3(float(pageEntries[at+3u]%4u)/3.0,float(pageEntries[at+10u]%16u)/15.0,0.4);
    outColor=vec4(color,1.0);
#elif VGE_SHADOW_DEBUG_CANDIDATES == 1
    uint capacity=uint(candidateHeaders.length())/32u;
    uint slot=min(uint(clamp(uv.x,0.0,0.999999)*float(capacity)),max(1u,capacity)-1u),at=slot*32u;
    uint row=uint(clamp(uv.y,0.0,0.999999)*8.0),state=candidateHeaders[at];
    vec3 color=state==3u?vec3(0.1,0.8,0.2):state<3u?vec3(0.9,0.6,0.1):vec3(0.8,0.1,0.1);
    uint counter=row==1u?1u:row==2u?12u:row==3u?13u:row==4u?16u:row==5u?26u:row==6u?10u:14u;
    if(row>0u)color=vec3(clamp(log2(1.0+float(candidateHeaders[at+counter]))/16.0,0.0,1.0),0.2,0.5);
    outColor=vec4(color,1.0);
#else
    uint capacity=uint(readiness.length())/32u;
    uint light=min(uint(clamp(uv.x,0.0,0.999999)*float(capacity)),max(1u,capacity)-1u);
    uint at=light*32u,row=uint(clamp(uv.y,0.0,0.999999)*11.0);
    uint state=readiness[at+12u];
    vec3 color=state==1u?vec3(0.1,0.8,0.2):state==2u?vec3(1.0,0.6,0.05):vec3(0.65,0.08,0.08);
    if(row==1u)color=vec3(float(readiness[at+14u]%16u)/15.0,0.2,0.5);
    if(row==2u)color=vec3(min(1.0,float(readiness[at+17u])/8.0),0.0,0.0);
    if(row==3u)color=vec3(float(readiness[at+16u])/float(max(1u,readiness[at+15u])),0.3,0.1);
    if(row==4u)color=vec3(0.1,min(1.0,float(readiness[at+18u])/8.0),0.8);
    if(row==5u)color=vec3(clamp(uintBitsToFloat(readiness[at+24u]),0.0,1.0),0.2,0.1);
    if(row==6u)color=vec3(clamp(uintBitsToFloat(readiness[at+25u])*10.0,0.0,1.0),0.3,0.1);
    if(row==7u)color=vec3(clamp(uintBitsToFloat(readiness[at+26u]),0.0,1.0),0.1,0.7);
    if(row==8u)color=vec3(0.1,float(readiness[at+28u])/float(max(1u,readiness[at+27u])),0.3);
    if(row==9u)color=vec3(float(readiness[at+29u])/float(max(1u,readiness[at+27u])),0.5,0.1);
    if(row==10u)color=vec3(float(readiness[at+13u])/13.0,0.1,0.8);
    if(readiness[at]==0u && readiness[at+1u]==0u)color=vec3(0.03);
    outColor=vec4(color,1.0);
#endif
}
