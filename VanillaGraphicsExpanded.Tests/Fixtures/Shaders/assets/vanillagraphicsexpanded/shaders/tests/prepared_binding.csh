#version 430 core
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;
uniform sampler2D inputs[2];
uniform sampler2D unused;
layout(rgba32f) writeonly uniform image2D outputImage;

/// Writes both array inputs so linked reflection retains their complete extent.
void main()
{
    imageStore(outputImage, ivec2(0), texelFetch(inputs[0], ivec2(0), 0) + texelFetch(inputs[1], ivec2(0), 0));
}
