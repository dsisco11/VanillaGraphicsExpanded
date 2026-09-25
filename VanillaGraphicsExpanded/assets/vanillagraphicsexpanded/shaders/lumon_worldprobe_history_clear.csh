#version 430 core
layout(local_size_x = 64) in;
layout(std430, binding = 0) readonly buffer HistoryClearSlots
{
    uvec4 clearInfo; // Slot count, clipmap resolution, tile size, full-reset flag.
    uint slots[];
};
layout(binding = 0, rgba16f) writeonly uniform image2D radianceAtlas;
layout(binding = 1, rgba16f) writeonly uniform image2D visibilityAtlas;
layout(binding = 2, rg16f) writeonly uniform image2D distanceAtlas;
layout(binding = 3, rg32f) writeonly uniform image2D metadataAtlas;

/** One group owns one unique physical probe; no two groups write overlapping history. */
void main()
{
    uint entry = gl_WorkGroupID.x + gl_WorkGroupID.y * gl_NumWorkGroups.x;
    if (entry >= clearInfo.x) return;
    uint slot = clearInfo.w != 0u ? entry : slots[entry];
    uint resolution = clearInfo.y;
    uint x = slot % resolution;
    slot /= resolution;
    uint y = slot % resolution;
    slot /= resolution;
    uint z = slot % resolution;
    uint level = slot / resolution;
    ivec2 scalar = ivec2(x + z * resolution, y + level * resolution);
    if (gl_LocalInvocationIndex == 0u)
    {
        imageStore(visibilityAtlas, scalar, vec4(0.0));
        imageStore(distanceAtlas, scalar, vec4(0.0));
        imageStore(metadataAtlas, scalar, vec4(0.0));
    }
    uint tile = clearInfo.z;
    ivec2 origin = scalar * int(tile);
    for (uint texel = gl_LocalInvocationIndex; texel < tile * tile; texel += 64u)
        imageStore(radianceAtlas, origin + ivec2(texel % tile, texel / tile), vec4(0.0));
}
