#version 330 core
@import "./includes/celestial_source.glsl"
layout(std140) uniform CelestialGenerationInputs
{
    vec4 tile; // face, face size, tile origin x/y
    vec4 selection; // residual index range, parent mip, parent face size
    vec4 source; // physical amplitude, storage scale, octaves, physical bound
    vec4 kernel; // cap height and peak density
};
uniform sampler2D catalog;
uniform sampler2D indices;
uniform samplerCube parentMap;
layout(location=0) out vec4 outColor;
/** Explicit nearest rounding avoids driver-dependent truncation when narrowing packed floats. */
float packedRound(float value, float bits)
{
    if(value<=0.0) return 0.0;
    float step=exp2(max(-14.0,floor(log2(value)))-bits);
    return floor(value/step+.5)*step;
}
/** Generates physical source radiance or reduces a completed parent mip into an independent scratch tile. */
void main()
{
    ivec2 pixel=ivec2(gl_FragCoord.xy)+ivec2(tile.zw);
    int size=int(tile.y), face=int(tile.x);
    vec3 d=celestialDirection(face,2.0*(vec2(pixel)+.5)/float(size)-1.0);
    vec3 encoded;
    if(selection.z<0.0)
    {
        vec3 radiance=celestialField(d,int(source.z),2.0/float(size))*source.x;
        for(int j=0;j<int(selection.y);j++)
        {
            int offset=int(selection.x)+j;
            int id=int(texelFetch(indices,ivec2(offset%4096,offset/4096),0).x);
            vec3 star=texelFetch(catalog,ivec2(id*2,0),0).xyz;
            vec3 flux=texelFetch(catalog,ivec2(id*2+1,0),0).xyz;
            radiance+=flux*max(0.0,1.0-(1.0-dot(d,star))/kernel.x)*kernel.y;
        }
        encoded=radiance*source.y;
    }
    else
    {
        // Proportional child footprints retain the odd 3-to-1 reduction at the 1536 level.
        int parentSize=int(selection.w);
        ivec2 first=pixel*parentSize/size, last=(pixel+1)*parentSize/size;
        vec3 sum=vec3(0); float area=0.0;
        for(int y=0;y<3;y++) for(int x=0;x<3;x++)
        {
            ivec2 child=first+ivec2(x,y);
            if(child.x>=last.x || child.y>=last.y) continue;
            vec3 direction=celestialDirection(face,2.0*(vec2(child)+.5)/float(parentSize)-1.0);
            float weight=celestialSolidAngle(child,parentSize);
            sum+=textureLod(parentMap,direction,selection.z).rgb*weight; area+=weight;
        }
        encoded=sum/max(area,1e-20);
    }
    encoded=clamp(encoded,vec3(0),vec3(32000));
    outColor=vec4(packedRound(encoded.r,6.0),packedRound(encoded.g,6.0),packedRound(encoded.b,5.0),1);
}
