#version 430 core
layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;
layout(std140, binding = 28) uniform UniformStateInputs
{
    float scalar;
    float values[2];
    vec3 vector;
    mat4 transform;
};
layout(rgba32f) writeonly uniform image2D result;

/// Exposes scalar, complete array, vector and matrix upload representations for semantic readback.
void main()
{
    imageStore(result, ivec2(0), vec4(scalar, values[0] + values[1], vector.x, transform[0][0]) + (UNIFORM_ALTERNATE ? vec4(1) : vec4(0)));
}
