/// Validates the entire dispatched domain or independently completes histogram validation.
void observeInvocation() {
    #if OBSERVATION_FAMILY == 3
    statistics();
    #else
    uint index=gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*gl_NumWorkGroups.x*gl_WorkGroupSize.x;
    uint total=uint(extent.x*extent.y*extent.z);

#if OBSERVATION_FAMILY == 2
    if(mode==4) {
        uint ranks=uint(rankSide*rankSide);
        if(index>=ranks*uint(rankSlices))return;
        if(index==0u)imageStore(resultImage,ivec2(12,0),uvec4(0x48495354u));
        addCounter(11,1u);
        if(imageLoad(histogram,ivec2(int(index%ranks),int(index/ranks))).r!=1u){
            addCounter(2,1u);
            // A missing rank has no source texel; report its rank/slice instead of inventing coordinates.
            minCounter(10,index);
        }
        return;
    }
#endif

#if OBSERVATION_FAMILY == 0
    // Sparse completion counts records, while failures retain original image coordinates.
    if(mode==16 || mode==28) {
        if(index>=constantValue)return;
        if(index==0u)imageStore(resultImage,ivec2(0,0),uvec4(0x47505541u));
        addCounter(1,1u);
        uint address=index*(mode==16?4u:6u),pixel=expectedWord(address);
        ivec3 point=ivec3(int(pixel)%extent.x,int(pixel)/extent.x%extent.y,int(pixel)/(extent.x*extent.y));
        uvec4 actual=fetchBits(point,false);
        uint channel=mode==16?0u:expectedWord(address+1u),count=mode==16?3u:1u;
        for(uint lane=0u;lane<count;lane++) {
            uint c=channel+lane;
            bool valid;
            if(mode==16)valid=equalBits(actual[c],expectedWord(address+1u+lane));
            else valid=finiteBits(actual[c]) && number(actual[c])>=packDouble2x32(uvec2(expectedWord(address+2u),expectedWord(address+3u)))
                && number(actual[c])<=packDouble2x32(uvec2(expectedWord(address+4u),expectedWord(address+5u)));
            if(!valid)fail(pixel*4u+c,2);
        }
        addCounter(19,count);
        return;
    }
#endif
    if(index>=total)return;
    if(index==0u)imageStore(resultImage,ivec2(0,0),uvec4(0x47505541u));
    addCounter(1,1u);
    if(index%uint(pixelStride)!=uint(pixelOffset))return;
    ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
    #if KIND == 0 && AUTHORED_PROGRAM
    if(mode==21 || mode==26) {
        // Retain the executed selector in otherwise unused result words for request provenance.
        if(index==0u) { imageStore(resultImage,ivec2(16,0),uvec4(uint(predicateChoice))); imageStore(resultImage,ivec2(17,0),uvec4(constantValue)); }
        vec4 actualValue;
#if POINT_SOURCE != 0
        if(mode==26)actualValue=vec4(expectedWord(index*3u),expectedWord(index*3u+1u),expectedWord(index*3u+2u),0);
        else
#endif
        actualValue=texelFetch(actualImage,p,0);
        bvec2 result=authored(p,index,actualValue,
            
#if PREDICATE_INPUTS & 2
        ((constantValue&2u)!=0u ? historyAt(min(p,historySize()-1),0) : texelFetch(actualImage,p,0)),
#else
        texelFetch(actualImage,p,0),
#endif
            
#if PREDICATE_INPUTS & 8
        ((constantValue&8u)!=0u ? texelFetch(additionalImage,min(p,textureSize(additionalImage,0)-1),0) : texelFetch(actualImage,p,0)),
#else
        texelFetch(actualImage,p,0),
#endif
            
#if PREDICATE_INPUTS & 16
        ((constantValue&16u)!=0u ? texelFetch(fourthImage,min(p,textureSize(fourthImage,0)-1),0) : texelFetch(actualImage,p,0)));
#else
        texelFetch(actualImage,p,0));
#endif
        if(!result.x)fail(index*4u,2);
        if(result.y){addCounter(14,1u);minCounter(10,index);}
        return;
    }
    #endif
    #if !AUTHORED_PROGRAM
    uvec4 value=fetchBits(p,false);
    #if OBSERVATION_FAMILY == 0
    if(mode==29) {
        uint compared=0u;
        for(uint region=0u;region<constantValue;region++) {
            uint address=region*12u;
            ivec3 lower=ivec3(expectedWord(address),expectedWord(address+1u),expectedWord(address+2u));
            ivec3 upper=ivec3(expectedWord(address+3u),expectedWord(address+4u),expectedWord(address+5u));
            if(any(lessThan(p,lower)) || any(greaterThanEqual(p,upper)))continue;
            uint mask=expectedWord(address+6u);
            for(uint c=0u;c<4u;c++)if((mask&(1u<<c))!=0u) {
                compared++;
                if(!equalBits(value[c],expectedWord(address+8u+c)))fail(index*4u+c,2);
            }
        }
        addCounter(19,compared);
        return;
    }
    uvec4 expected=(mode==2||mode==6||mode==7||mode==19)?fetchBits(p,true):(mode==1||mode==8||mode==10||mode==23)?expectedTuple(index):mode==24?texelFetch(expectedImage,p,0):mode==25?channelMinimum:uvec4(constantValue);
    #elif OBSERVATION_FAMILY == 1
    uvec4 expected=mode==20?fetchBits(p,true):uvec4(constantValue);
    #else
    uvec4 expected=uvec4(constantValue);
    #endif
    #if OBSERVATION_FAMILY == 0
    uvec4 doubleLow=uvec4(0),doubleHigh=uvec4(0),upperLow=uvec4(0),upperHigh=uvec4(0);
    if(mode==15 || mode==17)for(uint c=0u;c<4u;c++)if((channels&(1u<<c))!=0u) {
        uint address=expectedScalar(index,c)*(mode==15?2u:4u);
        doubleLow[c]=expectedWord(address);doubleHigh[c]=expectedWord(address+1u);
        if(mode==17){upperLow[c]=expectedWord(address+2u);upperHigh[c]=expectedWord(address+3u);}
    }
    #endif
    bool matching=constantValue!=0u;
    bool authoredValid=true;
#if OBSERVATION_FAMILY == 0
    uint compared=0u;
#endif
#if KIND == 0

#if OBSERVATION_FAMILY == 0
    if(mode==10) {
        // Preserve the authored float addition grouping; this is generic arithmetic, not a rendering oracle.
        precise vec4 pair=texelFetch(historyImage,ivec3(p.xy,1),0)+texelFetch(historyImage,ivec3(p.xy,2),0);
        precise vec4 base=texelFetch(historyImage,ivec3(p.xy,0),0)+pair;
        expected=floatBitsToUint(base+uintBitsToFloat(expected));
    }
#endif

    #if OBSERVATION_FAMILY == 0
    if(mode==13)expected=floatBitsToUint(uintBitsToFloat(value[referenceChannel])*multipliers);
    #endif
#endif
    for(uint c=0u;c<4u;c++) {
        if((channels&(1u<<c))==0u)continue;
#if OBSERVATION_FAMILY != 0
        addCounter(15,1u);
#endif
        uint scalar=index*4u+c,bits=value[c];
#if OBSERVATION_FAMILY == 0
        uint eligible=uint(mode!=8);
        if(mode==7)eligible&=uint(number(expected[c])!=0.0);
        if(mode==16)eligible&=uint((expected.a&(1u<<c))!=0u);
        compared+=eligible;
#endif
    #if KIND == 0

#if OBSERVATION_FAMILY == 1
        if(mode==18) {
            bool accepted=finiteBits(bits)&&number(bits)>=number(channelMinimum[c])&&number(bits)<=number(channelMaximum[c]);
            matching=constantValue!=0u?matching&&accepted:matching||accepted;
        }
#endif


#if OBSERVATION_FAMILY == 0
        if(mode==19) {
            precise float scaled=uintBitsToFloat(expected[c])*multipliers.x;
            precise float base=scaled/multipliers.w;
            precise float lower=base+float(bounds.x),upper=base+float(bounds.y);
            float n=uintBitsToFloat(bits);
            if(!(n>=lower&&n<=upper))fail(scalar,2);
        }
#endif

    #endif
#if OBSERVATION_FAMILY != 0
        if(!finiteBits(bits))fail(scalar,3);
        else {
            minCounter(7,ordered(bits));
            maxCounter(8,ordered(bits));
#if KIND == 0 && OBSERVATION_FAMILY == 1
            // Positive float encodings order by magnitude, so one associative maximum retains the absolute RGB peak.
            if(mode==22 && c<3u)maxCounter(16,bits&0x7fffffffu);
#endif
            double n=number(bits);
            if(n>0.0)addCounter(5,1u);
            if(n!=0.0)addCounter(13,1u);
            bool clearCoverage=mode==14 && number(value[referenceChannel])==0.0;
            if(clearCoverage ? n!=0.0 : n<bounds.x || n>bounds.y)fail(scalar,4);
        }
#endif
        #if OBSERVATION_FAMILY == 0
        if((mode==1||mode==2||mode==5||mode==10||mode==13||mode==25)&&!equalBits(bits,expected[c]))fail(scalar,2);
        if(mode==15&&(!finiteBits(bits)||abs(number(bits)-packDouble2x32(uvec2(doubleLow[c],doubleHigh[c])))>tolerance))fail(scalar,2);
        if(mode==17&&(!finiteBits(bits)||number(bits)<packDouble2x32(uvec2(doubleLow[c],doubleHigh[c]))||
            number(bits)>packDouble2x32(uvec2(upperLow[c],upperHigh[c]))))fail(scalar,2);
        if(mode==22) {
            uint address=expectedScalar(index,c)*2u;
            precise float difference=abs(uintBitsToFloat(bits)-uintBitsToFloat(expectedWord(address)));
            authoredValid=authoredValid && difference<=uintBitsToFloat(expectedWord(address+1u));
        }
        if(mode==23)authoredValid=authoredValid && bits==expected[c];
        if(mode==24 && roundEven(uintBitsToFloat(bits)*255.0)!=float(expected[c]))fail(scalar,2);
        if(mode==5&&equalBits(bits,expected[c]))addCounter(14,1u);
        if(mode==6&&bits!=expected[c])fail(scalar,2);
        if(mode==7&&number(expected[c])!=0.0&&!equalBits(bits,expected[c]))fail(scalar,2);
        #endif
        if(mode==9&&(bits&constantValue)!=0u)addCounter(14,1u);
    }
    #if OBSERVATION_FAMILY == 0
    addCounter(19,mode==8?3u:compared);
    if((mode==22 || mode==23) && !authoredValid)fail(index*4u,2);
    #endif
    #if OBSERVATION_FAMILY == 1
    if(mode==18&&matching)addCounter(14,1u);
    #endif
#if KIND == 0

#if OBSERVATION_FAMILY == 1
    if(mode==20) {
        precise float scaled=uintBitsToFloat(expected.r)*multipliers.x;
        precise float base=scaled/multipliers.w;
        precise float limit=base+multipliers.y;
        if(uintBitsToFloat(value.r)>limit&&texelFetch(additionalImage,p,0).r>multipliers.z)
            addCounter(14,1u);
    }
#endif

#endif

#if OBSERVATION_FAMILY == 0
    if(mode==8) {
        vec3 difference=uintBitsToFloat(value.xyz)-uintBitsToFloat(expected.xyz);
        if(!(length(difference)<=float(tolerance)))fail(index*4u,2);
    }
#endif


#if OBSERVATION_FAMILY == 2
    if(mode==3) {
        uint ranks=uint(rankSide*rankSide),slice=index/ranks;
        double n=number(value.r);
        if(!finiteBits(value.r)||n<0.0||n>=1.0)fail(index*4u,2);
        else {
            uint rank=uint(n*double(ranks));
            if(!equalBits(value.r,floatBitsToUint((float(rank)+0.5)/float(ranks))))fail(index*4u,2);
            imageAtomicAdd(histogram,ivec2(int(rank),int(slice)),1u);
        }
        if(slice>0u) {
            ivec3 first=ivec3(int(index%ranks)%rankSide,int(index%ranks)/rankSide,0);
            if(value.r!=fetchBits(first,false).r)orCounter(9,1u<<slice);
        }
    }
#endif

    #endif
    #endif
}
#if OBSERVATION_FAMILY == 3
shared double partialMoments[512];
/// Reads one finite selected component with explicit scalar accumulation.
double selectedMoment(uint bits,uint channel) {
    return (channels&(1u<<channel))!=0u&&finiteBits(bits)?number(bits):0.0;
}
/// Reads one independently stored double partial from its integer representation.
double loadMoment(uint group,int channel,int offset) {
    int address=int(group)*16+channel*2+offset;
    return packDouble2x32(uvec2(imageLoad(histogram,ivec2(address,0)).r,imageLoad(histogram,ivec2(address+1,0)).r));
}
/// Writes one explicit vector lane using explicitly indexed scalar moments.
void storeMoment(uint group,int channel,double sum,double square) {
    uvec2 s=unpackDouble2x32(sum),q=unpackDouble2x32(square);
    int address=mode==11?int(group)*16+channel*2:16+channel*2;
    if(mode==11) {
        imageStore(histogram,ivec2(address,0),uvec4(s.x));imageStore(histogram,ivec2(address+1,0),uvec4(s.y));
        imageStore(histogram,ivec2(address+8,0),uvec4(q.x));imageStore(histogram,ivec2(address+9,0),uvec4(q.y));
    } else {
        imageStore(resultImage,ivec2(address,0),uvec4(s.x));imageStore(resultImage,ivec2(address+1,0),uvec4(s.y));
        imageStore(resultImage,ivec2(address+8,0),uvec4(q.x));imageStore(resultImage,ivec2(address+9,0),uvec4(q.y));
    }
}
/// Reduces every selected component with deterministic double accumulation, returning only four sums and moments.
void statistics() {
    uint local=gl_LocalInvocationID.x,group=gl_WorkGroupID.x;
    // Retain exact FP64 accumulator representations across function-local dynamic array accesses.
    uvec2 momentBits[8];
    for(int c=0;c<8;c++)momentBits[c]=uvec2(0);
    if(mode==11) {
        uint total=uint(extent.x*extent.y*extent.z);
        for(uint index=gl_GlobalInvocationID.x;index<total;index+=gl_NumWorkGroups.x*64u) {
            ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
            uvec4 bits=fetchBits(p,false);
        #if AUTHORED_STATISTIC
            bits=statisticValue(p,bits);
            if(!finiteBits(bits.r))fail(index*4u,3);
        #endif
            for(int c=0;c<4;c++) {
                double value=selectedMoment(bits[c],uint(c));
            #if STATISTIC_EXPRESSION == 2
                value=abs(value);
            #elif KIND == 0 && STATISTIC_EXPRESSION == 3
                uvec4 weight=texelFetch(expectedImage,p,0);
                double first=value*packDouble2x32(weight.rg);
                value=first*packDouble2x32(weight.ba);
            #endif
                momentBits[c]=unpackDouble2x32(packDouble2x32(momentBits[c])+value);
            #if AUTHORED_STATISTIC
                precise float square=float(value)*float(value);momentBits[c+4]=unpackDouble2x32(packDouble2x32(momentBits[c+4])+double(square));
            #else
                momentBits[c+4]=unpackDouble2x32(packDouble2x32(momentBits[c+4])+value*value);
            #endif
            }
        }
    } else {
        for(uint index=local;index<256u;index+=64u)for(int c=0;c<4;c++) {
            momentBits[c]=unpackDouble2x32(packDouble2x32(momentBits[c])+loadMoment(index,c,0));
            momentBits[c+4]=unpackDouble2x32(packDouble2x32(momentBits[c+4])+loadMoment(index,c,8));
        }
    }
    for(int c=0;c<8;c++)partialMoments[c*64+int(local)]=packDouble2x32(momentBits[c]);
    barrier();
    for(uint step=32u;step>0u;step>>=1u) {
        if(local<step)for(int c=0;c<8;c++)
            partialMoments[c*64+int(local)]+=partialMoments[c*64+int(local+step)];
        barrier();
    }
    if(local!=0u)return;
    for(int c=0;c<4;c++)storeMoment(group,c,partialMoments[c*64],partialMoments[(c+4)*64]);
    if(mode==11)imageAtomicAdd(resultImage,ivec2(11,0),1u);
    else {imageStore(resultImage,ivec2(12,0),uvec4(0x53554d53u));imageAtomicAdd(resultImage,ivec2(14,0),1u);}
}

#endif

/// Keeps tail invocations in every collective without observing nonexistent source records.
void main() {
    initializeCounters();
    observeInvocation();
    publishCounters();
}
