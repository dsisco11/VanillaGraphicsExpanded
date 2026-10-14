#version 430 core
@import "./includes/post_local_exposure.glsl"
layout(local_size_x=8,local_size_y=8) in;
layout(std140,binding=28) uniform LocalExposureInputs { vec4 filterParameters; };
uniform sampler2D logImage;
layout(rg32f,binding=0) uniform writeonly image2D baseOutput;
/** Writes a complete current-frame base; missing cells remain explicitly invalid. */
void main()
{
    ivec2 pixel=ivec2(gl_GlobalInvocationID.xy);
    if(any(greaterThanEqual(pixel,imageSize(baseOutput)))) return;
    imageStore(baseOutput,pixel,vec4(VgeFilterLocalBase(logImage,pixel,filterParameters.x),0,0));
}
