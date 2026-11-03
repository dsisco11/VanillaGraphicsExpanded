// Invocation-private counters preserve repeated failures and overlapping region expectations.
uint invocationCounters[20];
shared uint groupCounters[1280];

/// Selects only the result facts exposed by this operation's typed result.
bool requiredCounter(int slot) {
#if OBSERVATION_FAMILY == 0
    return slot==1 || slot==2 || slot==6 || slot==19 || (mode==5 && slot==14);
#elif OBSERVATION_FAMILY == 4
    return slot==1 || slot==2 || slot==6 || slot==10 || slot==14;
#elif OBSERVATION_FAMILY == 3
    return slot==3 || slot==6;
#else
    return slot==1 || slot==3 || slot==4 || slot==5 || slot==6 || slot==7 ||
        slot==8 || slot==13 || slot==15 ||
        (OBSERVATION_FAMILY==1 && mode==22 && slot==16) ||
        (OBSERVATION_FAMILY==2 && (slot==2 || slot==9 || slot==10 || slot==11)) ||
        (OBSERVATION_FAMILY==1 && (mode==9 || mode==18 || mode==20) && slot==14);
#endif
}

/// Establishes additive, extrema and bit-union identities for inactive tail lanes as well.
void initializeCounters() {
    for(int slot=0;slot<20;slot++)
        invocationCounters[slot]=(slot==6 || slot==7 || slot==10)?0xffffffffu:0u;
}

/// Counts exact unsigned events locally before the associative workgroup reduction.
void addCounter(int slot,uint value) { invocationCounters[slot]+=value; }
/// Retains the earliest coordinate or ordered minimum without floating conversion.
void minCounter(int slot,uint value) { invocationCounters[slot]=min(invocationCounters[slot],value); }
/// Retains an ordered maximum without floating conversion.
void maxCounter(int slot,uint value) { invocationCounters[slot]=max(invocationCounters[slot],value); }
/// Retains every independently detected slice difference.
void orCounter(int slot,uint value) { invocationCounters[slot]|=value; }

/// Publishes at most one global atomic per requested fact per workgroup.
void publishCounters() {
    uint lane=gl_LocalInvocationID.x;
    for(int slot=0;slot<20;slot++)if(requiredCounter(slot))
        groupCounters[slot*64+int(lane)]=invocationCounters[slot];
    barrier();
    for(uint step=32u;step>0u;step>>=1u) {
        if(lane<step)for(int slot=0;slot<20;slot++)if(requiredCounter(slot)) {
            int address=slot*64+int(lane);
            uint other=groupCounters[address+int(step)];
            if(slot==6 || slot==7 || slot==10)groupCounters[address]=min(groupCounters[address],other);
            else if(slot==8 || slot==16)groupCounters[address]=max(groupCounters[address],other);
            else if(slot==9)groupCounters[address]|=other;
            else groupCounters[address]+=other;
        }
        barrier();
    }
    if(lane!=0u)return;
    for(int slot=0;slot<20;slot++)if(requiredCounter(slot)) {
        uint value=groupCounters[slot*64];
        if(slot==6 || slot==7 || slot==10) {
            if(value!=0xffffffffu)imageAtomicMin(resultImage,ivec2(slot,0),value);
        } else if(value!=0u) {
            if(slot==8 || slot==16)imageAtomicMax(resultImage,ivec2(slot,0),value);
            else if(slot==9)imageAtomicOr(resultImage,ivec2(slot,0),value);
            else imageAtomicAdd(resultImage,ivec2(slot,0),value);
        }
    }
}
