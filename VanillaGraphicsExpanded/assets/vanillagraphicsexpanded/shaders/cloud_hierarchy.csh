#version 430 core
layout(local_size_x=8, local_size_y=8) in;
uniform sampler2D cloudMap;
layout(rg32f) readonly uniform image2D sourceBounds;
layout(rg32f) writeonly uniform image2D targetBounds;
layout(std140) uniform CloudHierarchyParameters { ivec4 hierarchyExtent; };

/** Supplies the min/max reduction identity without relying on finite sentinel heights. */
vec2 emptyBounds()
{
    float infinity = uintBitsToFloat(0x7f800000u);
    return vec2(infinity, -infinity);
}

/** Reads a native leaf with the same validity/occupancy rules as cloud density, never stretching borders. */
vec2 leafBounds(ivec2 cell)
{
    if (any(greaterThanEqual(cell, ivec2(hierarchyExtent.x)))) return emptyBounds();
    vec4 column = texelFetch(cloudMap, cell, 0);
    if (any(isnan(column)) || any(isinf(column)) || column.r <= 0.0 || column.a <= column.b)
        return emptyBounds();
    return column.ba * 50.0;
}

/** Reduces exactly four children; prior dispatch visibility is supplied by the owning compute boundary. */
void main()
{
    ivec2 cell = ivec2(gl_GlobalInvocationID.xy);
    if (any(greaterThanEqual(cell, ivec2(hierarchyExtent.y)))) return;
    vec2 bounds = emptyBounds();
    for (int z = 0; z < 2; z++)
    for (int x = 0; x < 2; x++)
    {
        ivec2 child = cell * 2 + ivec2(x, z);
        vec2 interval;
        if (hierarchyExtent.z != 0) interval = leafBounds(child);
        else interval = imageLoad(sourceBounds, child).rg;
        bounds = vec2(min(bounds.x, interval.x), max(bounds.y, interval.y));
    }
    imageStore(targetBounds, cell, vec4(bounds, 0.0, 0.0));
}
