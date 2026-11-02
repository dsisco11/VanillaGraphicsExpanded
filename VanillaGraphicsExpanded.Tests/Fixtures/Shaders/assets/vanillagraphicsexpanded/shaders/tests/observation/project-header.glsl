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

layout(local_size_x=64)in;
#if KIND == 0
layout(binding=0)uniform sampler2DArray sourceImage;
#elif KIND == 1
layout(binding=0)uniform isampler2DArray sourceImage;
#else
layout(binding=0)uniform usampler2DArray sourceImage;
#endif
#if KIND == 0
layout(binding=1)uniform sampler2DArray referenceImage;
#elif KIND == 1
layout(binding=1)uniform isampler2DArray referenceImage;
#else
layout(binding=1)uniform usampler2DArray referenceImage;
#endif
layout(rgba32f,binding=0)writeonly uniform image2DArray destinationImage;



// Serial policies count their actual loop iterations independently of the requested extent.
uint orderedVisits;
