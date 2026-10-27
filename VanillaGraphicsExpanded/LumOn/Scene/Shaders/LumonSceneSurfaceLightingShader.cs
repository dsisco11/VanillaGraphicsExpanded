using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Declares the bounded direct, indirect and outgoing surface-lighting producer.</summary>
[ShaderProgram("Contract", "lumonscene_surface_lighting", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_surface_lighting.csh")]

internal sealed partial class LumonSceneSurfaceLightingShader : TraceGeometryComputeShader, ILumonSceneSurfaceLightingShaderBindings
{
    private VgeFrameUniformBuffer? frameInputs;
    #region Public API
    /// <summary>Retains an explicit calibration publication for isolated compute submission.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Adopts the linked executable for retained input submission.</summary>
    public LumonSceneSurfaceLightingShader(ComputeShaderAsset pipeline) : base(pipeline) { }
    #endregion
    #region Private
    /// <summary>Uses the shared world calibration unless the caller supplied an isolated snapshot.</summary>
    CpuUniformBuffer ILumonSceneSurfaceLightingShaderBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    #endregion
}
