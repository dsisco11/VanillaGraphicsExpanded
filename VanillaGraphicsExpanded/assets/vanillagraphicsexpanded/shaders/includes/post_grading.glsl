/** Adjusts display brightness and contrast around middle gray, then blends a luminance-derived warm tint. */
vec3 VgeGradeDisplay(vec3 color,vec4 controls,float warmth)
{
    float exponent=1.0/max(controls.x*controls.y,0.001);
    vec3 adjusted=pow(max(color,vec3(0)),vec3(exponent))*max(controls.z,0.0);
    adjusted=(adjusted-0.5)*max(1.0+controls.w,0.0)+0.5;
    float luminance=dot(max(adjusted,vec3(0)),vec3(0.2126,0.7152,0.0722));
    vec3 warm=luminance*vec3(1.12,0.98,0.78);
    return clamp(mix(adjusted,warm,clamp(warmth,0.0,1.0)),0.0,1.0);
}
/** Produces deterministic screen-space grain without an external noise implementation. */
float VgeDisplayGrain(vec2 pixel,float time)
{
    vec2 cell=floor(pixel)+vec2(floor(time*24.0),floor(time*13.0));
    return fract(sin(dot(cell,vec2(19.173,83.927)))*17831.743);
}
/** Composes independently authored damage and frost edge treatments after grading. */
vec3 VgeScreenEffects(vec3 color,vec2 uv,vec2 pixel,vec4 effects,vec4 vignette)
{
    float grain=VgeDisplayGrain(pixel,effects.y);
    float glitch=clamp(effects.z,0.0,1.0);
    color*=1.0-glitch*0.18*grain;
    float radius=length((uv-0.5)*2.0);
    float edge=smoothstep(0.48,1.25,radius);
    float frost=clamp(vignette.z,0.0,1.0)*edge*(0.65+0.35*grain);
    color=mix(color,vec3(0.78,0.87,0.96),frost);
    float side=clamp(1.0+vignette.y*(uv.x*2.0-1.0),0.0,1.0);
    float injury=clamp(vignette.x,0.0,1.0)*edge*side;
    return mix(color,vec3(0.45,0.015,0.01),injury);
}
