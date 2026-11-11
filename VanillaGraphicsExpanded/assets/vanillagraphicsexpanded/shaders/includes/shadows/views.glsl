// Hosts declare the generated usamplerBuffer shadowViews binding. Each record is sixteen uvec4 texels.
/// Rejects out-of-range compact view references without reading arbitrary projection data.
bool shadowViewExists(uint view) { return view<uint(textureSize(shadowViews))/16u; }
/// Fetches one bounded record vector; callers first validate the compact view reference.
uvec4 shadowViewWords(uint view,uint vectorIndex) { return texelFetch(shadowViews,int(view*16u+vectorIndex)); }
/// Projects the final deformed position relative to this record's explicitly frozen world origin.
vec4 shadowProject(uint view,vec4 position) {
    mat4 projection=mat4(uintBitsToFloat(shadowViewWords(view,0u)),uintBitsToFloat(shadowViewWords(view,1u)),
        uintBitsToFloat(shadowViewWords(view,2u)),uintBitsToFloat(shadowViewWords(view,3u)));
    return projection*position;
}
/// Returns four homogeneous half-space distances around the uncached rectangle inside the virtual region.
vec4 shadowClipRectangle(uint view,vec4 position) {
    uvec4 region=shadowViewWords(view,4u),rectangle=shadowViewWords(view,5u);
    vec2 pixel=(position.xy+position.ww)*0.5*vec2(region.zw);
    return vec4(pixel-vec2(rectangle.xy)*position.w,vec2(rectangle.zw)*position.w-pixel);
}
