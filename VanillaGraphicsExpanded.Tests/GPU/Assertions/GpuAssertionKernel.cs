namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Defines generic predicates over texels, without reproducing any renderer algorithm.</summary>
internal static partial class GpuAssertionKernel
{
    #region Internal API
    /// <summary>Specializes only the native sampler interpretation; expected values remain independent inputs.</summary>
    internal static string Source(int kind, int statisticExpression = 0, string predicate = "") => "#version 450 core\n#define KIND " + kind +
        "\n#define STATISTIC_EXPRESSION " + statisticExpression + "\n" + Body.Replace("/* AUTHORED_PREDICATE */", predicate.Length == 0 ? "return bvec2(true,false);" : predicate) + Statistics;
    #endregion

    #region Private
    private const string Body = """
        layout(local_size_x=64) in;
        #if KIND == 0
        layout(binding=0) uniform sampler2DArray actualImage;
        layout(binding=1) uniform sampler2DArray historyImage;
        layout(binding=3) uniform sampler2DArray additionalImage;
        layout(binding=4) uniform sampler2DArray fourthImage;
        #elif KIND == 1
        layout(binding=0) uniform isampler2DArray actualImage;
        layout(binding=1) uniform isampler2DArray historyImage;
        #else
        layout(binding=0) uniform usampler2DArray actualImage;
        layout(binding=1) uniform usampler2DArray historyImage;
        #endif
        layout(binding=2) uniform usampler2DArray expectedImage;
        layout(r32ui,binding=0) uniform uimage2D resultImage;
        layout(r32ui,binding=1) uniform uimage2D histogram;
        layout(location=0) uniform ivec3 extent;
        layout(location=1) uniform int mode;
        layout(location=2) uniform uint channels;
        layout(location=3) uniform double tolerance;
        layout(location=4) uniform dvec2 bounds;
        layout(location=5) uniform int rankSide;
        layout(location=6) uniform int rankSlices;
        layout(location=7) uniform uint constantValue;
        layout(location=8) uniform vec4 multipliers;
        layout(location=9) uniform int referenceChannel;
        layout(location=10) uniform int pixelStride;
        layout(location=11) uniform int pixelOffset;
        layout(location=13) uniform uvec4 channelMinimum;
        layout(location=14) uniform uvec4 channelMaximum;
        void statistics();

        /// Decode the IEEE value using integer fields and double arithmetic, including float subnormals.
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
            if(a==b)return true;
        #if KIND == 0
            // Numeric exact equality equates signed zero and NaNs, but never a subnormal with zero.
            if(((a|b)&0x7fffffffu)==0u)return true;
            if((a&0x7fffffffu)>0x7f800000u && (b&0x7fffffffu)>0x7f800000u)return true;
            if(!finiteBits(a)||!finiteBits(b))return false;
        #endif
            return tolerance>0.0 && abs(number(a)-number(b))<=tolerance;
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
        /// Reads original components without changing the represented numeric kind.
        uvec4 fetchBits(ivec3 p,bool history) {
        #if KIND == 0
            return floatBitsToUint(history?texelFetch(historyImage,p,0):texelFetch(actualImage,p,0));
        #else
            return uvec4(history?texelFetch(historyImage,p,0):texelFetch(actualImage,p,0));
        #endif
        }
        /// Accumulates a predicate failure and the earliest actual source component.
        void fail(uint index,int counter) {
            imageAtomicAdd(resultImage,ivec2(counter,0),1u);
            imageAtomicMin(resultImage,ivec2(6,0),index);
        }
        #if KIND == 0
        /// Preserves .NET numeric equality for selected lanes, including subnormal metadata, signed zero and NaN.
        bool same(vec4 a,vec4 b,uint mask) {
            uvec4 first=floatBitsToUint(a),second=floatBitsToUint(b);
            for(int channel=0;channel<4;channel++)if((mask&(1u<<channel))!=0u && !equalBits(first[channel],second[channel]))return false;
            return true;
        }
        /// Evaluates independently authored scenario expectations; its second result counts selected witnesses.
        bvec2 authored(ivec3 coordinate,uint index,vec4 a,vec4 b,vec4 c,vec4 d) {
            /* AUTHORED_PREDICATE */
        }
        #endif
        /// Validates the entire dispatched domain or independently completes histogram validation.
        void main() {
            if(mode==11||mode==12){statistics();return;}
            uint index=gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*gl_NumWorkGroups.x*gl_WorkGroupSize.x;
            uint total=uint(extent.x*extent.y*extent.z);
            if(mode==4) {
                uint ranks=uint(rankSide*rankSide);
                if(index>=ranks*uint(rankSlices))return;
                if(index==0u)imageStore(resultImage,ivec2(12,0),uvec4(0x48495354u));
                imageAtomicAdd(resultImage,ivec2(11,0),1u);
                if(imageLoad(histogram,ivec2(int(index%ranks),int(index/ranks))).r!=1u){
                    imageAtomicAdd(resultImage,ivec2(2,0),1u);
                    // A missing rank has no source texel; report its rank/slice instead of inventing coordinates.
                    imageAtomicMin(resultImage,ivec2(10,0),index);
                }
                return;
            }
            if(index>=total)return;
            if(index==0u)imageStore(resultImage,ivec2(0,0),uvec4(0x47505541u));
            imageAtomicAdd(resultImage,ivec2(1,0),1u);
            if(index%uint(pixelStride)!=uint(pixelOffset))return;
            ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
            #if KIND == 0
            if(mode==21) {
                bvec2 result=authored(p,index,texelFetch(actualImage,p,0),
                    texelFetch(historyImage,min(p,textureSize(historyImage,0)-1),0),
                    texelFetch(additionalImage,min(p,textureSize(additionalImage,0)-1),0),
                    texelFetch(fourthImage,min(p,textureSize(fourthImage,0)-1),0));
                if(!result.x)fail(index*4u,2);
                if(result.y){imageAtomicAdd(resultImage,ivec2(14,0),1u);imageAtomicMin(resultImage,ivec2(10,0),index);}
                return;
            }
            #endif
            uvec4 value=fetchBits(p,false);
            uvec4 expected=(mode==2||mode==6||mode==7||mode==19||mode==20)?fetchBits(p,true):(mode==1||mode==8||mode==10||mode==16)?texelFetch(expectedImage,p,0):uvec4(constantValue);
            uvec4 doubleLow=uvec4(0),doubleHigh=uvec4(0);
            if(mode==15){doubleLow=texelFetch(expectedImage,ivec3(p.xy,p.z*2),0);doubleHigh=texelFetch(expectedImage,ivec3(p.xy,p.z*2+1),0);}
            uvec4 upperLow=uvec4(0),upperHigh=uvec4(0);
            if(mode==17) {
                doubleLow=texelFetch(expectedImage,ivec3(p.xy,p.z*4),0);doubleHigh=texelFetch(expectedImage,ivec3(p.xy,p.z*4+1),0);
                upperLow=texelFetch(expectedImage,ivec3(p.xy,p.z*4+2),0);upperHigh=texelFetch(expectedImage,ivec3(p.xy,p.z*4+3),0);
            }
            bool matching=constantValue!=0u;
        #if KIND == 0
            if(mode==10) {
                // Preserve the authored float addition grouping; this is generic arithmetic, not a rendering oracle.
                precise vec4 pair=texelFetch(historyImage,ivec3(p.xy,1),0)+texelFetch(historyImage,ivec3(p.xy,2),0);
                precise vec4 base=texelFetch(historyImage,ivec3(p.xy,0),0)+pair;
                expected=floatBitsToUint(base+uintBitsToFloat(expected));
            }
            if(mode==13)expected=floatBitsToUint(uintBitsToFloat(value[referenceChannel])*multipliers);
        #endif
            for(uint c=0u;c<4u;c++) {
                if((channels&(1u<<c))==0u)continue;
                imageAtomicAdd(resultImage,ivec2(15,0),1u);
                uint scalar=index*4u+c,bits=value[c];
            #if KIND == 0
                if(mode==18) {
                    bool accepted=finiteBits(bits)&&number(bits)>=number(channelMinimum[c])&&number(bits)<=number(channelMaximum[c]);
                    matching=constantValue!=0u?matching&&accepted:matching||accepted;
                }
                if(mode==19) {
                    precise float scaled=uintBitsToFloat(expected[c])*multipliers.x;
                    precise float base=scaled/multipliers.w;
                    precise float lower=base+float(bounds.x),upper=base+float(bounds.y);
                    float n=uintBitsToFloat(bits);
                    if(!(n>=lower&&n<=upper))fail(scalar,2);
                }
            #endif
                if(!finiteBits(bits))fail(scalar,3);
                else {
                    imageAtomicMin(resultImage,ivec2(7,0),ordered(bits));
                    imageAtomicMax(resultImage,ivec2(8,0),ordered(bits));
                    double n=number(bits);
                    if(n>0.0)imageAtomicAdd(resultImage,ivec2(5,0),1u);
                    if(n!=0.0)imageAtomicAdd(resultImage,ivec2(13,0),1u);
                    bool clearCoverage=mode==14 && number(value[referenceChannel])==0.0;
                    if(clearCoverage ? n!=0.0 : n<bounds.x || n>bounds.y)fail(scalar,4);
                }
                if((mode==1||mode==2||mode==5||mode==10||mode==13)&&!equalBits(bits,expected[c]))fail(scalar,2);
                if(mode==15&&(!finiteBits(bits)||abs(number(bits)-packDouble2x32(uvec2(doubleLow[c],doubleHigh[c])))>tolerance))fail(scalar,2);
                if(mode==17&&(!finiteBits(bits)||number(bits)<packDouble2x32(uvec2(doubleLow[c],doubleHigh[c]))||
                    number(bits)>packDouble2x32(uvec2(upperLow[c],upperHigh[c]))))fail(scalar,2);
                if(mode==16&&(expected.a&(1u<<c))!=0u) {
                    imageAtomicAdd(resultImage,ivec2(14,0),1u);
                    if(!equalBits(bits,expected[c]))fail(scalar,2);
                }
                if(mode==5&&equalBits(bits,expected[c]))imageAtomicAdd(resultImage,ivec2(14,0),1u);
                if(mode==6&&bits!=expected[c])fail(scalar,2);
                if(mode==7&&number(expected[c])!=0.0&&!equalBits(bits,expected[c]))fail(scalar,2);
                if(mode==9&&(bits&constantValue)!=0u)imageAtomicAdd(resultImage,ivec2(14,0),1u);
            }
            if(mode==18&&matching)imageAtomicAdd(resultImage,ivec2(14,0),1u);
        #if KIND == 0
            if(mode==20) {
                precise float scaled=uintBitsToFloat(expected.r)*multipliers.x;
                precise float base=scaled/multipliers.w;
                precise float limit=base+multipliers.y;
                if(uintBitsToFloat(value.r)>limit&&texelFetch(additionalImage,p,0).r>multipliers.z)
                    imageAtomicAdd(resultImage,ivec2(14,0),1u);
            }
        #endif
            if(mode==8) {
                vec3 difference=uintBitsToFloat(value.xyz)-uintBitsToFloat(expected.xyz);
                if(!(length(difference)<=float(tolerance)))fail(index*4u,2);
            }
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
                    if(value.r!=fetchBits(first,false).r)imageAtomicOr(resultImage,ivec2(9,0),1u<<slice);
                }
            }
        }
        """;
    #endregion
}
