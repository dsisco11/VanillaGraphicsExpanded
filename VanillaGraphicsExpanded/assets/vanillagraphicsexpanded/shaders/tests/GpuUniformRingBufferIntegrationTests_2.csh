#version 430 core
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

layout(binding = 0, rgba32ui) writeonly uniform uimage3D outImg;

void main()
{
    imageStore(outImg, ivec3(0, 0, 0), uvec4(0u));
}

