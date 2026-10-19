#version 430 core
@import "encoding.glsl"
layout(local_size_x=4,local_size_y=4,local_size_z=4) in;
layout(std140) uniform VolumeCopy { ivec4 copyExtent; vec4 copyScales; vec4 copyCap; };
uniform sampler3D oldDiffuse;
layout(rgba16f) writeonly uniform image3D newDiffuse;
/** Copies untouched cells with the destination's bounded representation, without changing the published source. */
void main()
{
    ivec3 p=ivec3(gl_GlobalInvocationID);
    if(any(greaterThanEqual(p,copyExtent.xyz))) return;
    vec3 physical=texelFetch(oldDiffuse,p,0).rgb*copyScales.x;
    imageStore(newDiffuse,p,CloudVolumeEncode(physical,copyCap.rgb,copyScales.y));
}
