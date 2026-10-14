#version 430 core
@import "./includes/pbr_color.glsl"
@import "./includes/post_filmic.glsl"
layout(local_size_x=4,local_size_y=4,local_size_z=4) in;
layout(std140,binding=28) uniform ColorGradingInputs { vec4 gradingControls; };
layout(rgba16f,binding=0) uniform writeonly image3D gradedOutput;
/** Bakes the complete display response into an unpublished three-dimensional candidate. */
void main()
{
    ivec3 pixel=ivec3(gl_GlobalInvocationID), size=imageSize(gradedOutput);
    if(any(greaterThanEqual(pixel,size))) return;
    vec3 coordinate=vec3(pixel)/vec3(size-1);
    imageStore(gradedOutput,pixel,vec4(VgeGradedDisplay(VgeGradingRadiance(coordinate),gradingControls),1));
}
