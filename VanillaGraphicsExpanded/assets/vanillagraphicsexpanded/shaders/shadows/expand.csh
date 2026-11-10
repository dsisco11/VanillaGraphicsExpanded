#version 450 core
layout(local_size_x=64) in;
layout(std430,binding=0) readonly buffer DrawInputs { uint inputWords[]; };
layout(std430,binding=1) readonly buffer DrawWorld { uint worldWords[]; };
layout(std430,binding=2) readonly buffer DrawRanges { uint nativeRanges[]; };
layout(std430,binding=3) readonly buffer DrawCandidates { uint headers[]; };
layout(std430,binding=4) readonly buffer DrawCoordinates { ivec4 coordinates[]; };
layout(std430,binding=5) readonly buffer DrawPages { uint pageWords[]; };
layout(std430,binding=6) buffer DrawScratch { uint scratch[]; };
layout(std430,binding=7) buffer DrawOutput { uint outputWords[]; };
#define I(n) inputWords[uint(n)]
#define Q(p,n) inputWords[I(21)+(p)*96u+uint(n)]
#define R(r,n) inputWords[I(22)+(r)*12u+uint(n)]
#define E(s,n) pageWords[(s)*64u+uint(n)]
#define S(p,n) scratch[I(28)+(p)*4u+uint(n)]

/// Rejects the entire temporary batch; no failed selection can clear or publish a page.
void fail(uint reason) { atomicOr(outputWords[0],reason); }
/// Charges all candidate, lookup, range and duplicate probes to one aggregate visit allowance.
bool visit() {
    uint count=atomicAdd(outputWords[1],1u);
    if(count<I(14)) return true;
    fail(1u); return false;
}
/// Resolves a current coordinate with the publication's exact bounded hash convention.
uint chunk(ivec4 c) {
    uint h=2166136261u;
    h=(h^uint(c.w))*16777619u; h=(h^uint(c.x))*16777619u;
    h=(h^uint(c.y))*16777619u; h=(h^uint(c.z))*16777619u;
    for(uint probe=0u;probe<I(6);probe++) {
        if(!visit()) return I(5);
        uint slot=(h+probe)&(I(5)-1u), b=slot*16u;
        if(worldWords[b+10u]==0u) return I(5);
        if(all(equal(ivec4(worldWords[b],worldWords[b+1u],worldWords[b+2u],worldWords[b+3u]),c))) return slot;
    }
    return I(5);
}
/// Finds the exact unfinished replacement, including receiver enclosure and applied scene revisions.
uint replacement(uint p) {
    uint h=2166136261u;
    for(uint i=0u;i<17u;i++) h=(h^Q(p,i))*16777619u;
    for(uint probe=0u;probe<I(9);probe++) {
        if(!visit()) return I(8);
        uint slot=(h+probe)%I(8);
        if(E(slot,20)!=1u || slot>=I(11)) continue;
        bool same=true;
        for(uint i=0u;i<17u;i++) if(E(slot,i)!=Q(p,i)) same=false;
        for(uint i=28u;i<46u;i++) if(E(slot,i)!=Q(p,i)) same=false;
        if(same) return slot;
    }
    return I(8);
}
/// Conservatively rejects only boxes wholly outside a page side plane, retaining near/source crossings.
bool intersects(uint p,ivec3 coordinate) {
    dvec3 origin=dvec3(packDouble2x32(uvec2(Q(p,84),Q(p,85))),
        packDouble2x32(uvec2(Q(p,86),Q(p,87))),packDouble2x32(uvec2(Q(p,88),Q(p,89))));
    dmat4 matrix;
    for(uint col=0u;col<4u;col++) for(uint row=0u;row<4u;row++)
        matrix[col][row]=double(uintBitsToFloat(Q(p,64u+col*4u+row)));
    bool left=true,right=true,bottom=true,top=true;
    double padding=double(uintBitsToFloat(Q(p,90)));
    for(uint corner=0u;corner<8u;corner++) {
        dvec3 world=dvec3(coordinate)*32.0+dvec3((corner&1u)!=0u?32.0+padding:-padding,
            (corner&2u)!=0u?32.0+padding:-padding,(corner&4u)!=0u?32.0+padding:-padding);
        dvec4 clip=matrix*dvec4(world-origin,1.0);
        // Expand floating point side planes; uncertain/nonfinite arithmetic retains the box.
        if(any(isnan(clip))||any(isinf(clip))) return true;
        double error=1e-5*(1.0+max(max(abs(clip.x),abs(clip.y)),abs(clip.w)));
        left=left && clip.x < -clip.w-error; right=right && clip.x > clip.w+error;
        bottom=bottom && clip.y < -clip.w-error; top=top && clip.y > clip.w+error;
    }
    return !(left||right||bottom||top);
}
/// Expands one completed candidate collection; atomic scalar keys cannot expose partially initialized identities.
void expand(uint p) {
    if(p>=I(1)) return;
    if(I(32)==0u) { fail(2u); return; }
    uint h=Q(p,80)*32u;
    if(headers[h]!=3u || headers[h+1u]>I(7) || headers[h+16u]!=0u
        || headers[h+24u]!=Q(p,81) || headers[h+25u]!=Q(p,82)
        || headers[h+21u]!=I(19) || headers[h+22u]!=I(20)
        || headers[h+28u]!=I(17) || headers[h+29u]!=I(18)) { fail(4u); return; }
    uint physical=replacement(p);
    if(physical==I(8)) { fail(8u); return; }
    S(p,0)=physical; S(p,1)=E(physical,21); S(p,2)=E(physical,22); S(p,3)=1u;
    uint array=physical/I(10);
    for(uint c=0u;c<headers[h+1u];c++) {
        if(!visit()) return;
        ivec4 coordinate=coordinates[Q(p,80)*I(7)+c];
        if(coordinate.w!=int(Q(p,2))) { fail(4u); return; }
        if(!intersects(p,coordinate.xyz)) continue;
        uint slot=chunk(coordinate);
        if(slot==I(5) || (worldWords[slot*16u+10u]&2u)==0u) { fail(16u); return; }
        uint first=worldWords[slot*16u+12u],count=worldWords[slot*16u+13u];
        if(first>I(2)||count>I(2)-first) { fail(16u); return; }
        for(uint r=first;r<first+count;r++) {
            if(!visit()) return;
            if(R(r,7)!=nativeRanges[r*8u+6u] || R(r,8)!=nativeRanges[r*8u+7u]) { fail(16u); return; }
            if(R(r,2)==0u) continue;
            uint key=p*I(2)+r+1u,hash=key*2654435761u;
            bool inserted=false;
            for(uint probe=0u;probe<I(13);probe++) {
                if(!visit()) return;
                uint address=(hash+probe)&(I(12)-1u),old=atomicCompSwap(scratch[address],0u,key);
                if(old==key) { inserted=true; break; }
                if(old!=0u) continue;
                uint command=R(r,1)+array*R(r,11);
                if(command>=I(3)) { fail(16u); return; }
                atomicAdd(scratch[I(29)+command],1u);
                atomicAdd(outputWords[2],1u);
                uint cost=atomicAdd(outputWords[3],R(r,3));
                if(R(r,3)>I(16)||cost>I(16)-R(r,3)) fail(32u);
                inserted=true; break;
            }
            if(!inserted) { fail(64u); return; }
        }
    }
}
/// Computes exclusive instance offsets and initializes every submitted argument, including inactive slots.
void compact() {
    uint total=0u;
    for(uint c=0u;c<I(3);c++) {
        uint count=scratch[I(29)+c]; scratch[I(30)+c]=total;
        if(count>I(15)||total>I(15)-count) { fail(128u); break; }
        total+=count;
    }
    outputWords[4]=total;
    // Reset already zeroed every slot. A failure leaves all commands, clears and certificates inactive.
    if(outputWords[0]!=0u) return;
    for(uint c=0u;c<I(3);c++) {
        uint dst=32u+c*5u,src=I(23)+c*5u;
        outputWords[dst]=I(src); outputWords[dst+1u]=scratch[I(29)+c];
        outputWords[dst+2u]=I(src+2u); outputWords[dst+3u]=I(src+3u);
        outputWords[dst+4u]=scratch[I(30)+c];
    }
    for(uint p=0u;p<I(1);p++) {
        uint physical=S(p,0),array=physical/I(10),clear=I(25)+(array*I(1)+p)*4u;
        outputWords[clear]=3u; outputWords[clear+1u]=1u; outputWords[clear+3u]=p;
        uint metadata=I(27)+p*20u;
        outputWords[metadata+16u]=physical%I(10); outputWords[metadata+18u]=p;
        uint publication=I(26)+p*64u;
        for(uint i=0u;i<64u;i++) outputWords[publication+i]=Q(p,i);
        outputWords[publication+20u]=2u; outputWords[publication+21u]=S(p,1);
        outputWords[publication+22u]=S(p,2); outputWords[publication+27u]=1u;
    }
}
/// Emits compact page instances from deduplicated keys, repeating geometry only for genuinely selected pages.
void emit(uint address) {
    if(address>=I(12)||outputWords[0]!=0u) return;
    uint key=scratch[address]; if(key==0u) return;
    uint p=(key-1u)/I(2),r=(key-1u)%I(2),physical=S(p,0);
    uint command=R(r,1)+(physical/I(10))*R(r,11);
    uint index=scratch[I(30)+command]+atomicAdd(scratch[I(31)+command],1u);
    uint dst=I(24)+index*20u;
    for(uint i=0u;i<16u;i++) outputWords[dst+i]=Q(p,64u+i);
    outputWords[dst+16u]=physical%I(10); outputWords[dst+17u]=physical/I(10);
    outputWords[dst+18u]=p; outputWords[dst+19u]=0u;
}
/// Executes explicit GPU stages separated by host-declared storage/command/vertex visibility barriers.
void main() {
    uint index=gl_GlobalInvocationID.x;
    if(I(0)==0u) {
        if(index<uint(scratch.length())) scratch[index]=0u;
        if(index<uint(outputWords.length())) outputWords[index]=0u;
    } else if(I(0)==1u) expand(index);
    else if(I(0)==2u && index==0u) compact();
    else if(I(0)==3u) emit(index);
}
