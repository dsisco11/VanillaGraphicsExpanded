#version 450 core
layout(std140,binding=28) uniform AssertionInputs { ivec4 domain; uvec4 selectors; dvec4 limits; vec4 multipliers; ivec4 controls; uvec4 channelMinimum; uvec4 channelMaximum; vec4 evaluation; };
#define extent domain.xyz
#define mode domain.w
#define channels selectors.x
#define constantValue selectors.y
#define rankSide int(selectors.z)
#define rankSlices int(selectors.w)
#define tolerance limits.x
#define bounds limits.yz
#define referenceChannel controls.x
#define pixelStride controls.y
#define pixelOffset controls.z
#define predicateChoice controls.w
#define parameters multipliers

layout(local_size_x=8,local_size_y=8)in;
layout(binding=0)uniform sampler2DArray sourceImage;
layout(rgba32f,binding=0)writeonly uniform image2DArray destination;



/// Maps integer nearest texels and expands only authored controls.x, including zero alpha.
void main(){
    ivec2 p=ivec2(gl_GlobalInvocationID.xy);
    if(any(greaterThanEqual(p,domain.xy)))return;
    ivec2 from=p*textureSize(sourceImage,0).xy/domain.xy;
    vec4 original=texelFetch(sourceImage,ivec3(from,0),0),expanded=vec4(0);
    for(int c=0;c<controls.x;c++)expanded[c]=original[c];
    imageStore(destination,ivec3(p,controls.y),expanded);
}
