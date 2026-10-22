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
/** Retains one plane set, switching between occupied cells and coarse empty blocks. */
struct CloudVolumeRay
{
    vec3 origin;
    vec3 velocity;
    vec3 inverseVelocity;
    vec3 plane;
    vec3 next;
    ivec3 block;
    ivec3 direction;
    ivec3 voxel;
    ivec3 sampleCell;
    float t;
    float end;
    float cellSpan;
    int operations;
    int limit;
    bool failed;
    bool finiteReciprocals;
    uint occupancyState; // Unknown fine (0), empty coarse (1), occupied fine (2), unknown coarse (3).
};
/** Clips an axis without dividing by a zero direction component. */
bool CloudVolumeSlab(float origin,float direction,float lo,float hi,inout vec2 span)
{
    if(direction==0)return origin>=lo&&origin<=hi;
    vec2 t=vec2(lo-origin,hi-origin)/direction;
    span=vec2(max(span.x,min(t.x,t.y)),min(span.y,max(t.x,t.y)));
    return span.y>span.x;
}
/** Evaluates an original-ray plane crossing, retaining division for overflowed reciprocals. */
float CloudVolumeCrossing(float plane,float origin,float velocity,float inverse)
{
    // Tiny nonzero components can overflow their reciprocal. Division retains exact zero-plane
    // crossings in that exceptional case instead of allowing zero times infinity to become NaN.
    return isinf(inverse)?(plane-origin)/velocity:(plane-origin)*inverse;
}
/** Initializes physical cells and crossing parameters under one consistent ray convention. */
void CloudVolumeEnterCells(CloudVolumeGrid grid,inout CloudVolumeRay ray)
{
    vec3 position=ray.origin+ray.velocity*ray.t;
    ivec3 logical=ivec3(floor(position));
    ray.next=vec3(3.402823e38);
    for(int a=0;a<3;a++)if(ray.direction[a]!=0)
    {
        if(ray.direction[a]<0&&position[a]==floor(position[a]))logical[a]--;
        // Compare original-ray crossing parameters too: position reconstruction may round onto
        // an adjacent integer plane even when that entry has not actually been crossed yet.
        float entry=float(logical[a]+(ray.direction[a]<0?1:0));
        if(CloudVolumeCrossing(entry,ray.origin[a],ray.velocity[a],ray.inverseVelocity[a])>ray.t)logical[a]-=ray.direction[a];
        ray.plane[a]=float(logical[a]+(ray.direction[a]>0?1:0));
        ray.next[a]=CloudVolumeCrossing(ray.plane[a],ray.origin[a],ray.velocity[a],ray.inverseVelocity[a]);
        if(ray.next[a]<=ray.t)
        {
            logical[a]+=ray.direction[a];ray.plane[a]+=float(ray.direction[a]);
            ray.next[a]=CloudVolumeCrossing(ray.plane[a],ray.origin[a],ray.velocity[a],ray.inverseVelocity[a]);
        }
        if(ray.next[a]<=ray.t)ray.failed=true;
    }
    ivec2 horizontal=ivec2((logical.x+grid.slot.x)%grid.size.x,(logical.z+grid.slot.y)%grid.size.z);
    if(horizontal.x<0)horizontal.x+=grid.size.x;
    if(horizontal.y<0)horizontal.y+=grid.size.z;
    ray.voxel=ivec3(horizontal.x,clamp(logical.y,0,grid.size.y-1),horizontal.y);
}
/** Initializes retained cell/block DDA; a finite miss is a complete empty ray. */
CloudVolumeRay CloudVolumeBegin(CloudVolumeGrid grid,vec3 origin,vec3 direction,float distance)
{
    CloudVolumeRay ray;
    ray.origin=(origin-grid.first)/grid.cell;ray.velocity=direction/grid.cell;
    ray.inverseVelocity=1.0/mix(ray.velocity,vec3(1),equal(ray.velocity,vec3(0)));
    ray.finiteReciprocals=!any(isinf(ray.inverseVelocity));
    ray.direction=ivec3(sign(ray.velocity));ray.block=ivec3(0);ray.plane=vec3(0);ray.next=vec3(3.402823e38);
    ray.t=0;ray.end=0;ray.operations=0;ray.limit=2048;
    ray.failed=any(isnan(origin))||any(isinf(origin))||any(isnan(direction))||any(isinf(direction))||
        isnan(distance)||isinf(distance)||distance<0||abs(dot(direction,direction)-1.0)>.00001;
    ray.occupancyState=0u;ray.cellSpan=1;ray.voxel=ivec3(0);ray.sampleCell=ivec3(0);
    if(ray.failed||distance==0)return ray;
    vec2 span=vec2(0,distance);
    for(int a=0;a<3;a++)if(!CloudVolumeSlab(origin[a],direction[a],grid.minimum[a],grid.maximum[a],span))return ray;
    ray.t=span.x;ray.end=span.y;
    ray.cellSpan=1/max(abs(ray.velocity.x),max(abs(ray.velocity.y),abs(ray.velocity.z)));
    CloudVolumeEnterCells(grid,ray);
    ray.block=ray.voxel/ivec3(8,4,8);
    return ray;
}
/** Advances all tied axes at the retained cell or block scale, including short seam blocks. */
void CloudVolumeAdvance(CloudVolumeGrid grid,inout CloudVolumeRay ray,bool occupied)
{
    bvec3 crossed=lessThanEqual(ray.next,vec3(ray.t));
    ivec3 move=ivec3(crossed)*ray.direction;
    if(!occupied)
    {
        ray.block+=move;
        ray.block+=ivec3(lessThan(ray.block,ivec3(0)))*grid.blocks;
        ray.block-=ivec3(greaterThanEqual(ray.block,grid.blocks))*grid.blocks;
        ivec3 side=ivec3(8,4,8);
        ray.plane+=vec3(move*min(side,grid.size-ray.block*side));
    }
    else
    {
        ray.voxel+=move;
        ray.voxel+=ivec3(lessThan(ray.voxel,ivec3(0)))*grid.size;
        ray.voxel-=ivec3(greaterThanEqual(ray.voxel,grid.size))*grid.size;
        ray.plane+=vec3(move);
    }
    // Crossings always derive from original planes; reciprocal multiplication is shared by all
    // cell advances. Entry correction and later crossings use this same parameter convention.
    vec3 next=(ray.plane-ray.origin)*ray.inverseVelocity;
    if(!ray.finiteReciprocals)
        for(int a=0;a<3;a++)if(isinf(ray.inverseVelocity[a]))
            next[a]=(ray.plane[a]-ray.origin[a])/ray.velocity[a];
    ray.next=mix(ray.next,next,crossed);
    if(any(lessThanEqual(mix(vec3(3.402823e38),next,crossed),vec3(ray.t))))ray.failed=true;
    if(!occupied)ray.occupancyState=3u;
    else
    {
        ivec3 block=ray.voxel/ivec3(8,4,8);
        if(any(notEqual(block,ray.block)))ray.occupancyState=0u;
        ray.block=block;
    }
}
/** Returns one exact occupied cell or a whole certified-empty block span, rejecting failed prefixes. */
bool CloudVolumeNext(CloudVolumeGrid grid,inout CloudVolumeRay ray,out vec2 interval,out bool occupied)
{
    interval=vec2(ray.t);occupied=false;
    if(ray.failed||ray.t>=ray.end)return false;
    bool wasCoarse=ray.occupancyState==3u;
    bool classify=ray.occupancyState==0u||wasCoarse;
    if(classify)
    {
        int id=(ray.block.z*grid.blocks.y+ray.block.y)*grid.blocks.x+ray.block.x;
        ray.occupancyState=(volumeOccupancy[id]&3u)==0u?1u:2u;
        if(wasCoarse&&ray.occupancyState==2u)
        {
            CloudVolumeEnterCells(grid,ray);
            if(ray.failed)return false;
        }
    }
    occupied=ray.occupancyState==2u;
    if(!occupied&&!wasCoarse&&classify)
    {
        // Switch this same plane set to coarse exits once. Consecutive empty blocks retain
        // cheap block advances, while occupied cells retain just the fine DDA state.
        for(int a=0;a<3;a++)if(ray.direction[a]!=0)
        {
            int side=a==1?4:8;
            int boundary=ray.direction[a]>0?min((ray.block[a]+1)*side,grid.size[a]):ray.block[a]*side;
            float logical=ray.plane[a]-(ray.direction[a]>0?1.0:0.0);
            float plane=logical+float(boundary-ray.voxel[a]);
            ray.plane[a]=plane;ray.next[a]=CloudVolumeCrossing(plane,ray.origin[a],ray.velocity[a],ray.inverseVelocity[a]);
        }
    }
    float finish=min(ray.end,min(ray.next.x,min(ray.next.y,ray.next.z)));
    if(finish<=ray.t){ray.failed=true;return false;}
    interval=vec2(ray.t,finish);ray.sampleCell=ray.voxel;ray.t=finish;ray.operations++;
    if(ray.t<ray.end)
    {
        CloudVolumeAdvance(grid,ray,occupied);
    }
    if(ray.operations>ray.limit){ray.failed=true;return false;}
    return !ray.failed;
}
/** Distinguishes a fully traversed ray from a failed or truncated prefix. */
bool CloudVolumeComplete(CloudVolumeRay ray){return !ray.failed&&ray.t>=ray.end;}
#endif
