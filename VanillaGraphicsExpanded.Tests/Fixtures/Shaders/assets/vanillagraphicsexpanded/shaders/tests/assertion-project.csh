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


/// Authors an explicit fixture input, independent of the shader algorithm under test.
vec4 project(vec4 a,vec4 b,ivec3 coordinate,uint index){ switch(predicateChoice){case 1:{
a.g+=20.0;return a;
}
case 2:{
return a;
}
case 3:{
precise vec4 square=a*a; return square;
}
case 4:{
return roundEven(a*255);
}
case 5:{
return vec4(dot(a.rgb,vec3(0.2126,0.7152,0.0722)),0,0,0);
}
case 6:{
precise vec3 physical=a.rgb/parameters.x;
precise vec3 weighted=physical*b.r;
return vec4(weighted,0);
}
case 7:{
return vec4(b.g==1.0?parameters.x:a.r,max(a.g,b.g==1.0?1.0:0.0),0,0);
}
case 8:{
return vec4(a.r,0,0,0);
}
case 9:{
a.r+=1.0;return a;
}
case 10:{
a.a=4.0;return a;
}
case 11:{
a.a=index==4u?4.0:0.0;return a;
}
case 12:{
precise vec3 physical=a.rgb/parameters.x;return vec4(physical,0);
}
case 13:{
return vec4(a.rgb,0.37);
}
case 14:{
return vec4(0,-a.b,0,0);
}
case 15:{
precise vec3 contribution=a.rgb-b.rgb;return vec4(contribution,0);
}
case 16:{
return vec4(0,0,0,a.r);
}
case 17:{
if(index!=0u)return vec4(0);precise float sum=0;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+(value.r);}return vec4(sum,0,0,0);
}
case 18:{
if(index!=0u)return vec4(0);precise float sum=0;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+((value.r+value.g)+value.b);}return vec4(sum,0,0,0);
}
case 19:{
return vec4(a.r+parameters.x,float(coordinate.x),float(coordinate.y),1.0);
}
case 20:{
return a/b;
}
case 21:{
precise float value=((a.r+a.g)+a.b)/3;return vec4(value,0,0,0);
}
case 22:{
if(index!=0u)return vec4(0);precise float sum=0;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+(value.r*value.r);}return vec4(sum,0,0,0);
}
case 23:{
precise vec3 radiance=a.rgb/parameters.x;return vec4(radiance,0);
}
case 24:{
precise float moment=a.r*float(coordinate.x);return vec4(moment,0,0,0);
}
case 25:{
precise float scaled=a.r*255;precise float delta=scaled-(100+(float(coordinate.x/8)+.5)/8);return vec4(delta,0,0,0);
}
case 26:{
if(index!=0u)return vec4(0);precise float sum=0;for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+((value.r*.2126+value.g*.7152)+value.b*.0722);}return vec4(sum,0,0,0);
}
case 27:{
return vec4(a.rgb,1024);
}
}return vec4(0); }
/// Copies the complete original domain, including the final partial workgroup.
void main(){
    uint index=gl_GlobalInvocationID.x+gl_GlobalInvocationID.y*gl_NumWorkGroups.x*gl_WorkGroupSize.x;
    if(index>=uint(extent.x*extent.y*extent.z))return;
    ivec3 p=ivec3(int(index)%extent.x,(int(index)/extent.x)%extent.y,int(index)/(extent.x*extent.y));
    imageStore(destinationImage,p,project(vec4(texelFetch(sourceImage,p,0)),vec4(texelFetch(referenceImage,p,0)),p,index));
}
