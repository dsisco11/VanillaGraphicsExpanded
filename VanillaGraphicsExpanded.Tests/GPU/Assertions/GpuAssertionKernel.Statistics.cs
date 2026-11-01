namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Reduces generic image moments through bounded parallel partials and one device-side final reduction.</summary>
internal static partial class GpuAssertionKernel
{
    #region Private
    private const string Statistics = """
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
            double moments[8];
            for(int c=0;c<8;c++)moments[c]=0.0;
            if(mode==11) {
                uint total=uint(extent.x*extent.y*extent.z);
                for(uint index=gl_GlobalInvocationID.x;index<total;index+=gl_NumWorkGroups.x*64u) {
                    ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
                    uvec4 bits=fetchBits(p,false);
                #if KIND == 0 && STATISTIC_EXPRESSION == 1
                    {
                        // This is the independently authored equation A*h-b, never a Jacobi update or production residual executable.
                        precise float neighbors=texelFetch(actualImage,ivec3((p.x+extent.x-1)%extent.x,p.y,0),0).r
                            +texelFetch(actualImage,ivec3((p.x+1)%extent.x,p.y,0),0).r;
                        neighbors=neighbors+texelFetch(actualImage,ivec3(p.x,(p.y+extent.y-1)%extent.y,0),0).r;
                        neighbors=neighbors+texelFetch(actualImage,ivec3(p.x,(p.y+1)%extent.y,0),0).r;
                        precise float center=4.0*uintBitsToFloat(bits.r);
                        precise float laplacian=neighbors-center;
                        precise float residual=laplacian-uintBitsToFloat(texelFetch(expectedImage,p,0).r);
                        bits=floatBitsToUint(vec4(residual,0,0,0));
                        if(!finiteBits(bits.r))fail(index*4u,3);
                    }
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
                        moments[c]+=value;
                    #if KIND == 0 && STATISTIC_EXPRESSION == 1
                        precise float square=float(value)*float(value);moments[c+4]+=double(square);
                    #else
                        moments[c+4]+=value*value;
                    #endif
                    }
                }
            } else {
                for(uint index=local;index<256u;index+=64u)for(int c=0;c<4;c++) {
                    moments[c]+=loadMoment(index,c,0);
                    moments[c+4]+=loadMoment(index,c,8);
                }
            }
            for(int c=0;c<8;c++)partialMoments[c*64+int(local)]=moments[c];
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
        """;
    #endregion
}
