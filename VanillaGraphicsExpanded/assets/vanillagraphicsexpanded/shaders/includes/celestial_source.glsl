// Deterministic fictional source; coordinates agree with the CPU cube and hash contracts.
/** Avalanches a wrapping unsigned lattice key with the shared deterministic hash. */
uint celestialHash(uint key)
{
    key ^= key >> 16u; key *= 0x7feb352du; key ^= key >> 15u; key *= 0x846ca68bu;
    return key ^ (key >> 16u);
}
/** Converts OpenGL face-local coordinates into the common celestial direction. */
vec3 celestialDirection(int face, vec2 p)
{
    if (face == 0) return normalize(vec3(1, -p.y, -p.x));
    if (face == 1) return normalize(vec3(-1, -p.y, p.x));
    if (face == 2) return normalize(vec3(p.x, 1, p.y));
    if (face == 3) return normalize(vec3(p.x, -1, -p.y));
    if (face == 4) return normalize(vec3(p.x, -p.y, 1));
    return normalize(vec3(-p.x, -p.y, -1));
}
/** Smoothly interpolates direction-space value noise without face-local seams. */
float celestialNoise(vec3 p)
{
    ivec3 cell = ivec3(floor(p)); vec3 f = fract(p); f = f*f*(3.0-2.0*f);
    float result = 0.0;
    for (int z=0; z<2; z++) for (int y=0; y<2; y++) for (int x=0; x<2; x++)
    {
        uvec3 i = uvec3(cell + ivec3(x,y,z));
        uint key = i.x*0x8da6b343u ^ i.y*0xd8163841u ^ i.z*0xcb1ab31fu ^ 0x56474531u;
        result += float(celestialHash(key) >> 8u) / 16777216.0
            * (x==0 ? 1.0-f.x : f.x) * (y==0 ? 1.0-f.y : f.y) * (z==0 ? 1.0-f.z : f.z);
    }
    return result;
}
/** Evaluates bounded positive galaxy structure with footprint-filtered multiscale dust. */
vec3 celestialField(vec3 d, int octaves, float footprint)
{
    vec3 g = vec3(d.x, .5*d.y+.8660254038*d.z, -.8660254038*d.y+.5*d.z);
    float latitude = asin(clamp(g.y,-1.0,1.0)), longitude = atan(g.z,g.x);
    float band = exp(-.5*pow(latitude/.2094395102,2.0));
    float bulge = exp(-.5*(pow(latitude/.1396263402,2.0)+pow(longitude/.436332313,2.0)));
    float noise=0.0, weight=0.0, amplitude=1.0, frequency=4.0;
    for (int i=0; i<7; i++)
    {
        if(i>=octaves) break;
        float filtered=clamp(2.0-frequency*footprint,0.0,1.0);
        noise+=amplitude*mix(.5,celestialNoise(g*frequency),filtered);
        weight+=amplitude; amplitude*=.5; frequency*=2.0;
    }
    noise/=weight;
    float w=.05+(band+2.0*bulge)*exp(-2.0*noise)*(.5+noise);
    float t=.25+.5*noise;
    vec3 color=t<.5 ? mix(vec3(1,.65,.35),vec3(1),2.0*t) : mix(vec3(1),vec3(.55,.70,1),2.0*t-1.0);
    return color/dot(color,vec3(.2126,.7152,.0722))*w;
}
/** Evaluates the analytic cube-area corner primitive for coarse integration. */
float celestialArea(vec2 p) { return atan(p.x*p.y,sqrt(1.0+dot(p,p))); }
/** Returns a positive texel area without fine-resolution FP32 cancellation. */
float celestialSolidAngle(ivec2 pixel, int size)
{
    // At fine resolutions the corner primitive subtracts nearly equal FP32 values.
    // The positive midpoint Jacobian avoids cancellation; coarse levels use the exact integral.
    if(size>32)
    {
        vec2 p=2.0*(vec2(pixel)+.5)/float(size)-1.0;
        return 4.0/(float(size)*float(size)*pow(1.0+dot(p,p),1.5));
    }
    vec2 a=2.0*vec2(pixel)/float(size)-1.0, b=2.0*vec2(pixel+1)/float(size)-1.0;
    return celestialArea(b)-celestialArea(vec2(a.x,b.y))-celestialArea(vec2(b.x,a.y))+celestialArea(a);
}
