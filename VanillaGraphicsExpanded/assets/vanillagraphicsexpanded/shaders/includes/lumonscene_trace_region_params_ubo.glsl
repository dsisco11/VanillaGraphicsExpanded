// LumOnScene TraceSceneRegion -> Clipmap params UBO
//
// Non-opaque uniforms for lumonscene_trace_scene_region_to_clipmap.csh.
// All fields are std140-aligned.

#ifndef LUMONSCENE_TRACE_REGION_PARAMS_UBO_GLSL
#define LUMONSCENE_TRACE_REGION_PARAMS_UBO_GLSL

@import "./vge_ubo_bindings.glsl"

layout(std140, binding = VGE_UBO_OBJECT_BINDING) uniform VgeLumOnSceneTraceRegionParamsUBO
{
    // x = regionUpdateCount (uint)
    // y = levels (int stored as uint)
    // z = resolution (int stored as uint)
    // w reserved
    uvec4 counts;

    // originMinCell[level].xyz
    ivec4 originMinCell[8];

    // ring[level].xyz
    ivec4 ring[8];
} vgeTraceRegionParams;

#endif // LUMONSCENE_TRACE_REGION_PARAMS_UBO_GLSL
