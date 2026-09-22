#version 430 core
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(binding = 0, rgba16f) writeonly uniform image2DArray irradianceAtlas;

/** Clears stale history on geometry invalidation without relying on optional texture-clear extensions. */
void main()
{
    ivec3 cell = ivec3(gl_GlobalInvocationID);
    if (all(lessThan(cell, imageSize(irradianceAtlas)))) imageStore(irradianceAtlas, cell, vec4(0.0));
}
