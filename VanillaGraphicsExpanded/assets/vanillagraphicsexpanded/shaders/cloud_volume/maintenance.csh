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
layout(r16f) writeonly uniform image3D densityField;
layout(rg16f) writeonly uniform image3D directField;
layout(rgba16f) writeonly uniform image3D diffuseField;
layout(rgba16f) writeonly uniform image3D diffuseSpare;
layout(std430) buffer Regions { uint regions[]; };
layout(std430) buffer Occupancy { uint occupancy[]; };

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
    if(all(lessThan(p,extentMode.xyz)) && ResetCell(p))
    {
        imageStore(densityField,p,vec4(0)); imageStore(directField,p,vec4(1,1,0,0));
        imageStore(diffuseField,p,fallbackSource); imageStore(diffuseSpare,p,fallbackSource);
    }
    if(gl_LocalInvocationIndex==0u)
    {
        bool changed=extentMode.w==0;
        for(int z=0;z<8 && !changed;z++) for(int y=0;y<4 && !changed;y++) for(int x=0;x<8 && !changed;x++)
        {
            ivec3 q=origin+ivec3(x,y,z);
            if(all(lessThan(q,extentMode.xyz)) && ResetCell(q)) changed=true;
        }
        if(changed)
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
            occupancy[id]=extentMode.w==0?0u:2u;
        }
    }
}
