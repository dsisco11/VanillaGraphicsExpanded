#version 430 core
layout(local_size_x=64) in;
struct Answer { ivec4 status; ivec4 cell; ivec4 normal; vec4 fraction; vec4 radiance; };
layout(std430,binding=0) readonly buffer Answers { Answer answers[]; };
layout(std430,binding=1) writeonly buffer Completions { ivec4 completion[]; };
layout(std430,binding=2) writeonly buffer Descriptors { Answer descriptors[]; };
layout(std430,binding=3) buffer Counter { uint count; };
/** Exports geometry metadata and only missing-light descriptors; ready RGB never leaves the GPU. */
void main()
{
    uint i=gl_GlobalInvocationID.x;
    if(i>=uint(answers.length())) return;
    Answer a=answers[i];
    int descriptor=-1;
    if(a.status.x==1)
    {
        bool ready=a.radiance.w==1.0 && !any(isnan(a.radiance.xyz)) && !any(isinf(a.radiance.xyz));
        if(ready) descriptor=-2;
        else { descriptor=int(atomicAdd(count,1u)); descriptors[descriptor]=a; }
    }
    completion[i]=ivec4(a.status.xy,floatBitsToInt(a.fraction.w),descriptor);
}
