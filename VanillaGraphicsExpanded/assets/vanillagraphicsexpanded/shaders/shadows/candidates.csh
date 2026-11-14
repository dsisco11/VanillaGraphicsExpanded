#version 450 core

// One invocation owns an association's continuation. Independent associations can be scheduled
// without atomics; all reserved work is charged against the caller's aggregate batch allowance.
layout(local_size_x=1, local_size_y=1, local_size_z=1) in;
layout(std430, binding=0) readonly buffer CandidateInputs { uint inputs[]; };
layout(std430, binding=1) buffer CandidateHeaders { uint headers[]; };
layout(std430, binding=2) buffer CandidateCoordinates { ivec4 coordinates[]; };
layout(std430, binding=3) buffer CandidateDedup { uint dedup[]; };
layout(std430, binding=4) readonly buffer CandidateChanges { ivec4 changes[]; };
layout(std430, binding=5) readonly buffer CandidateWorld { uvec4 world[]; };

@import "../includes/shadows/certificates.glsl"

uint baseInput, baseHeader, baseOutput, baseHash;
uint slabsLeft, cellsLeft, probesLeft, changesLeft;
#define P(i) inputs[baseInput+uint(i)]
#define H(i) headers[baseHeader+uint(i)]

/// Reads an outward-rounded local FP32 bound.
float number(uint offset) { return uintBitsToFloat(P(offset)); }
/// Returns relative lower/upper XYZ endpoints, whose fourth components are unused.
vec3 vectorAt(uint offset) { return vec3(number(offset),number(offset+2u),number(offset+4u)); }
/// Includes an outward error allowance for every short arithmetic chain in the bounded local domain.
float errorBound(float value) {
    vec3 r=max(abs(vectorAt(40u)),abs(vectorAt(48u)));
    r=max(r,max(abs(vectorAt(56u)),abs(vectorAt(64u))));
    float scale=max(max(r.x,r.y),max(r.z,float(max(P(20),max(P(21),P(22))))));
    return (abs(value)+scale+1.0)*4.0e-6;
}
/// Converts a closed extent to touching cells, then clips before converting to signed integer indices.
int lowerCell(float value, int maximum) { return int(clamp(ceil(value-errorBound(value))-1.0,0.0,float(maximum)+1.0)); }
/// Includes the upper touching cell; a negative result denotes no intersection.
int upperCell(float value, int maximum) { return int(clamp(floor(value+errorBound(value)),-1.0,float(maximum))); }
/// Matches native lookup's wrapping unsigned FNV hash, retaining the dimension.
uint coordinateHash(ivec4 c) {
    uint h=2166136261u;
    h=(h^uint(c.w))*16777619u; h=(h^uint(c.x))*16777619u;
    h=(h^uint(c.y))*16777619u; return (h^uint(c.z))*16777619u;
}
/// Restricts a segment parameter by a linear inequality, rounding outwards rather than dropping ties.
bool restrictUpper(float coefficient, float rhs, inout float lo, inout float hi) {
    rhs += errorBound(rhs);
    // A near-parallel constraint cannot safely narrow the interval. It can still
    // reject a slab whose negative gap exceeds every possible coefficient error.
    float uncertainty=errorBound(coefficient);
    if(abs(coefficient)<=uncertainty) return rhs>=-abs(coefficient)-uncertainty;
    float u=rhs/coefficient;
    if(coefficient>0.0) hi=min(hi,u+errorBound(u));
    else lo=max(lo,u-errorBound(u));
    return lo<=hi;
}
/// Bounds a transverse directional interval by all four endpoint products.
vec2 swept(float lo, float hi, float h0, float h1, float s0, float s1) {
    vec4 p=vec4(h0*s0,h0*s1,h1*s0,h1*s1);
    return vec2(lo+min(min(p.x,p.y),min(p.z,p.w)),hi+max(max(p.x,p.y),max(p.z,p.w)));
}
/// Computes the full slab rectangle; no center ray or sampled-direction approximation is used.
bool rectangle(uint subset,int slab,out ivec4 range) {
    uint sector=72u+subset*16u;
    int q=int(P(sector)), a=(q+1)%3, b=(q+2)%3, sign=int(P(sector+1u));
    vec3 r0=vectorAt(40u),r1=vectorAt(48u);
    vec2 x,y;
    if(P(8)==0u) {
        float q0=sign>0 ? float(slab) : -float(slab)-1.0;
        float q1=q0+1.0;
        float low=sign>0 ? r0[q] : -r1[q], high=sign>0 ? r1[q] : -r0[q];
        float h0=max(0.0,q0-high-errorBound(q0-high));
        float h1=q1-low+errorBound(q1-low);
        if(h1<h0) return false;
        x=swept(r0[a],r1[a],h0,h1,number(sector+4u),number(sector+6u));
        y=swept(r0[b],r1[b],h0,h1,number(sector+8u),number(sector+10u));
    } else {
        vec3 s0=vectorAt(56u),s1=vectorAt(64u);
        float u0=0.0,u1=1.0;
        if(!restrictUpper(s0[q]-r0[q],float(slab)+1.0-r0[q],u0,u1)
            || !restrictUpper(r1[q]-s1[q],r1[q]-float(slab),u0,u1)) return false;
        vec3 low0=r0+(s0-r0)*u0,low1=r0+(s0-r0)*u1;
        vec3 high0=r1+(s1-r1)*u0,high1=r1+(s1-r1)*u1;
        x=vec2(min(low0[a],low1[a]),max(high0[a],high1[a]));
        y=vec2(min(low0[b],low1[b]),max(high0[b],high1[b]));
    }
    ivec3 end=ivec3(P(20),P(21),P(22));
    range=ivec4(lowerCell(x.x,end[a]),upperCell(x.y,end[a]),lowerCell(y.x,end[b]),upperCell(y.y,end[b]));
    return range.x<=range.y && range.z<=range.w;
}
/// Moves one exact transverse cursor only after its dedup operation has completed.
void nextCell() {
    H(10)=1u; H(11)=0u;
    if(int(H(4))<int(H(7))) H(4)++;
    else if(int(H(5))<int(H(9))) { H(4)=H(6); H(5)++; }
    else { H(10)=0u; H(3)++; }
}
/// Retains every coordinate, including empty and missing cells, and rejects bounded hash/output exhaustion.
bool emitCell(uint q) {
    ivec3 relative=ivec3(0); relative[int(q)]=int(H(3));
    relative[(int(q)+1)%3]=int(H(4)); relative[(int(q)+2)%3]=int(H(5));
    ivec4 c=ivec4(relative+ivec3(P(16),P(17),P(18)),int(P(15)));
    uint hash=coordinateHash(c);
    while(H(11)<P(3)) {
        if(probesLeft==0u) return false;
        probesLeft--; H(14)++;
        uint index=baseHash+((hash+H(11))&(P(2)-1u));
        uint stored=dedup[index];
        if(stored==0u) {
            if(H(1)>=P(1)) { H(0)=4u; return false; }
            coordinates[baseOutput+H(1)]=c;
            dedup[index]=H(1)+1u; H(1)++; nextCell(); return true;
        }
        if(all(equal(coordinates[baseOutput+stored-1u],c))) { H(15)++; nextCell(); return true; }
        H(11)++;
    }
    H(0)=4u; return false;
}
/// Revalidates current availability with bounded probing; cached connectivity never prunes geometry.
bool validateCell() {
    ivec4 c=coordinates[baseOutput+H(17)]; uint hash=coordinateHash(c);
    while(H(27)<P(13)) {
        if(probesLeft==0u) return false;
        probesLeft--; H(14)++;
        uint entry=((hash+H(27))&P(12))*4u;
        uvec4 info=world[entry+2u];
        bool found=all(equal(ivec4(world[entry]),c));
        if(info.z==0u || found) {
            // Only a proven-fresh empty or a drawable publication resolves this coordinate.
            if(info.z==0u || ((info.z&2u)==0u && (info.z&12u)!=12u)) H(16)++;
            H(17)++; H(27)=0u; return true;
        }
        H(27)++;
    }
    H(16)++; H(17)++; H(27)=0u; return true;
}
/// Tests a conservative active region for changed partitions; bounds-only membership itse remains stable.
bool affectsRegion(ivec4 c) {
    if(c.w!=int(P(15))) return false;
    vec3 lo=vec3(0.0),hi=vec3(P(20),P(21),P(22))+1.0;
    if(P(8)!=0u) { lo=min(vectorAt(40u),vectorAt(56u)); hi=max(vectorAt(48u),vectorAt(64u)); }
    vec3 cell=vec3(shadowCellDelta(c.x,int(P(16))),shadowCellDelta(c.y,int(P(17))),shadowCellDelta(c.z,int(P(18))));
    return all(lessThanEqual(cell,hi+vec3(errorBound(0.0)))) && all(greaterThanEqual(cell+1.0,lo-vec3(errorBound(0.0))));
}
/// Executes a bounded slice; completion follows both traversal and current dependency validation.
void main() {
    uint slot=inputs[0]; baseInput=4u+slot*256u; baseHeader=slot*32u;
    baseOutput=slot*P(1); baseHash=slot*P(2);
    slabsLeft=P(4);cellsLeft=P(5);probesLeft=P(6);changesLeft=P(7);
    H(26)=P(19);
    if(P(10)==0u || H(24)!=P(34) || H(25)!=P(35)) { H(0)=5u; return; }
    if(H(0)>=4u) return;
    bool newWorld=H(28)!=P(32) || H(29)!=P(33);
    bool newSequence=H(19)!=P(24) || H(20)!=P(25);
    if(H(29)>P(33) || (H(29)==P(33) && H(28)>P(32)) || H(20)>P(25)
        || (H(20)==P(25) && H(19)>P(24))) { H(0)=5u; return; }
    if(newSequence) {
        // The host also rejects incomplete/lost history. The GPU verifies the covered watermark itse.
        uvec2 old=uvec2(H(19),H(20)), first=uvec2(P(26),P(27));
        if(old.y<first.y || (old.y==first.y && old.x<first.x)) { H(0)=5u; return; }
        if(H(21)!=P(30) || H(22)!=P(31)) { H(18)=0u; H(21)=P(30);H(22)=P(31); }
        if(H(0)==3u) H(0)=2u;
        while(H(18)<P(14)) {
            if(changesLeft==0u) return;
            changesLeft--;H(30)++;
            if(affectsRegion(changes[H(18)])) { H(17)=0u; H(16)=0u; H(27)=0u; }
            H(18)++;
        }
        H(19)=P(24);H(20)=P(25);
    }
    if(newWorld) {
        H(17)=0u;H(16)=0u;H(27)=0u; H(28)=P(32);H(29)=P(33);
        if(H(0)==3u) H(0)=2u;
    }
    if(H(0)==0u) {
        // Hash clearing is resumable and charged to probe work, including slot reuse.
        while(H(23)<P(2)) { if(probesLeft==0u)return; probesLeft--;H(14)++;dedup[baseHash+H(23)++]=0u; }
        H(0)=1u;
    }
    while(H(0)==1u) {
        if(H(2)>=P(9)) { H(0)=2u;H(17)=0u;H(16)=0u;H(27)=0u;break; }
        uint q=P(72u+H(2)*16u); uint last=P(20u+q);
        if(H(3)>last) { H(2)++;H(3)=0u;H(10)=0u;continue; }
        if(H(10)==0u) {
            if(slabsLeft==0u)return; slabsLeft--;H(13)++;
            ivec4 range;
            if(!rectangle(H(2),int(H(3)),range)) { H(3)++;continue; }
            H(6)=uint(range.x);H(7)=uint(range.y);H(8)=uint(range.z);H(9)=uint(range.w);
            H(4)=H(6);H(5)=H(8);H(10)=1u;
        }
        if(H(10)==1u) {
            if(cellsLeft==0u)return;cellsLeft--;H(12)++;H(10)=2u;H(11)=0u;
        }
        if(!emitCell(q))return;
    }
    if(H(0)==2u) {
        while(H(17)<H(1)) {
            // A continued probe is still charged as a cell visit; conservative overcharging cannot exceed budgets.
            if(cellsLeft==0u)return;cellsLeft--;H(12)++;
            if(!validateCell())return;
        }
        H(0)=3u;
    }
    // Publish the validated dependency interval even when its sequence starts at
    // zero and therefore never enters the incremental-change traversal above.
    if(H(0)==3u) { H(21)=P(30); H(22)=P(31); }
}
