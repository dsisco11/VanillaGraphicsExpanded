#version 450 core
layout(local_size_x=8, local_size_y=8, local_size_z=1) in;
layout(std430,binding=0) readonly buffer ClearPolicy { uint policy[]; };
layout(std430,binding=1) buffer PageEntries { uint entries[]; };
layout(std430,binding=2) readonly buffer PageResults { uint results[]; };
layout(r32ui,binding=0) uniform writeonly uimage2D clearBank0;
layout(r32ui,binding=1) uniform writeonly uimage2D clearBank1;
layout(r32ui,binding=2) uniform writeonly uimage2D clearBank2;
layout(r32ui,binding=3) uniform writeonly uimage2D clearBank3;
#define E(s,i) entries[(s)*128u+uint(i)]
/// Initializes only an exact unfinished assignment and separately publishes write authorization.
void main() {
    uint slot=results[1],size=policy[10];
    if(results[0]!=0u || slot>=policy[0] || size==0u || E(slot,20)!=1u ||
        E(slot,21)!=results[2] || E(slot,22)!=results[3] || E(slot,27)!=0u) return;
    uint bank=E(slot,17);
    if(policy[8]==0u || bank>=policy[1]/policy[8] || bank>=4u) return;
    ivec2 dimensions=bank==0u?imageSize(clearBank0):bank==1u?imageSize(clearBank1):
        bank==2u?imageSize(clearBank2):imageSize(clearBank3);
    uvec2 tile=uvec2(E(slot,18),E(slot,19));
    if(any(lessThanEqual(dimensions,ivec2(0))) ||
        any(greaterThanEqual(tile,uvec2(dimensions)/size))) return;
    uvec2 base=tile*size;
    if(any(greaterThanEqual(base,uvec2(dimensions))) || any(greaterThan(uvec2(size),uvec2(dimensions)-base))) return;
    if(policy[11]!=0u) {
        // A separate dispatch follows the complete clear and its image/storage barrier.
        if(all(equal(gl_GlobalInvocationID,uvec3(0)))) E(slot,27)=1u;
        return;
    }
    if(any(greaterThanEqual(gl_GlobalInvocationID.xy,uvec2(size)))) return;
    ivec2 at=ivec2(base+gl_GlobalInvocationID.xy);
    uvec4 empty=uvec4(0x3f800000u);
    if(bank==0u) imageStore(clearBank0,at,empty);
    else if(bank==1u) imageStore(clearBank1,at,empty);
    else if(bank==2u) imageStore(clearBank2,at,empty);
    else imageStore(clearBank3,at,empty);
}
