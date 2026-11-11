#version 450 core
layout(local_size_x=1, local_size_y=1, local_size_z=1) in;
layout(std430,binding=0) readonly buffer PageCommands { uint commands[]; };
layout(std430,binding=1) buffer PageEntries { uint entries[]; };
layout(std430,binding=2) buffer PageResults { uint results[]; };
layout(std430,binding=3) buffer WritableAssignments { uvec4 writable[]; };
// Command prefix: capacity, resident slots, probes, target slots, batch low/high, completed low/high.
// Each 128-word entry has key[0..16], state[20], assignment[21..22], retirement[23..24],
// required batch[25..26], coverage doubles[28..39], world/scene epoch/sequence[40..45].
// Rendered time[46..47], first invalidation[60..61], projection[64..97], invalidated flag[110].
#define C(i) commands[16u+uint(i)]
#define E(s,i) entries[(s)*128u+uint(i)]
/// Compares low/high unsigned pairs without requiring 64-bit integer shader extensions.
bool leq(uvec2 a,uvec2 b) { return a.y<b.y || (a.y==b.y && a.x<=b.x); }
/// Checks the complete identity, never merely its hash.
bool keyEqual(uint s,uint count) { for(uint i=0u;i<count;i++) if(E(s,i)!=C(i)) return false; return true; }
/// Compares the three complete dependency stamps explicitly, preserving both words of each revision.
bool dependenciesEqual(uint s) {
    return E(s,40)==C(40) && E(s,41)==C(41) && E(s,42)==C(42)
        && E(s,43)==C(43) && E(s,44)==C(44) && E(s,45)==C(45);
}
/// Tests bounded depth-inclusive enclosure plus exact applied scene dependencies.
bool covers(uint s) {
    for(uint i=0u;i<3u;i++) {
        double lo=packDouble2x32(uvec2(E(s,28u+2u*i),E(s,29u+2u*i)));
        double hi=packDouble2x32(uvec2(E(s,34u+2u*i),E(s,35u+2u*i)));
        double a=packDouble2x32(uvec2(C(28u+2u*i),C(29u+2u*i)));
        double b=packDouble2x32(uvec2(C(34u+2u*i),C(35u+2u*i)));
        if(isnan(a)||isnan(b)||isinf(a)||isinf(b)||a>b||a<lo||b>hi) return false;
    }
    return E(s,110)==0u && dependenciesEqual(s);
}
/// Moves a slot to the fixed retirement queue; its tile cannot be reused until the batch fence signals.
void retire(uint s) { E(s,20)=3u; E(s,23)=commands[4]; E(s,24)=commands[5]; }
/// Accumulates the current required enclosure separately from certified rendered coverage.
void requireRegion(uint s) {
    bool first=E(s,25)!=commands[4] || E(s,26)!=commands[5];
    for(uint i=0u;i<6u;i++) {
        double value=packDouble2x32(uvec2(C(28u+2u*i),C(29u+2u*i)));
        if(!first) { double old=packDouble2x32(uvec2(E(s,48u+2u*i),E(s,49u+2u*i))); value=i<3u?min(old,value):max(old,value); }
        uvec2 bits=unpackDouble2x32(value); E(s,48u+2u*i)=bits.x; E(s,49u+2u*i)=bits.y;
    }
    E(s,25)=commands[4]; E(s,26)=commands[5];
}
/// Requires an entire currently demanded enclosure in the proposed replacement.
bool containsRequired(uint s) {
    if(E(s,25)!=commands[4] || E(s,26)!=commands[5]) return true;
    for(uint i=0u;i<6u;i++) {
        double required=packDouble2x32(uvec2(E(s,48u+2u*i),E(s,49u+2u*i)));
        double proposed=packDouble2x32(uvec2(C(28u+2u*i),C(29u+2u*i)));
        if(i<3u?proposed>required:proposed<required) return false;
    }
    return true;
}
/// Returns assignment and reason to GPU consumers; production does not read selections back.
void answer(uint reason,uint slot) {
    results[0]=reason; results[1]=slot;
    results[2]=slot<commands[0]?E(slot,21):0u; results[3]=slot<commands[0]?E(slot,22):0u;
}
/// Executes one bounded operation serially, preserving old certificates until replacement publication.
void main() {
    uint capacity=commands[0],resident=commands[1],limit=min(commands[2],capacity),target=commands[3],op=C(20);
    answer(1u,capacity);
    if(op>11u) return;
    // GPU completion producers cannot change CPU-owned allocation/freeze batch state.
    if(commands[13]!=0u && op!=2u) return;
    if(op==1u || op==2u) {
        double nearDepth=packDouble2x32(uvec2(C(86),C(87)));
        double farDepth=packDouble2x32(uvec2(C(88),C(89)));
        if(C(64)>1u || C(65)!=commands[10] || C(65)==0u || isnan(nearDepth)||isinf(nearDepth)||
            isnan(farDepth)||isinf(farDepth)||farDepth<=nearDepth ||
            (C(64)==1u && (nearDepth<=0.0lf || C(66)<C(65) || C(66)%C(65)!=0u))) return;
    }
    if(op==10u) {
        if(commands[12]!=0u) return;
        for(uint s=0u;s<capacity;s++)
            writable[s]=E(s,20)==1u && E(s,27)==1u
                ?uvec4(E(s,21),E(s,22),commands[4],commands[5]):uvec4(0);
        answer(0u,capacity); return;
    }
    if(commands[12]!=0u && (op==1u || op==9u)) { answer(8u,capacity); return; }
    if(op>=5u && op<=8u) {
        for(uint s=0u;s<capacity;s++) {
            if(E(s,20)==3u && leq(uvec2(E(s,23),E(s,24)),uvec2(commands[6],commands[7]))) E(s,20)=0u;
            bool remove=op==7u || (op==5u && keyEqual(s,12u)) ||
                (op==6u && E(s,0)==C(0) && E(s,1)==C(1) && E(s,2)==C(2)) || s>=target;
            if(remove && (E(s,20)==1u || E(s,20)==2u)) retire(s);
        }
        answer(0u,capacity); return;
    }
    uint hash=2166136261u;
    for(uint i=0u;i<17u;i++) hash=(hash^C(i))*16777619u;
    uint freeSlot=capacity,oldSlot=capacity,building=capacity;
    for(uint probe=0u;probe<limit;probe++) {
        uint s=(hash+probe)%capacity;
        if(E(s,20)==3u && leq(uvec2(E(s,23),E(s,24)),uvec2(commands[6],commands[7]))) E(s,20)=0u;
        if(E(s,20)==0u && s<min(resident,target) && freeSlot==capacity) freeSlot=s;
        if(!keyEqual(s,17u)) continue;
        if(E(s,20)==2u) oldSlot=s;
        if(E(s,20)==1u) building=s;
        if(op==4u && E(s,20)!=0u) retire(s);
    }
    if(op==4u) { answer(0u,capacity); return; }
    if(op==11u) {
        if(oldSlot==capacity) return;
        double when=packDouble2x32(uvec2(C(46),C(47)));
        double rendered=packDouble2x32(uvec2(E(oldSlot,46),E(oldSlot,47)));
        if(isnan(when)||isinf(when)||when<rendered) { answer(4u,capacity); return; }
        // Repeated scene edits or motion cannot extend the original invalidation deadline.
        double previous=packDouble2x32(uvec2(E(oldSlot,60),E(oldSlot,61)));
        if(E(oldSlot,110)==0u || when<previous) { E(oldSlot,60)=C(46); E(oldSlot,61)=C(47); E(oldSlot,110)=1u; }
        answer(0u,oldSlot); return;
    }
    if(op==9u) {
        // Abandon only the exact unfinished assignment; a current old certificate keeps its original coverage.
        if(building==capacity || E(building,21)!=C(21) || E(building,22)!=C(22)) { answer(4u,capacity); return; }
        retire(building); answer(0u,building); return;
    }
    if(op==0u || op==3u) {
        if(op==3u) { if(oldSlot<capacity) requireRegion(oldSlot); else if(building<capacity) requireRegion(building); }
        if(oldSlot<capacity && covers(oldSlot)) {
            answer(0u,oldSlot);
        } else if(building<capacity) answer(5u,building);
        else answer(oldSlot<capacity?2u:1u,capacity);
        return;
    }
    if(op==2u) {
        if(commands[12]==0u || building==capacity ||
            any(notEqual(writable[building],uvec4(C(21),C(22),commands[4],commands[5])))) { answer(4u,capacity); return; }
        if(building==capacity || C(27)!=1u || E(building,27)!=1u || E(building,21)!=C(21) || E(building,22)!=C(22)) { answer(4u,capacity); return; }
        // Exact pending enclosure/dependencies must accompany a complete draw certificate.
        for(uint i=0u;i<12u;i++) if(E(building,28u+i)!=C(28u+i)) { answer(4u,capacity); return; }
        if(!dependenciesEqual(building)) { answer(4u,capacity); return; }
        for(uint i=64u;i<98u;i++) if(E(building,i)!=C(i)) { answer(4u,capacity); return; }
        double timestamp=packDouble2x32(uvec2(C(46),C(47)));
        double started=packDouble2x32(uvec2(E(building,46),E(building,47)));
        if(isnan(timestamp)||isinf(timestamp)||timestamp<started) { answer(4u,capacity); return; }
        if(!containsRequired(building) || (oldSlot<capacity && !containsRequired(oldSlot))) { answer(6u,capacity); return; }
        E(building,20)=2u; E(building,27)=0u; E(building,25)=commands[4]; E(building,26)=commands[5];
        E(building,46)=C(46); E(building,47)=C(47); E(building,110)=0u;
        requireRegion(building);
        if(oldSlot<capacity) retire(oldSlot);
        answer(0u,building); return;
    }
    if(building<capacity) { answer(5u,building); return; }
    // A replacement cannot discard a currently required old region. An enclosure needs complete selection/drawing.
    if(oldSlot<capacity && !containsRequired(oldSlot)) { answer(6u,capacity); return; }
    if(freeSlot==capacity) {
        // Prefer invalid or unreferenced pages; withdrawal does not make their tiles immediately writable.
        for(uint probe=0u;probe<limit;probe++) {
            uint s=(hash+probe)%capacity;
            if(E(s,20)==2u && (E(s,25)!=commands[4] || E(s,26)!=commands[5]) && s!=oldSlot) retire(s);
        }
        answer(3u,capacity); return;
    }
    uvec2 generation=uvec2(results[4],results[5]);
    if(generation.x==0xffffffffu) { answer(7u,capacity); return; }
    generation.x++;
    results[4]=generation.x; results[5]=generation.y;
    for(uint i=0u;i<128u;i++) E(freeSlot,i)=C(i);
    // Physical addresses are explicit metadata, independent of the consumer's table lookup.
    E(freeSlot,17)=freeSlot/commands[8];
    uint tile=freeSlot%commands[8];
    E(freeSlot,18)=tile%commands[9]; E(freeSlot,19)=tile/commands[9];
    E(freeSlot,20)=1u; E(freeSlot,21)=generation.x; E(freeSlot,22)=generation.y;
    E(freeSlot,27)=0u;
    E(freeSlot,60)=0u; E(freeSlot,61)=0u; E(freeSlot,110)=0u;
    E(freeSlot,25)=commands[4]; E(freeSlot,26)=commands[5];
    for(uint i=0u;i<12u;i++) E(freeSlot,48u+i)=C(28u+i);
    answer(0u,freeSlot);
}
