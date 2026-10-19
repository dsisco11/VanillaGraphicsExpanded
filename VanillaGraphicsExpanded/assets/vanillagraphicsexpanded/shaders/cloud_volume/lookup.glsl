#ifndef VGE_STORED_CLOUD_LOOKUP
#define VGE_STORED_CLOUD_LOOKUP
/** Converts bounded native-local coordinates to hardware trilinear circular addressing, using wind once upstream. */
vec3 CloudVolumeUv(vec3 native,vec3 firstGuardNative,vec3 cellSize,ivec3 size,ivec2 firstSlot)
{
    vec3 cells=(native-firstGuardNative)/cellSize;
    return (cells+vec3(firstSlot.x,0,firstSlot.y))/vec3(size);
}
/** Rejects finite native edges explicitly; texture Repeat is only an internal material-slot seam. */
bool CloudVolumeInside(vec3 native,float nativeExtent,vec2 heights)
{
    return native.x>=0 && native.z>=0 && native.x<nativeExtent && native.z<nativeExtent &&
        native.y>=heights.x && native.y<=heights.y;
}
#endif
