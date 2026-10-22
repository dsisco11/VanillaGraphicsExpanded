#version 430 core
layout(local_size_x=8,local_size_y=4,local_size_z=8) in;
layout(std140) uniform VolumeMaintenance
{
    ivec4 extentMode;
    ivec4 firstSlot;
    ivec4 windowShift;
    ivec4 blockRange;
    vec4 fallbackSource;
};
layout(r16f) uniform image3D densityField;
layout(rg16f) writeonly uniform image3D directField;
layout(rgba16f) writeonly uniform image3D diffuseField;
layout(rgba16f) writeonly uniform image3D diffuseSpare;
layout(std430) buffer Regions { uint regions[]; };
layout(std430) buffer Occupancy { uint occupancy[]; };
layout(std430) buffer MaintenanceScratch {uint scratch[];};
shared uint origins,changed;


/** Recovers logical coordinates without assuming a physical block is contiguous across the seam. */
ivec3 Logical(ivec3 p) { return ivec3((p.x-firstSlot.x+extentMode.x)%extentMode.x,p.y,(p.z-firstSlot.z+extentMode.z)%extentMode.z); }
/** Guards and slots whose previous identity left the admitted window cannot retain density. */
bool ResetCell(ivec3 p)
{
    ivec3 q=Logical(p), old=q+windowShift.xyz;
    return extentMode.w==0 || any(lessThanEqual(q,ivec3(0))) || any(greaterThanEqual(q,extentMode.xyz-1)) ||
        any(lessThanEqual(old,ivec3(0))) || any(greaterThanEqual(old,extentMode.xyz-1));
}
/** Initializes bounded blocks, or clears entering/guard slots while preserving overlapping texels in place. */
void main()
{
    int id=blockRange.x+int(gl_WorkGroupID.x);
    if(int(gl_WorkGroupID.x)>=blockRange.y) return;
    ivec3 b=ivec3(id%blockRange.z,(id/blockRange.z)%blockRange.w,id/(blockRange.z*blockRange.w));
    ivec3 origin=b*ivec3(8,4,8), p=origin+ivec3(gl_LocalInvocationID);
    if(gl_LocalInvocationIndex==0u){origins=0u;changed=0u;}
    barrier();
    bool inRange=all(lessThan(p,extentMode.xyz));
    bool reset=inRange&&ResetCell(p);
    if(reset)
    {
        atomicOr(changed,1u);
        imageStore(densityField,p,vec4(0)); imageStore(directField,p,vec4(1,1,0,0));
        imageStore(diffuseField,p,fallbackSource); imageStore(diffuseSpare,p,fallbackSource);
    }
    barrier();
    // Only a block touched by maintenance needs recounting. Unchanged blocks keep their
    // certificate without reading density. Reset lanes never read uninitialized memory.
    if(changed!=0u&&inRange&&!reset&&imageLoad(densityField,p).r>0)
    atomicAdd(origins,1u);
    barrier();
    if(gl_LocalInvocationIndex==0u)
    {
        if(changed!=0u)
        {
            int record=id*16;
            if(extentMode.w==0)
            {
                for(int i=0;i<16;i++) regions[record+i]=0u;
                regions[record]=1u;
            }
            else
            {
                // Retained jobs keep their original demand and waiting age. Only whole-block
                // completion claims are revoked when new/guard texels alter the footprint.
                regions[record]|=7u;
                regions[record+2]=0u; regions[record+4]=0u; regions[record+6]=0u; regions[record+8]=0u;
                regions[record+10]=0u; regions[record+11]=0u; regions[record+13]=0u; regions[record+15]=0u;
            }
            // A scroll may leave neighboring filtered support; conservative occupied is safe until density repair.
            uint previousSupport=occupancy[id];
            if((previousSupport&4u)==0u||(((previousSupport>>3u)&511u)!=0u)!=(origins!=0u))atomicOr(scratch[48],1u);
            occupancy[id]=4u|(origins<<3u)|(extentMode.w==0?0u:2u);
        }
    }
}
