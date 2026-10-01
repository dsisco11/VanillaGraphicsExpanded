using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the bounded direct, indirect and outgoing surface-lighting producer.</summary>
[ShaderProgram("Contract", "lumonscene_surface_lighting", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_surface_lighting.csh")]

internal sealed partial class LumonSceneSurfaceLightingShader : TraceGeometryComputeShader, ILumonSceneSurfaceLightingShaderBindings
{
    /// <summary>Adopts the linked executable for retained input submission.</summary>
    public LumonSceneSurfaceLightingShader(GpuComputePipeline pipeline) : base(pipeline) { }
}
