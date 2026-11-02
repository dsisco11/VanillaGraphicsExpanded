/// Evaluates the independently authored periodic equation residual, rather than the solver update.
uvec4 statisticValue(ivec3 p,uvec4 bits)
{
                // This is the independently authored equation A*h-b, never a Jacobi update or production residual executable.
                precise float neighbors=texelFetch(actualImage,ivec3((p.x+extent.x-1)%extent.x,p.y,0),0).r
                    +texelFetch(actualImage,ivec3((p.x+1)%extent.x,p.y,0),0).r;
                neighbors=neighbors+texelFetch(actualImage,ivec3(p.x,(p.y+extent.y-1)%extent.y,0),0).r;
                neighbors=neighbors+texelFetch(actualImage,ivec3(p.x,(p.y+1)%extent.y,0),0).r;
                precise float center=4.0*uintBitsToFloat(bits.r);
                precise float laplacian=neighbors-center;
                precise float residual=laplacian-uintBitsToFloat(texelFetch(expectedImage,p,0).r);
                return floatBitsToUint(vec4(residual,0,0,0));
            }
