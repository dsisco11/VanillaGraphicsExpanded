#ifndef VGE_CLOUD_VOLUME_SAMPLING
#define VGE_CLOUD_VOLUME_SAMPLING
#ifdef VGE_CLOUD_PMJ
@import "./squirrel3.glsl"
uniform sampler2D cloudPmjJitter;
/** Supplies distinct view/light dimensions; each full-resolution pixel advances once per four-phase cycle. */
vec2 CloudRayJitter(vec2 uv)
{
    uvec2 pixel=uvec2(floor(uv*vgeFrame.screenSize));
    uint cycle=uint(textureSize(cloudPmjJitter,0).x);
    uint index=(vgeFrame.frameIndex/4u+Squirrel3HashU(pixel.x,pixel.y))%cycle;
    // Decode at quantization-bin centers so UNorm endpoints cannot place samples on segment faces.
    return (texelFetch(cloudPmjJitter,ivec2(int(index),0),0).rg*65535.0+.5)/65536.0;
}
/** Decorrelates occupied cells, view samples and light directions while retaining temporal progression. */
float CloudSegmentJitter(float value,ivec2 cell,int sampleIndex,uint salt)
{
    uint hash=Squirrel3HashU(uint(cell.x),uint(cell.y),uint(sampleIndex)^salt);
    float shift=float(hash>>8u)/16777216.0;
    return clamp(fract(value+shift),1.0/65536.0,65535.0/65536.0);
}
#else
/** Keeps reference transport deterministic and independent of the PMJ texture. */
vec2 CloudRayJitter(vec2 uv) { return vec2(.5); }
/** Uses midpoint quadrature for all reference and constant-density integration segments. */
float CloudSegmentJitter(float value,ivec2 cell,int sampleIndex,uint salt) { return .5; }
#endif
#endif
