// Fragment hosts provide shadowWriteEntries[], shadowWritableAssignments[] and one r32ui
// array image shadowDepthPool through their generated binding contracts. No depth attachment writes.
struct ShadowWriteView {
    // Last two key words are the signed base page; the remaining identity is exact.
    uint key[17];
    uint capacity;
    uint probes;
    uint pageSize;
    uint arrayLayers;
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
    ivec2 origin=ivec2(view.key[15],view.key[16]),page,local;
    for(int axis=0;axis<2;axis++) {
        // Baseline GLSL leaves negative remainder operands undefined. Unsigned
        // magnitude division also handles INT_MIN without signed negation.
        bool negative=viewTexel[axis]<0;
        uint magnitude=negative?0u-uint(viewTexel[axis]):uint(viewTexel[axis]);
        uint quotient=magnitude/view.pageSize,remainder=magnitude%view.pageSize;
        int delta=negative?int(0u-quotient):int(quotient),base=origin[axis];
        local[axis]=int(remainder);
        if(negative && remainder!=0u) { delta--;local[axis]=int(view.pageSize-remainder); }
        if((delta>0 && base>2147483647-delta) || (delta<0 && base<(-2147483647-1)-delta)) return false;
        page[axis]=base+delta;
    }
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
        uint layer=WE(slot,17);
        ivec3 dimensions=imageSize(shadowDepthPool);
        if(layer>=view.arrayLayers || layer>=uint(dimensions.z)) return false;
        // Divide before multiplying so malformed tile coordinates cannot wrap into valid storage.
        uvec2 tile=uvec2(WE(slot,18),WE(slot,19));
        if(any(greaterThanEqual(tile,uvec2(dimensions.xy)/view.pageSize))) return false;
        ivec2 at=ivec2(tile*view.pageSize)+local;
        uint encoded=floatBitsToUint(depth==0.0?0.0:depth);
        imageAtomicMin(shadowDepthPool,ivec3(at,int(layer)),encoded);
        return true;
    }
    return false;
}
