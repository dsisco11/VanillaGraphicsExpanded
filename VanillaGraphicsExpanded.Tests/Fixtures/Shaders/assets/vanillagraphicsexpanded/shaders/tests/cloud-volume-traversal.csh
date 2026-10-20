#version 430 core
@import "../cloud_volume/traversal.glsl"
layout(local_size_x=1) in;
layout(std140) uniform TraversalProbe
{
    ivec4 probeSize;
    ivec4 probeSlots;
    vec4 probeFirst;
    vec4 probeCell;
    vec4 probeMinimum;
    vec4 probeMaximum;
    vec4 probeOrigin;
    vec4 probeDirection;
};
layout(std430) buffer TraversalResult{vec4 result[];};
/** Integrates the declared midpoint schedule and reports coverage and failed-prefix rejection separately. */
void main()
{
    CloudVolumeGrid grid;grid.size=probeSize.xyz;grid.blocks=ivec3(probeSlots.z,probeSlots.w,probeSlots.z);
    grid.slot=probeSlots.xy;grid.first=probeFirst.xyz;grid.cell=probeCell.xyz;
    grid.minimum=probeMinimum.xyz;grid.maximum=probeMaximum.xyz;
    CloudVolumeRay ray=CloudVolumeBegin(grid,probeOrigin.xyz,probeDirection.xyz,probeOrigin.w,probeDirection.w);
    vec3 coordOrigin=(ray.origin+vec3(grid.slot.x,0,grid.slot.y))/vec3(grid.size);
    vec3 coordVelocity=ray.velocity/vec3(grid.size);
    if(probeSize.w>0)ray.limit=probeSize.w;
    float coverage=0,occupiedLength=0,tau=0,maxSpan=0;int emptyCount=0,samples=0;
    for(int i=0;i<2048;i++)
    {
        vec2 span;bool occupied;if(!CloudVolumeNext(grid,ray,span,occupied))break;
        float length=span.y-span.x;coverage+=length;
        if(occupied)
        {maxSpan=max(maxSpan,length);occupiedLength+=length;samples++;
            tau+=textureLod(volumeDensity,coordOrigin+coordVelocity*((span.x+span.y)*.5),0).r*length;}
        else emptyCount++;
    }
    result[0]=vec4(coverage,occupiedLength,tau,float(ray.operations));
    result[1]=vec4(CloudVolumeComplete(ray)?1:0,ray.failed?1:0,float(emptyCount),float(samples));
    result[2]=vec4(maxSpan,ray.stepLength,0,0);
}
