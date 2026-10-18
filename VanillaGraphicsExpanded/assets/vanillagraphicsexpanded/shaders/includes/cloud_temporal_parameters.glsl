layout(std140,binding=14) uniform CloudTemporalParameters
{
    mat4 cloudPreviousProjection;
    vec4 cloudPreviousTranslation;
    vec4 cloudTrace; // reconstruction width/height, current two-by-two phase
    vec4 cloudHistory; // valid, previous reconstruction phase x/y, history weight
    vec4 cloudLayout; // physical trace width/height, reconstruction-to-trace ratio, spare
};
/** Maps a physical trace texel to its scheduled reconstruction position, including odd trailing edges. */
ivec2 CloudTraceLocation(ivec2 pixel)
{
    return cloudLayout.z>1.5 ? min(pixel*2+ivec2(cloudTrace.zw),ivec2(cloudTrace.xy)-1) : pixel;
}
/** Maps a reconstruction texel to its exact full-view position without inventing a sixteen-frame phase cycle. */
vec2 CloudReconstructionUv(ivec2 pixel,vec2 phase)
{
    vec2 offset=cloudLayout.z>1.5 ? vec2(0) : phase;
    return (min(vec2(pixel)*2.0+offset,vgeFrame.screenSize-vec2(1))+.5)/vgeFrame.screenSize;
}
/** Selects one actual traced full-view pixel; odd-sized trailing tiles remain inside the viewport. */
vec2 CloudTraceUv(ivec2 pixel,vec2 phase)
{
    return CloudReconstructionUv(CloudTraceLocation(pixel),phase);
}
