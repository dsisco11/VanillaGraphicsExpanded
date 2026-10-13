#version 430 core
@import "./includes/pbr_persistent_lighting.glsl"
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
        // Carry storage rejection through the existing compact completion, before CPU
        // integration grants a resident lease or advances the scheduler's success state.
        if (a.radiance.w == 1.0 && !VgeDirectionalLightAdmitted(a.radiance.xyz)) a.status.xy = ivec2(5, 0);
        bool ready=a.status.x == 1 && a.radiance.w==1.0 && VgeDirectionalLightAdmitted(a.radiance.xyz);
        if(ready) descriptor=-2;
        else if (a.status.x == 1) { descriptor=int(atomicAdd(count,1u)); descriptors[descriptor]=a; }
    }
    completion[i]=ivec4(a.status.xy,floatBitsToInt(a.fraction.w),descriptor);
}
