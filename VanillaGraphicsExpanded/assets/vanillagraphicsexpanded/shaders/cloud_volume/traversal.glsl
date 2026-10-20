#ifndef VGE_STORED_CLOUD_TRAVERSAL
#define VGE_STORED_CLOUD_TRAVERSAL
uniform sampler3D volumeDensity;
layout(std430) readonly buffer VolumeOccupancy { uint volumeOccupancy[]; };
/** Describes finite native bounds and circular texels without absolute world floats. */
struct CloudVolumeGrid
{
    ivec3 size;
    ivec3 blocks;
    ivec2 slot;
    vec3 first;
    vec3 cell;
    vec3 minimum;
    vec3 maximum;
};
/** Retains the current physical block and unwrapped next planes across all integration steps. */
struct CloudVolumeRay
{
    vec3 origin;
    vec3 velocity;
    vec3 plane;
    vec3 next;
    ivec3 block;
    ivec3 direction;
    float t;
    float end;
    float stepLength;
    int operations;
    int limit;
    bool failed;
    bool known;
    bool occupied;
};
/** Clips an axis without dividing by a zero direction component. */
bool CloudVolumeSlab(float origin,float direction,float lo,float hi,inout vec2 span)
{
    if(direction==0)return origin>=lo&&origin<=hi;
    vec2 t=vec2(lo-origin,hi-origin)/direction;
    span=vec2(max(span.x,min(t.x,t.y)),min(span.y,max(t.x,t.y)));
    return span.y>span.x;
}
/** Initializes one DDA from finite input and nominal voxel-relative spacing; a miss is a complete empty ray. */
CloudVolumeRay CloudVolumeBegin(CloudVolumeGrid grid,vec3 origin,vec3 direction,float distance,float fraction)
{
    CloudVolumeRay ray;
    ray.origin=(origin-grid.first)/grid.cell;ray.velocity=direction/grid.cell;
    ray.direction=ivec3(sign(direction));ray.block=ivec3(0);ray.plane=vec3(0);ray.next=vec3(3.402823e38);
    ray.t=0;ray.end=0;ray.operations=0;ray.limit=fraction==1.0?1024:2048;
    ray.failed=any(isnan(origin))||any(isinf(origin))||any(isnan(direction))||any(isinf(direction))||
        isnan(distance)||isinf(distance)||distance<0||abs(dot(direction,direction)-1.0)>.00001||
        (fraction!=.5&&fraction!=1.0);
    ray.known=false;ray.occupied=false;ray.stepLength=1;
    if(ray.failed||distance==0)return ray;
    vec2 span=vec2(0,distance);
    for(int a=0;a<3;a++)if(!CloudVolumeSlab(origin[a],direction[a],grid.minimum[a],grid.maximum[a],span))return ray;
    ray.t=span.x;ray.end=span.y;
    ray.stepLength=fraction/max(abs(ray.velocity.x),max(abs(ray.velocity.y),abs(ray.velocity.z)));
    vec3 start=ray.origin+ray.velocity*ray.t;
    ivec3 voxel=ivec3(floor(start));
    for(int a=0;a<3;a++)if(ray.direction[a]<0&&start[a]==floor(start[a]))voxel[a]--;
    ivec3 physical=ivec3(((voxel.x+grid.slot.x)%grid.size.x+grid.size.x)%grid.size.x,
        clamp(voxel.y,0,grid.size.y-1),((voxel.z+grid.slot.y)%grid.size.z+grid.size.z)%grid.size.z);
    ray.block=physical/ivec3(8,4,8);
    for(int a=0;a<3;a++)if(ray.direction[a]!=0)
    {
        int width=a==1?4:8;
        int boundary=ray.direction[a]>0?min((ray.block[a]+1)*width,grid.size[a]):ray.block[a]*width;
        ray.plane[a]=float(voxel[a]+boundary-physical[a]);
        ray.next[a]=(ray.plane[a]-ray.origin[a])/ray.velocity[a];
    }
    return ray;
}
/** Advances all tied axes, accounting for short physical edge blocks and circular seams. */
void CloudVolumeAdvance(CloudVolumeGrid grid,inout CloudVolumeRay ray)
{
    for(int a=0;a<3;a++)if(ray.direction[a]!=0&&ray.next[a]<=ray.t)
    {
        ray.block[a]=(ray.block[a]+ray.direction[a]+grid.blocks[a])%grid.blocks[a];
        int side=a==1?4:8;
        int width=min(side,grid.size[a]-ray.block[a]*side);
        ray.plane[a]+=float(ray.direction[a]*width);
        ray.next[a]=(ray.plane[a]-ray.origin[a])/ray.velocity[a];
        if(ray.next[a]<=ray.t)ray.failed=true;
    }
    ray.operations++;ray.known=false;
}
/** Returns an exact clipped interval, skipping only zero-certified blocks; cap breaches invalidate the whole ray. */
bool CloudVolumeNext(CloudVolumeGrid grid,inout CloudVolumeRay ray,out vec2 interval,out bool occupied)
{
    interval=vec2(ray.t);occupied=false;
    if(ray.failed||ray.t>=ray.end)return false;
    if(!ray.known)
    {
        int id=(ray.block.z*grid.blocks.y+ray.block.y)*grid.blocks.x+ray.block.x;
        ray.occupied=volumeOccupancy[id]!=0u;ray.known=true;
    }
    float boundary=min(ray.end,min(ray.next.x,min(ray.next.y,ray.next.z)));
    float finish=ray.occupied?min(ray.t+ray.stepLength,boundary):boundary;
    if(finish<=ray.t){ray.failed=true;return false;}
    interval=vec2(ray.t,finish);occupied=ray.occupied;ray.t=finish;ray.operations++;
    if(ray.t<ray.end&&ray.t==boundary)CloudVolumeAdvance(grid,ray);
    if(ray.operations>ray.limit){ray.failed=true;return false;}
    return !ray.failed;
}
/** Samples stored extinction with one hardware trilinear fetch. */
float CloudVolumeSample(CloudVolumeGrid grid,vec3 native)
{
    vec3 uv=((native-grid.first)/grid.cell+vec3(grid.slot.x,0,grid.slot.y))/vec3(grid.size);
    return textureLod(volumeDensity,uv,0).r;
}
/** Distinguishes a fully traversed ray from a failed or truncated prefix. */
bool CloudVolumeComplete(CloudVolumeRay ray){return !ray.failed&&ray.t>=ray.end;}
#endif
