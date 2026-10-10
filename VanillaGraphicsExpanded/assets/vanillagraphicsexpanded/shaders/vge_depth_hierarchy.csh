#version 430 core
layout(local_size_x=256) in;
uniform sampler2D primaryDepth;
layout(std140) uniform VgeDepthHierarchyComputeUBO { ivec4 extentLevels; };
layout(std430) coherent buffer DepthHierarchyTail { uint completed; float tail[]; };
layout(r32f) coherent uniform image2D mip0;
layout(r32f) coherent uniform image2D mip1;
layout(r32f) coherent uniform image2D mip2;
layout(r32f) coherent uniform image2D mip3;
layout(r32f) coherent uniform image2D mip4;
layout(r32f) coherent uniform image2D mip5;
layout(r32f) coherent uniform image2D mip6;
shared float evenDepth[4096];
shared float oddDepth[1024];
shared uint lastGroup;
/** Returns the floor-sized OpenGL mip extent, including one-texel axes. */
ivec2 sizeAt(int level) { return max(extentLevels.xy >> level, ivec2(1)); }
/** Maps a destination boundary to the first overlapping source texel without division on even axes. */
ivec2 sourceFirst(ivec2 p, ivec2 source, ivec2 dest) {
    ivec2 result=p*2;
    if(source.x!=dest.x*2) result.x=p.x*source.x/dest.x;
    if(source.y!=dest.y*2) result.y=p.y*source.y/dest.y;
    return result;
}
/** Maps an exclusive destination boundary to the conservative source end. */
ivec2 sourceEnd(ivec2 p, ivec2 source, ivec2 dest) {
    ivec2 result=p*2;
    if(source.x!=dest.x*2) result.x=(p.x*source.x+dest.x-1)/dest.x;
    if(source.y!=dest.y*2) result.y=(p.y*source.y+dest.y-1)/dest.y;
    return result;
}
/** Writes a uniquely owned texel to an explicitly bound mip image. */
void storeMip(int level, ivec2 p, float depth) {
    if(level==0) imageStore(mip0,p,vec4(depth));
    else if(level==1) imageStore(mip1,p,vec4(depth));
    else if(level==2) imageStore(mip2,p,vec4(depth));
    else if(level==3) imageStore(mip3,p,vec4(depth));
    else if(level==4) imageStore(mip4,p,vec4(depth));
    else if(level==5) imageStore(mip5,p,vec4(depth));
    else if(level==6) imageStore(mip6,p,vec4(depth));
}
/** Loads the completed tile boundary used by the elected coarse-tail workgroup. */
float loadBoundary(ivec2 p) { return imageLoad(mip6,p).r; }
/** Builds conservative local mips, then elects one finishing group without spinning. */
void main() {
    int lane=int(gl_LocalInvocationIndex);
    int boundary=min(6,extentLevels.z-1);
    ivec2 grid=sizeAt(boundary), group=ivec2(gl_WorkGroupID.xy);
    ivec2 first[7], end[7];
    first[boundary]=group; end[boundary]=group+1;
    // Backtrack exact proportional footprints; odd edges need overlapping halos.
    for(int level=boundary-1;level>=0;--level) {
        ivec2 source=sizeAt(level), dest=sizeAt(level+1);
        first[level]=sourceFirst(first[level+1],source,dest);
        end[level]=sourceEnd(end[level+1],source,dest);
    }
    // Mip zero is copied by disjoint ownership, independent of each tile's halo.
    ivec2 ownedFirst=(group*sizeAt(0)+grid-1)/grid;
    ivec2 ownedEnd=((group+1)*sizeAt(0)+grid-1)/grid;
    ivec2 ownedSize=ownedEnd-ownedFirst;
    for(int i=lane;i<ownedSize.x*ownedSize.y;i+=256) {
        ivec2 p=ownedFirst+ivec2(i%ownedSize.x,i/ownedSize.x);
        storeMip(0,p,texelFetch(primaryDepth,p,0).r);
    }
    for(int level=1;level<=boundary;++level) {
        ivec2 region=end[level]-first[level], source=sizeAt(level-1), dest=sizeAt(level);
        ivec2 previousRegion=end[level-1]-first[level-1];
        ivec2 writeFirst=(group*dest+grid-1)/grid;
        ivec2 writeEnd=((group+1)*dest+grid-1)/grid;
        for(int i=lane;i<region.x*region.y;i+=256) {
            ivec2 p=first[level]+ivec2(i%region.x,i/region.x);
            ivec2 a=sourceFirst(p,source,dest), b=sourceEnd(p+1,source,dest);
            float nearest=1.0;
            for(int y=a.y;y<b.y;++y) for(int x=a.x;x<b.x;++x) {
                ivec2 q=ivec2(x,y);
                int index=(q.y-first[level-1].y)*previousRegion.x+q.x-first[level-1].x;
                float depth=level==1?texelFetch(primaryDepth,q,0).r:
                    ((level&1)==0?evenDepth[index]:oddDepth[index]);
                nearest=min(nearest,depth);
            }
            if((level&1)==1) evenDepth[i]=nearest; else oddDepth[i]=nearest;
            // Halo computations overlap; image writes have exactly one owner.
            if(all(greaterThanEqual(p,writeFirst)) && all(lessThan(p,writeEnd))) storeMip(level,p,nearest);
        }
        barrier();
    }
    // Every invocation publishes its writes before lane zero advertises completion.
    memoryBarrierImage();
    barrier();
    if(lane==0) lastGroup=atomicAdd(completed,1u)==uint(grid.x*grid.y-1)?1u:0u;
    barrier();
    if(lastGroup==0u) return;
    memoryBarrierImage();
    memoryBarrierBuffer();
    // Only this group reaches the tail: ordinary workgroup barriers are sufficient.
    int sourceOffset=0, destOffset=0;
    for(int level=7;level<extentLevels.z;++level) {
        ivec2 source=sizeAt(level-1), dest=sizeAt(level);
        for(int i=lane;i<dest.x*dest.y;i+=256) {
            ivec2 p=ivec2(i%dest.x,i/dest.x);
            ivec2 a=sourceFirst(p,source,dest), b=sourceEnd(p+1,source,dest);
            float nearest=1.0;
            for(int y=a.y;y<b.y;++y) for(int x=a.x;x<b.x;++x)
                nearest=min(nearest,level==7?loadBoundary(ivec2(x,y)):tail[sourceOffset+y*source.x+x]);
            tail[destOffset+i]=nearest;
        }
        memoryBarrierBuffer();
        barrier();
        sourceOffset=destOffset;
        destOffset+=dest.x*dest.y;
    }
    // The host's storage barrier makes this reset visible to the next frame.
    if(lane==0) atomicExchange(completed,0u);
}
