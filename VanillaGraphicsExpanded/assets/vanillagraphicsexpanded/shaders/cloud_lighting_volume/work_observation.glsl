uniform sampler2D workMap;
layout(rgba32f) uniform image2D workPrevious;
layout(rgba32f) uniform image2D workReference;
/** Uses exact positive modulo on bounded native indices. */
ivec2 Circular(ivec2 p) { return (p%workExtent.y+workExtent.y)%workExtent.y; }
/** Rejects malformed native data before converting to physical descriptor units. */
vec4 Descriptor(ivec2 p)
{
    vec4 data=texelFetch(workMap,p,0);
    vec4 result=vec4(data.r/50.0,50.0*data.b,50.0*data.a,1);
    if(any(isnan(data))||any(isinf(data))||any(isnan(result))||any(isinf(result))||data.r<0.0||data.a<=data.b) return vec4(0);
    return result;
}
/** Separates local incompatibility from compatible stale illumination, including accumulated small changes. */
bool HardChange(vec4 old,vec4 next)
{
    return (old.x>0.0)!=(next.x>0.0) || (old.w==0.0&&next.w!=0.0) ||
        abs(old.y-next.y)>25.0 || abs(old.z-next.z)>25.0 ||
        abs(old.x-next.x)>0.25*max(max(old.x,next.x),1e-6);
}
/** Emits local support and filter-neighbor demand directly into exact region records. */
void Invalidate(ivec2 cell,vec4 descriptor)
{
    if(descriptor.w==0.0 || descriptor.x<=0.0) return;
    vec3 lo=vec3(vec2(cell)*50.0,0).xzy; lo.y=descriptor.y;
    vec3 hi=lo+vec3(50,0,50);hi.y=descriptor.z;
    vec3 maximum=vec3(workExtent.x-2,32,workExtent.x-2);
    ivec3 first=ivec3(clamp(floor((lo-workFirstNode.xyz)/vec3(100,25,100))-1.0,vec3(1),maximum));
    ivec3 last=ivec3(clamp(ceil((hi-workFirstNode.xyz)/vec3(100,25,100))+1.0,vec3(1),maximum));
    ivec3 alignment=ivec3(workAlignment.x,0,workAlignment.y);
    first=(first-1+alignment)/4;last=(last-1+alignment)/4;
    // Include whole neighboring regions so every lighting interpolation footprint is invalidated.
    first=max(first-1,ivec3(0));last=min(last+1,ivec3(workExtent.z-1,7,workExtent.z-1));
    for(int z=first.z;z<=last.z;z++) for(int y=first.y;y<=last.y;y++) for(int x=first.x;x<=last.x;x++)
        atomicOr(regions[RegionIndex(ivec3(x,y,z))*16u+14u],1u);
}
/** One invocation owns one circular slot, reads both old records, then replaces only that slot. */
void main()
{
    uint index=gl_GlobalInvocationID.x;
    if(index>=uint(workExtent.y*workExtent.y)) return;
    ivec2 cell=ivec2(int(index)%workExtent.y,int(index)/workExtent.y);
    ivec2 slot=Circular(cell+workAlignment.zw);
    vec4 old=imageLoad(workPrevious,slot), reference=imageLoad(workReference,slot), next=Descriptor(cell);
    ivec2 oldCell=cell-workHistory.xy;
    bool retained=workHistory.z!=0 && all(greaterThanEqual(oldCell,ivec2(0)))&&all(lessThan(oldCell,ivec2(workExtent.y)));
    bool hard=!retained || HardChange(old,next)||HardChange(reference,next);
    bool changed=!retained || any(notEqual(old,next));
    if(!retained && workHistory.z!=0)
    {
        // The old key for this slot may be outside the new window. Derive its relative native index.
        ivec2 departed=workHistory.xy+Circular(cell-workHistory.xy);
        Invalidate(departed,old); Invalidate(departed,reference);
    }
    if(hard)
    {
        if(retained) {Invalidate(cell,old);Invalidate(cell,reference);}
        Invalidate(cell,next);
        imageStore(workReference,slot,next);
    }
    if(changed) atomicOr(scratch[3],1u);
    imageStore(workPrevious,slot,next);
}
