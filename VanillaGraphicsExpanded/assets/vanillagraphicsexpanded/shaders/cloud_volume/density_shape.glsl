#ifndef VGE_CLOUD_DENSITY_SHAPE
#define VGE_CLOUD_DENSITY_SHAPE
/** Reads only the finite candidate-owned snapshot, never clamping a native descriptor across an edge. */
vec4 Column(ivec2 cell)
{
    if(any(lessThan(cell,ivec2(0)))||any(greaterThanEqual(cell,ivec2(volumeExtent.z)))) return vec4(0);
    ivec2 slot=(cell+volumeWindow.zw)%volumeExtent.z;
    return imageLoad(descriptors,slot);
}
#if VGE_VOLUME_DENSITY_DETAIL
/** Converts an integer lattice corner to a bounded scalar without transcendental hashing. */
float Corner(ivec3 key)
{
    uvec3 p=uvec3((key%128+128)%128);
    uint h=p.x*0x9e3779b9u^p.y*0x85ebca6bu^p.z*0xc2b2ae35u;
    h^=h>>16u;h*=0x7feb352du;h^=h>>15u;h*=0x846ca68bu;h^=h>>16u;
    return float(h>>8u)*(1.0/16777216.0);
}
/** Uses one smooth 100-unit lattice with an exact 12800-unit material period. */
float Noise(vec3 material)
{
    vec3 p=material/100.0,f=fract(p);ivec3 b=ivec3(floor(p));f=f*f*(3.0-2.0*f);
    return mix(mix(mix(Corner(b),Corner(b+ivec3(1,0,0)),f.x),
        mix(Corner(b+ivec3(0,1,0)),Corner(b+ivec3(1,1,0)),f.x),f.y),
        mix(mix(Corner(b+ivec3(0,0,1)),Corner(b+ivec3(1,0,1)),f.x),
        mix(Corner(b+ivec3(0,1,1)),Corner(b+ivec3(1,1,1)),f.x),f.y),f.z);
}
/** Connected faces remain un-eroded only where their neighboring support covers this sample height. */
bool Covered(ivec2 cell,float y)
{
    vec4 c=Column(cell);return c.w>0 && c.x>0 && y>=c.y && y<=c.z;
}
/** Evaluates bounded shape detail exclusively during generation quadrature. */
float Shape(vec3 p,ivec2 cell,vec4 column)
{
    vec2 f=p.xz-vec2(cell)*50.0;
    float e=min(p.y-column.y,column.z-p.y);
    if(!Covered(cell+ivec2(-1,0),p.y)) e=min(e,f.x);
    if(!Covered(cell+ivec2(1,0),p.y)) e=min(e,50-f.x);
    if(!Covered(cell+ivec2(0,-1),p.y)) e=min(e,f.y);
    if(!Covered(cell+ivec2(0,1),p.y)) e=min(e,50-f.y);
    return smoothstep(0.0,2.0,max(e,0.0))*(1.0+.15*(2.0*Noise(p+volumeNoiseOrigin.xyz)-1.0));
}
#endif
/** Integrates exact cuboid overlap, with eight generation-only shape samples when detail is enabled. */
float CellDensity(vec3 lo)
{
    vec3 hi=lo+volumeCell.xyz;
    ivec2 first=max(ivec2(floor(lo.xz/50.0)),ivec2(0));
    ivec2 last=min(ivec2(ceil(hi.xz/50.0))-1,ivec2(volumeExtent.z-1));
    float density=0,voxelVolume=volumeCell.x*volumeCell.y*volumeCell.z;
    for(int z=first.y;z<=last.y;z++) for(int x=first.x;x<=last.x;x++)
    {
        ivec2 cell=ivec2(x,z);vec4 c=Column(cell);
        if(c.w==0||c.x==0) continue;
        vec3 a=max(lo,vec3(float(x)*50,c.y,float(z)*50));
        vec3 b=min(hi,vec3(float(x+1)*50,c.z,float(z+1)*50));
        vec3 size=max(b-a,vec3(0));if(any(equal(size,vec3(0))))continue;
        float factor=1;
        #if VGE_VOLUME_DENSITY_DETAIL
        factor=0;
        for(int iz=0;iz<2;iz++)for(int iy=0;iy<2;iy++)for(int ix=0;ix<2;ix++)
            factor+=Shape(a+size*(vec3(ix,iy,iz)*.5+.25),cell,c)*.125;
        #endif
        density+=c.x*(size.x*size.y*size.z/voxelVolume)*factor;
    }
    return density;
}
#endif
