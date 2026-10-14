#version 430 core
@import "./includes/pbr_frame_lighting.glsl"
@import "./includes/post_local_exposure.glsl"
layout(local_size_x=8,local_size_y=8) in;
uniform sampler2D sceneRadiance;
layout(rg32f,binding=0) uniform writeonly image2D logOutput;
/** Partitions all source pixels exactly once, including odd sizes and sparse valid emitters. */
void main()
{
    ivec2 pixel=ivec2(gl_GlobalInvocationID.xy), grid=imageSize(logOutput);
    if(any(greaterThanEqual(pixel,grid))) return;
    ivec2 size=textureSize(sceneRadiance,0);
    ivec2 first=pixel*size/grid, end=(pixel+1)*size/grid;
    float mean=0.0, count=0.0;
    for(int y=first.y;y<end.y;y++) for(int x=first.x;x<end.x;x++)
    {
        float q;
        if(!VgeLocalLogLuminance(VgePhysicalFrameLight(texelFetch(sceneRadiance,ivec2(x,y),0).rgb),q)) continue;
        // Incremental averaging avoids a sum proportional to the source footprint.
        count+=1.0; mean+=(q-mean)/count;
    }
    imageStore(logOutput,pixel,count>0.0 ? vec4(mean,1,0,0) : vec4(0));
}
