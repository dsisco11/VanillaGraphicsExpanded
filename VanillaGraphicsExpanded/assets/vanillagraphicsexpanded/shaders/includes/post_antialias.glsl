/** Applies bounded edge smoothing using the perceptual luminance stored alongside HDR color.
 * This filter is controlled by the native antialiasing switch.
 */
vec4 VgeFilterDisplayEdge(sampler2D image,vec2 coordinate,vec2 texel)
{
    vec4 center=texture(image,coordinate);
    vec4 west=texture(image,coordinate-vec2(texel.x,0));
    vec4 east=texture(image,coordinate+vec2(texel.x,0));
    vec4 south=texture(image,coordinate-vec2(0,texel.y));
    vec4 north=texture(image,coordinate+vec2(0,texel.y));
    float low=min(center.a,min(min(west.a,east.a),min(south.a,north.a)));
    float high=max(center.a,max(max(west.a,east.a),max(south.a,north.a)));
    float contrast=high-low;
    if(contrast<max(0.04,high*0.12)) return center;
    vec2 gradient=vec2(east.a-west.a,north.a-south.a);
    float magnitude=length(gradient);
    if(magnitude<0.00001) return center;
    vec2 offset=0.5*texel*gradient/magnitude;
    vec4 pair=0.5*(texture(image,coordinate-offset)+texture(image,coordinate+offset));
    float blend=0.5*smoothstep(0.0,0.25,contrast);
    return vec4(mix(center.rgb,pair.rgb,blend),center.a);
}
