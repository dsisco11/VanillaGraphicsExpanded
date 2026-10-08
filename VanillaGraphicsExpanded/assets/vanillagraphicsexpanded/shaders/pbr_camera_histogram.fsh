#version 330 core
@import "./includes/camera_exposure_inputs.glsl"
uniform sampler2D sceneRadiance;
layout(location=0) out vec2 histogram;
/** Bins a fixed stratified grid without atomics, compute shaders or CPU readback. */
void main()
{
    int bin = int(gl_FragCoord.x);
    vec2 total = vec2(0);
    ivec2 size = textureSize(sceneRadiance,0);
    for (int y=0; y<36; ++y) for (int x=0; x<64; ++x)
    {
        vec2 uv = (vec2(x,y)+.5)/vec2(64,36);
        vec3 color = texelFetch(sceneRadiance, min(ivec2(uv*vec2(size)),size-1),0).rgb;
        if (any(isnan(color)) || any(isinf(color))) continue;
        float luminance = dot(max(color,vec3(0)),vec3(.2126,.7152,.0722));
        if (!(luminance > exp2(meterRange.x)) || luminance >= exp2(meterRange.y)) continue;
        float level = log2(luminance);
        int selected = clamp(int((level-meterRange.x)*64.0/(meterRange.y-meterRange.x)),0,63);
        if (selected != bin) continue;
        vec2 centered = uv*2.0-1.0;
        float weight = mix(1.0, .25+.75*exp(-2.0*dot(centered,centered)),meterRange.z);
        total += vec2(weight,weight*level);
    }
    histogram = total;
}
