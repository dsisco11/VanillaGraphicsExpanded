#version 430 core
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

// Sampler uses an explicit unit via layout(binding=...).
layout(binding = 3) uniform usampler3D uOcc;

// Image uses an explicit unit via layout(binding=...).
layout(binding = 0, r32ui) writeonly uniform uimage3D outImg;

void main()
{
    uvec4 v = texelFetch(uOcc, ivec3(0, 0, 0), 0);
    imageStore(outImg, ivec3(0, 0, 0), v);
}

