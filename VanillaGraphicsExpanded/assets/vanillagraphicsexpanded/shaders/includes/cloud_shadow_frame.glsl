#ifndef VGE_CLOUD_SHADOW_FRAME
#define VGE_CLOUD_SHADOW_FRAME
/** Derives a conservative light-space box from the same certified native height summary. */
bool CloudShadowFrame(vec3 light,out vec3 center,out vec3 u,out vec3 v,out vec3 radius)
{
    center=vec3(0);u=vec3(1,0,0);v=vec3(0,1,0);radius=vec3(0);
    int root=cloudTraversal.y;
    if(cloudTraversal.x==0 || root<0 || root>7) return false;
    if(volumeOffsetWidth.w<=float(1<<root) || volumeOffsetWidth.w>float(2<<root)
        || any(notEqual(textureSize(cloudHeightBounds,0),ivec2(1<<root)))) return false;
    vec2 heights=texelFetch(cloudHeightBounds,ivec2(0),root).rg;
    if(any(isnan(heights))||any(isinf(heights))||heights.y<=heights.x) return false;
    float halfWidth=volumeOffsetWidth.w*25.0;
    center=vec3(halfWidth,heights.x*.5+heights.y*.5,halfWidth);
    vec3 halfSize=vec3(halfWidth,heights.y*.5-heights.x*.5,halfWidth);
    u=normalize(cross(abs(light.y)<.99?vec3(0,1,0):vec3(1,0,0),light));
    v=cross(light,u);
    radius=vec3(dot(abs(u),halfSize),dot(abs(v),halfSize),dot(abs(light),halfSize));
    return !any(isnan(center))&&!any(isinf(center))&&!any(isnan(radius))&&!any(isinf(radius))
        &&all(greaterThan(radius,vec3(0)));
}
#endif
