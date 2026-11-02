layout(std140,binding=28) uniform AssertionInputs { ivec4 domain; uvec4 selectors; dvec4 limits; vec4 multipliers; ivec4 controls; uvec4 channelMinimum; uvec4 channelMaximum; vec4 evaluation; };
#define extent domain.xyz
#define mode domain.w
#define channels selectors.x
#define constantValue selectors.y
#define rankSide int(selectors.z)
#define rankSlices int(selectors.w)
#define tolerance limits.x
#define bounds limits.yz
#define referenceChannel controls.x
#define pixelStride controls.y
#define pixelOffset controls.z
#define predicateChoice controls.w
#define parameters multipliers
layout(local_size_x=64) in;
#if KIND == 0
layout(binding=0) uniform sampler2DArray actualImage;



#elif KIND == 1
layout(binding=0) uniform isampler2DArray actualImage;

#else
layout(binding=0) uniform usampler2DArray actualImage;

#endif
#if OBSERVATION_FAMILY == 0 || OBSERVATION_FAMILY == 1 || (AUTHORED_PROGRAM && (PREDICATE_INPUTS & 2))
#if KIND == 0
#if POINT_SOURCE == 2
layout(binding=1) uniform sampler2D historyImage;
#elif POINT_SOURCE == 3
layout(binding=1) uniform sampler3D historyImage;
#else
layout(binding=1) uniform sampler2DArray historyImage;
#endif
#elif KIND == 1
layout(binding=1) uniform isampler2DArray historyImage;
#else
layout(binding=1) uniform usampler2DArray historyImage;
#endif
#endif
#if OBSERVATION_FAMILY == 1 || (AUTHORED_PROGRAM && (PREDICATE_INPUTS & 8))
layout(binding=3) uniform sampler2DArray additionalImage;
#endif
#if AUTHORED_PROGRAM && (PREDICATE_INPUTS & 16)
layout(binding=4) uniform sampler2DArray fourthImage;
#endif
#if OBSERVATION_FAMILY == 0 || (OBSERVATION_FAMILY == 3 && (STATISTIC_EXPRESSION == 3 || AUTHORED_STATISTIC)) || (AUTHORED_PROGRAM && (PREDICATE_INPUTS & 4))
layout(binding=2) uniform usampler2DArray expectedImage;
#endif
layout(r32ui,binding=0) uniform uimage2D resultImage;
#if OBSERVATION_FAMILY == 2 || OBSERVATION_FAMILY == 3
layout(r32ui,binding=1) uniform uimage2D histogram;
#endif
#if OBSERVATION_FAMILY == 3
void statistics();
#endif

/// Decodes finite IEEE float values exactly in double precision, including subnormals.
double number(uint bits) {
#if KIND == 1
    return double(int(bits));
#elif KIND == 2
    return double(bits);
#else
    uint exponent=(bits>>23)&255u, fraction=bits&0x7fffffu;
    double value=exponent==0u ? ldexp(double(fraction),-149)
        : ldexp(double(fraction|0x800000u),int(exponent)-150);
    return (bits&0x80000000u)!=0u ? -value : value;
#endif
}
/// Classifies nonfinite IEEE values without arithmetic or integer coercion.
bool finiteBits(uint bits) {
#if KIND == 0
    return (bits&0x7f800000u)!=0x7f800000u;
#else
    return true;
#endif
}
/// Compares represented values with explicit nonfinite, signed-zero and tolerance semantics.
bool equalBits(uint a,uint b) {
    // Integer masks preserve explicit Boolean combination without nested early-return merges.
    uint exact=uint(a==b);
    uint numeric=uint(tolerance>0.0)&uint(abs(number(a)-number(b))<=tolerance);
#if KIND == 0
    exact|=uint(((a|b)&0x7fffffffu)==0u);
    exact|=uint((a&0x7fffffffu)>0x7f800000u)&uint((b&0x7fffffffu)>0x7f800000u);
    numeric&=uint(finiteBits(a))&uint(finiteBits(b));
#endif
    return (exact|numeric)!=0u;
}
/// Orders finite bit patterns for atomic extrema while preserving integer precision.
uint ordered(uint bits) {
#if KIND == 0
    return (bits&0x80000000u)!=0u ? ~bits : bits^0x80000000u;
#elif KIND == 1
    return bits^0x80000000u;
#else
    return bits;
#endif
}
#if KIND == 0 && (OBSERVATION_FAMILY == 0 || OBSERVATION_FAMILY == 1 || (AUTHORED_PROGRAM && (PREDICATE_INPUTS & 2)))
/// Samples the explicitly declared history dimension without a source copy or selector mutation.
vec4 historyAt(ivec3 p,int level) {
#if POINT_SOURCE == 2
    return texelFetch(historyImage,p.xy,level);
#else
    return texelFetch(historyImage,p,level);
#endif
}
/// Preserves the original history extent for corresponding-domain clamping.
ivec3 historySize() {
#if POINT_SOURCE == 2
    return ivec3(textureSize(historyImage,0),1);
#else
    return textureSize(historyImage,0);
#endif
}
#endif
/// Reads original components without changing the represented numeric kind.
uvec4 fetchBits(ivec3 p,bool history) {
#if OBSERVATION_FAMILY == 2 || OBSERVATION_FAMILY == 3
#if KIND == 0
    return floatBitsToUint(texelFetch(actualImage,p,0));
#else
    return uvec4(texelFetch(actualImage,p,0));
#endif
#elif KIND == 0
    return floatBitsToUint(history?historyAt(p,0):texelFetch(actualImage,p,0));
#else
    return uvec4(history?texelFetch(historyImage,p,0):texelFetch(actualImage,p,0));
#endif
}
@import "counter-reduction.glsl"
/// Accumulates one invocation's failure and earliest original source component.
void fail(uint index,int counter) {
    addCounter(counter,1u);
    minCounter(6,index);
}

#if OBSERVATION_FAMILY == 0 || (AUTHORED_PROGRAM && POINT_SOURCE != 0)
/// Addresses tightly packed independently authored scalar records.
uint expectedWord(uint index) {
    int width=textureSize(expectedImage,0).x;
    return texelFetch(expectedImage,ivec3(int(index)%width,int(index)/width,0),0).r;
}
/// Maps an original channel to the tightly packed selected-channel scalar record.
uint expectedScalar(uint index,uint channel) {
    return index*uint(referenceChannel)+uint(bitCount(channels&((1u<<channel)-1u)));
}
/// Reads represented channels without padding each scalar image to RGBA.
uvec4 expectedTuple(uint index) {
    uvec4 result=uvec4(0);
    for(uint c=0u;c<4u;c++)if(((mode==8?7u:channels)&(1u<<c))!=0u)result[c]=expectedWord(mode==8?index*3u+c:expectedScalar(index,c));
    return result;
}
#endif
