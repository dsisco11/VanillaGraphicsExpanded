#version 430 core
layout(local_size_x=4,local_size_y=4,local_size_z=4) in;
layout(std140) uniform LightingMaintenance
{
    ivec4 extentMode;
    ivec4 nodeShift;
    ivec4 regionExtentGeneration;
    ivec4 regionShift;
    vec4 scalesCap;
    ivec4 regionAlignment;
};
uniform sampler3D oldDirect;
uniform sampler3D oldDiffuse;
layout(rg16f) writeonly uniform image3D newDirect;
layout(rgba16f) writeonly uniform image3D newDiffuse;
layout(std430) readonly buffer OldRegions { uint oldRegions[]; };
layout(std430) writeonly buffer NewRegions { uint newRegions[]; };

/** Tests interior nodes only: guards never acquire retained occupancy. */
bool Interior(ivec3 p,ivec3 size) { return all(greaterThan(p,ivec3(0))) && all(lessThan(p,size-1)); }
/** Copies complete node records and absolute-key region state to disjoint destinations. */
void main()
{
    ivec3 p=ivec3(gl_GlobalInvocationID), size=extentMode.xyz;
    if(all(lessThan(p,size)))
    {
        vec4 diffuse=vec4(0), direct=vec4(1,1,0,0);
        ivec3 old=p+nodeShift.xyz;
        if(extentMode.w!=0 && Interior(p,size) && Interior(old,size))
        {
            direct=texelFetch(oldDirect,old,0);
            diffuse=texelFetch(oldDiffuse,old,0);
            diffuse.rgb=clamp(diffuse.rgb*scalesCap.x,vec3(0),vec3(scalesCap.z))/scalesCap.y;
        }
        if(extentMode.w!=2) imageStore(newDirect,p,direct);
        imageStore(newDiffuse,p,diffuse);
    }
    ivec3 rsize=regionExtentGeneration.xyz;
    if(extentMode.w!=2 && all(lessThan(p,rsize)))
    {
        ivec3 old=p+regionShift.xyz;
        int dst=((p.z*rsize.y+p.y)*rsize.x+p.x)*16;
        bool retained=extentMode.w!=0 && all(greaterThanEqual(old,ivec3(0))) && all(lessThan(old,rsize));
        int src=((old.z*rsize.y+old.y)*rsize.x+old.x)*16;
        for(int i=0;i<16;i++) newRegions[dst+i]=retained?oldRegions[src+i]:0u;
        newRegions[dst]=uint(regionExtentGeneration.w);
        // Region records may survive while a new slab adds nodes. Keep ages and mark demand.
        ivec3 lo=max(p*4-regionAlignment.xyz,ivec3(0));
        ivec3 hi=min((p+1)*4-regionAlignment.xyz,ivec3(nodeShift.w,32,nodeShift.w));
        ivec3 oldLo=lo+nodeShift.xyz, oldHi=hi+nodeShift.xyz;
        bool introduced=!retained || any(lessThan(oldLo,ivec3(0))) ||
            any(greaterThan(oldHi,ivec3(nodeShift.w,32,nodeShift.w)));
        if(introduced) newRegions[dst+14]=1u;
    }
}
