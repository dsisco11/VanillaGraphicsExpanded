using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Supplies selected native stages to explicit engine-integration fixtures using backend-owned compilation.</summary>
internal static class PrecompiledStageFixture
{
    #region Internal API
    /// <summary>Freezes one selected module and completes its native stage through the integration boundary.</summary>
    internal static GpuShaderModule Load(ShaderStageSelection selection, ShaderAssetReader read)
    {
        var input = ShaderExecutableInputs.CreateStage(selection, read(selection.BinaryPath));
        return RHICommandContext.Current.OpenGL.CompileExternalStage(input).RequireResource();
    }
    /// <summary>Maps legacy test-native signatures to supplied semantic stage inputs.</summary>
    internal static ShaderStage Stage(ShaderType kind) => kind switch
    {
        ShaderType.VertexShader => ShaderStage.Vertex,
        ShaderType.FragmentShader => ShaderStage.Fragment,
        ShaderType.GeometryShader => ShaderStage.Geometry,
        ShaderType.TessControlShader => ShaderStage.TessellationControl,
        ShaderType.TessEvaluationShader => ShaderStage.TessellationEvaluation,
        ShaderType.ComputeShader => ShaderStage.Compute,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    /// <summary>Checks a selected catalog kind against the explicit native type expected by an integration fixture.</summary>
    internal static bool Matches(ShaderStageKind kind, ShaderType type) => kind switch
    {
        ShaderStageKind.Vertex => type == ShaderType.VertexShader,
        ShaderStageKind.Fragment => type == ShaderType.FragmentShader,
        ShaderStageKind.Geometry => type == ShaderType.GeometryShader,
        ShaderStageKind.TessellationControl => type == ShaderType.TessControlShader,
        ShaderStageKind.TessellationEvaluation => type == ShaderType.TessEvaluationShader,
        ShaderStageKind.Compute => type == ShaderType.ComputeShader,
        _ => false
    };
    #endregion
}
