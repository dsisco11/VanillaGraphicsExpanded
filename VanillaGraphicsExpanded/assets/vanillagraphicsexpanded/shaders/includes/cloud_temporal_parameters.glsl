layout(std140,binding=14) uniform CloudTemporalParameters
{
    mat4 cloudPreviousProjection;
    vec4 cloudPreviousTranslation;
    vec4 cloudTrace; // reconstruction width/height, current two-by-two phase
    vec4 cloudHistory; // valid, diagnostic display mode, reserved
    vec4 cloudLayout; // physical trace width/height, reconstruction-to-trace ratio, full-view divisor
};
