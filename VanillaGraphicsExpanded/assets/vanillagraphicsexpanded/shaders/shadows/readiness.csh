#version 450 core
layout(local_size_x=1,local_size_y=1,local_size_z=1) in;
layout(std430,binding=0) readonly buffer ShadowReadinessInputs { uint sampleInputs[]; };
layout(std430,binding=1) readonly buffer PageEntries { uint entries[]; };
layout(std430,binding=2) buffer ShadowReadinessResults { uint readiness[]; };
layout(std430,binding=3) buffer ShadowReadinessHistory { uint history[]; };
layout(std430,binding=4) buffer ShadowPriorityResults { uvec4 priority[]; };
layout(binding=0) uniform usampler2DArray shadowDepthPool;
uint requestBase;
#define S(i) sampleInputs[requestBase+uint(i)]
@import "../includes/shadows/coverage.glsl"
/// Checks one coherent current or retained generation for the entire light, without mixing projection sets.
uint coverageSet(uint h,uint first,uint count,out uint staleCount,out uint misses,out vec3 metrics,out uint reason) {
    staleCount=0u;misses=0u;metrics=vec3(0);reason=4u;
    if(count==0u || first>sampleInputs[1] || count>sampleInputs[1]-min(first,sampleInputs[1])) return 0u;
    uint status=1u;
    for(uint r=0u;r<count;r++) {
        requestBase=sampleInputs[2]+(first+r)*192u;
        bool identity=true;
        for(uint i=0u;i<12u;i++) if(S(i)!=sampleInputs[h+i]) identity=false;
        if(r>0u) {
            uint anchor=sampleInputs[2]+first*192u;
            // One coherent rendered generation and one current-light generation across all cube faces/levels.
            for(uint i=12u;i<14u;i++) if(S(i)!=sampleInputs[anchor+i]) identity=false;
            for(uint i=138u;i<140u;i++) if(S(i)!=sampleInputs[anchor+i]) identity=false;
        }
        uint hash=2166136261u;
        for(uint i=0u;i<17u;i++) hash=(hash^S(i))*16777619u;
        bool found=false,stale=false;uint selected=0u,failed=4u;
        if(identity && S(116)>0u) for(uint probe=0u;probe<min(S(117),S(116));probe++) {
            uint slot=(hash+probe)%S(116);
            if(PE(slot,20)!=2u) continue;
            bool match=true;
            for(uint i=0u;i<17u;i++) if(PE(slot,i)!=S(i)) match=false;
            if(match) {
                failed=shadowCoverageReason(slot,stale);
                if(S(191)==0u)failed=14u;
                if(failed==0u && !shadowPhysicalAddress(slot))failed=11u;
                if(failed==0u) { found=true;selected=slot;break; }
            }
        }
        if(!found) { status=0u;misses++;reason=max(reason,failed); }
        else if(stale) { staleCount++; if(status!=0u) status=2u; }
        if(found) {
            uvec2 now=uvec2(S(128),S(129)),rendered=uvec2(PE(selected,46),PE(selected,47));
            uvec2 invalidated=uvec2(PE(selected,60),PE(selected,61));
            float age=shadowPairLeq(rendered,now)?shadowTickSeconds(shadowTicksSubtract(now,rendered)):0.0;
            float staleAge=stale?shadowTickSeconds(shadowTicksSubtract(now,invalidated)):0.0;
            vec3 delta=uintBitsToFloat(uvec3(S(140),S(141),S(142)));
            float motion=S(64)==1u?length(delta):degrees(2.0*asin(clamp(length(delta)*0.5,0.0,1.0)));
            metrics=max(metrics,vec3(age,staleAge,motion));
        }
    }
    if(status!=0u)reason=0u;
    return status;
}
/// Reduces complete page enclosures and recovery on GPU before participating surface draws.
void main() {
    uint light=gl_GlobalInvocationID.x;
    if(light>=sampleInputs[0]) return;
    uint h=16u+light*32u,o=light*32u,hist=sampleInputs[h+20u]*32u;
    uint first=sampleInputs[h+12u],count=sampleInputs[h+13u],detail=sampleInputs[h+16u];
    uint requestedDetail=detail,candidateStreak=0u;
    bool same=true;
    for(uint i=0u;i<12u;i++) if(history[hist+i]!=sampleInputs[h+i]) same=false;
    uvec2 previous=uvec2(history[hist+16u],history[hist+17u]);
    uvec2 frame=uvec2(sampleInputs[4],sampleInputs[5]);
    uvec2 next=previous+uvec2(1u,previous.x==0xffffffffu?1u:0u);
    uint status=0u,reason=sampleInputs[3]==0u?13u:sampleInputs[h+14u]==0u?1u:12u,staleCount=0u,misses=0u,retained=0u;vec3 metrics=vec3(0);
    if(sampleInputs[3]!=0u && sampleInputs[h+14u]!=0u && sampleInputs[h+15u]!=0u) {
        status=coverageSet(h,first,count,staleCount,misses,metrics,reason);
        if(status==1u) {
            bool candidateContinuous=same && all(equal(next,frame)) && history[hist+12u]==requestedDetail;
            candidateStreak=min((candidateContinuous?history[hist+13u]:0u)+1u,sampleInputs[6]);
        }
        bool refining=same && history[hist+20u]!=0u && history[hist+18u]<requestedDetail &&
            status==1u && candidateStreak<sampleInputs[6];
        if((status==0u || refining) && sampleInputs[h+19u]>0u) {
            uint oldFirst=sampleInputs[h+18u],end=oldFirst+sampleInputs[h+19u];
            while(oldFirst<end) {
                uint anchor=sampleInputs[2]+oldFirst*192u,oldCount=1u;
                while(oldFirst+oldCount<end) {
                    uint at=sampleInputs[2]+(oldFirst+oldCount)*192u;
                    if(sampleInputs[at+12u]!=sampleInputs[anchor+12u] || sampleInputs[at+13u]!=sampleInputs[anchor+13u] ||
                        sampleInputs[at+147u]!=sampleInputs[anchor+147u]) break;
                    oldCount++;
                }
                uint oldStale,oldMiss,oldReason;vec3 oldMetrics;
                uint oldStatus=coverageSet(h,oldFirst,oldCount,oldStale,oldMiss,oldMetrics,oldReason);
                if(oldStatus!=0u) { status=oldStatus;staleCount=oldStale;first=oldFirst;count=oldCount;retained=1u;reason=0u;
                    metrics=oldMetrics;detail=sampleInputs[anchor+147u];break; }
                reason=max(reason,oldReason);oldFirst+=oldCount;
            }
        }
    }
    bool consecutive=same && all(equal(next,frame)) && history[hist+18u]==detail;
    uint streak=consecutive?history[hist+19u]:0u;
    bool downgrading=same && all(equal(next,frame)) && history[hist+20u]!=0u && detail<history[hist+18u] && status!=0u;
    bool wasReady=(consecutive && history[hist+20u]!=0u)||downgrading;
    if(status==1u) streak=detail==requestedDetail?candidateStreak:min(streak+1u,sampleInputs[6]);
    else streak=0u;
    if(status!=0u && !wasReady && (status!=1u || streak<sampleInputs[6])) { status=0u;reason=3u; }
    if(!same)for(uint i=21u;i<32u;i++)history[hist+i]=0u;
    // Include all contributing sources, even when arbitration rejected their shadow work.
    if(sampleInputs[h+21u]!=0u) {
        if(history[hist+21u]!=0xffffffffu)history[hist+21u]++;
        uint category=status==1u?23u:status==2u?24u:25u;
        if(history[hist+category]!=0xffffffffu)history[hist+category]++;
        if(sampleInputs[h+15u]!=0u && history[hist+22u]!=0xffffffffu)history[hist+22u]++;
        if(wasReady && status==0u && history[hist+26u]!=0xffffffffu)history[hist+26u]++;
        if(!wasReady && status!=0u && history[hist+27u]!=0xffffffffu)history[hist+27u]++;
    }
    for(uint i=0u;i<12u;i++) { history[hist+i]=sampleInputs[h+i];readiness[o+i]=sampleInputs[h+i]; }
    history[hist+12u]=requestedDetail;history[hist+13u]=candidateStreak;
    history[hist+16u]=frame.x;history[hist+17u]=frame.y;history[hist+18u]=detail;history[hist+19u]=streak;history[hist+20u]=status;
    readiness[o+12u]=status;readiness[o+13u]=reason;readiness[o+14u]=detail;readiness[o+15u]=count;
    readiness[o+16u]=staleCount;readiness[o+17u]=misses;readiness[o+18u]=detail==requestedDetail?streak:candidateStreak;
    readiness[o+19u]=frame.x;readiness[o+20u]=frame.y;
    readiness[o+21u]=first;readiness[o+22u]=count;readiness[o+23u]=retained;
    readiness[o+24u]=floatBitsToUint(metrics.x);readiness[o+25u]=floatBitsToUint(metrics.y);readiness[o+26u]=floatBitsToUint(metrics.z);
    readiness[o+27u]=history[hist+21u];readiness[o+28u]=history[hist+23u];readiness[o+29u]=history[hist+24u];
    readiness[o+30u]=history[hist+26u];readiness[o+31u]=history[hist+27u];
    priority[light]=uvec4(status,floatBitsToUint(metrics.x),floatBitsToUint(metrics.y),reason);
}
