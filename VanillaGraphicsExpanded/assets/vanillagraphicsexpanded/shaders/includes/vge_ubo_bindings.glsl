// ============================================================================
// VGE UBO Binding Contracts
//
// Defines reserved UBO binding points. These numbers are part of the runtime
// contract between shaders and the engine-side binding code.
//
// Source of truth in C#: VanillaGraphicsExpanded/Rendering/GpuBindingRegistry.cs
// ============================================================================

#ifndef VGE_UBO_BINDINGS_GLSL
#define VGE_UBO_BINDINGS_GLSL

#define VGE_UBO_FRAME_BINDING       12
#define VGE_UBO_WORLDPROBE_BINDING  13
#define VGE_UBO_OBJECT_BINDING      14
#define VGE_UBO_MATERIAL_BINDING    15
#define VGE_UBO_LIGHTS_BINDING      16
#define VGE_UBO_TERRAIN_BRIDGE_BINDING 27

#endif // VGE_UBO_BINDINGS_GLSL
