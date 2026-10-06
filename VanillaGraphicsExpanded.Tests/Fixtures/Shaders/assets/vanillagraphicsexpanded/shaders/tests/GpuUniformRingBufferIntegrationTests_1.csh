#version 430 core
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

layout(std140, binding = 0) uniform TestParams
{
    uvec4 u0;
} params;

layout(binding = 0, rgba32ui) writeonly uniform uimage3D outImg;

void main()
{
    imageStore(outImg, ivec3(0, 0, 0), params.u0);
}

