layout(std140,binding=14) uniform CloudTemporalParameters
{
    vec4 cloudPreviousTranslation;
    vec4 cloudTrace; // reconstruction width/height, current two-by-two phase
    vec4 cloudHistory; // valid, diagnostic display mode, stationary material-to-view transform, uint sampling epoch bits
    vec4 cloudLayout; // physical trace width/height, reconstruction-to-trace ratio, full-view divisor
};
