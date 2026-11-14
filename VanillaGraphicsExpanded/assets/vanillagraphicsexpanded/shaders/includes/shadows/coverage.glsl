@import "certificates.glsl"
#ifndef PE
#define PE(s,i) entries[(s)*128u+uint(i)]
#endif
/// Validates the explicit physical layer and tile bounds during whole-light readiness.
bool shadowPhysicalAddress(uint slot) {
    ivec3 dimensions=textureSize(shadowDepthPool,0);
    return S(65)>0u && PE(slot,17)<S(119) && PE(slot,17)<uint(dimensions.z) &&
        all(lessThan(uvec2(PE(slot,18),PE(slot,19)),uvec2(dimensions.xy)/S(65)));
}
/// Distinguishes complete current coverage from explicitly bounded retained coverage for every tap.
uint shadowCoverageReason(uint slot,out bool stale) {
    stale=false;
    for(uint i=0u;i<3u;i++) {
        uint a=28u+2u*i,b=34u+2u*i;
        uvec2 lo=uvec2(PE(slot,a),PE(slot,a+1u)),hi=uvec2(PE(slot,b),PE(slot,b+1u));
        uvec2 demandLo=uvec2(S(a),S(a+1u)),demandHi=uvec2(S(b),S(b+1u));
        if(!shadowEndpointValid(lo)||!shadowEndpointValid(hi)||!shadowEndpointValid(demandLo)||!shadowEndpointValid(demandHi)||
            !shadowEndpointLeq(lo,demandLo)||!shadowEndpointLeq(demandHi,hi)||!shadowEndpointLeq(demandLo,demandHi)) return 5u;
    }
    // Never reinterpret retained depth through another projection, even if its generation was mislabelled.
    for(uint i=64u;i<98u;i++) {
        if(S(64)==1u && (i==90u || i==91u)) continue; // Point filter width is receiver-dependent.
        if(PE(slot,i)!=S(i)) return 6u;
    }
    bool current=PE(slot,110)==0u && PE(slot,12)==S(138) && PE(slot,13)==S(139)
        && PE(slot,40)==S(40) && PE(slot,41)==S(41) && PE(slot,42)==S(42)
        && PE(slot,43)==S(43) && PE(slot,44)==S(44) && PE(slot,45)==S(45);
    if(current) return 0u;
    // Unknown invalidation time cannot become a newly started grace period at lookup time.
    if(PE(slot,110)!=1u) return 7u;
    uvec2 rendered=uvec2(PE(slot,46),PE(slot,47)),invalidated=uvec2(PE(slot,60),PE(slot,61));
    uvec2 now=uvec2(S(128),S(129)),renderAge=uvec2(S(130),S(131)),staleAge=uvec2(S(132),S(133));
    if(!shadowPairLeq(rendered,invalidated)||!shadowPairLeq(invalidated,now)) return 7u;
    if(all(equal(renderAge,uvec2(0))) || !shadowPairLeq(shadowTicksSubtract(now,rendered),renderAge)) return 8u;
    if(all(equal(staleAge,uvec2(0))) || !shadowPairLeq(shadowTicksSubtract(now,invalidated),staleAge)) return 9u;
    // Setup resolves the precise inclusive motion limit before FP32 demotion.
    // This value applies only after matching the immutable rendered mapping above.
    if(S(134)!=1u) return 10u;
    stale=true;
    return 0u;
}
