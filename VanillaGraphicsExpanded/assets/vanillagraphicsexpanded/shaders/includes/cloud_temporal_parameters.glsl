layout(std140,binding=14) uniform CloudTemporalParameters
{
    mat4 cloudPreviousProjection;
    vec4 cloudPreviousTranslation;
    vec4 cloudTrace; // reduced width/height, current two-by-two phase
    vec4 cloudHistory; // valid, previous phase x/y, current/previous RGB storage ratio
};
/** Selects an exact full-resolution pixel; odd-sized trailing tiles stay inside the viewport. */
vec2 CloudTraceUv(ivec2 pixel,vec2 phase)
{
    return (min(vec2(pixel)*2.0+phase,vgeFrame.screenSize-vec2(1))+.5)/vgeFrame.screenSize;
}
