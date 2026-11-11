// Fragment hosts provide shadowWriteEntries[], shadowWritableAssignments[] and four r32ui
// images shadowBank0..3 through their generated binding contracts. No depth attachment writes.
struct ShadowWriteView {
    // Last two key words are the signed base page; the remaining identity is exact.
    uint key[17];
    uint capacity;
    uint probes;
    uint pageSize;
    uint residentBanks;
    uvec2 batch;
};
#define WE(s,i) shadowWriteEntries[(s)*128u+uint(i)]
/// Accumulates one finite projected depth only into an exact frozen replacement assignment.
bool shadowAtomicDepth(ShadowWriteView view, ivec2 viewTexel, float depth) {
    if(gl_HelperInvocation || isnan(depth) || isinf(depth) || depth<0.0 || depth>1.0 ||
        view.pageSize==0u || view.pageSize>2147483647u || view.capacity==0u ||
        view.capacity>uint(shadowWriteEntries.length())/128u ||
        view.capacity>uint(shadowWritableAssignments.length())) return false;
    // A signed page origin plus small local texels preserves the whole page-key
    // range without optional integer-64 arithmetic or overflowing absolute texels.
    dvec2 relativePage=floor(dvec2(viewTexel)/double(view.pageSize));
    dvec2 absolutePage=dvec2(ivec2(view.key[15],view.key[16]))+relativePage;
    if(any(lessThan(absolutePage,dvec2(-2147483648.0lf))) ||
        any(greaterThan(absolutePage,dvec2(2147483647.0lf)))) return false;
    ivec2 page=ivec2(absolutePage);
    ivec2 local=ivec2(dvec2(viewTexel)-relativePage*double(view.pageSize));
    uint hash=2166136261u;
    for(uint i=0u;i<17u;i++) {
        uint value=i==15u?uint(page.x):i==16u?uint(page.y):view.key[i];
        hash=(hash^value)*16777619u;
    }
    for(uint probe=0u;probe<min(view.probes,view.capacity);probe++) {
        uint slot=(hash+probe)%view.capacity;
        if(WE(slot,20)!=1u || WE(slot,27)!=1u) continue;
        uvec4 expected=shadowWritableAssignments[slot];
        if(any(notEqual(expected.xy,uvec2(WE(slot,21),WE(slot,22)))) ||
            any(notEqual(expected.zw,view.batch)) || all(equal(expected.xy,uvec2(0)))) continue;
        bool match=true;
        for(uint i=0u;i<17u;i++) {
            uint value=i==15u?uint(page.x):i==16u?uint(page.y):view.key[i];
            if(WE(slot,i)!=value) match=false;
        }
        if(!match) continue;
        uint bank=WE(slot,17);
        if(bank>=view.residentBanks || bank>=4u) return false;
        ivec2 dimensions=bank==0u?imageSize(shadowBank0):bank==1u?imageSize(shadowBank1):
            bank==2u?imageSize(shadowBank2):imageSize(shadowBank3);
        // Divide before multiplying so malformed tile coordinates cannot wrap into valid storage.
        uvec2 tile=uvec2(WE(slot,18),WE(slot,19));
        if(any(greaterThanEqual(tile,uvec2(dimensions)/view.pageSize))) return false;
        ivec2 at=ivec2(tile*view.pageSize)+local;
        uint encoded=floatBitsToUint(depth==0.0?0.0:depth);
        if(bank==0u) imageAtomicMin(shadowBank0,at,encoded);
        else if(bank==1u) imageAtomicMin(shadowBank1,at,encoded);
        else if(bank==2u) imageAtomicMin(shadowBank2,at,encoded);
        else imageAtomicMin(shadowBank3,at,encoded);
        return true;
    }
    return false;
}
