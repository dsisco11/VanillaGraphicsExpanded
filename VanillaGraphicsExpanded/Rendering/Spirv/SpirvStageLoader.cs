using System;
using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Loads a preselected SPIR-V stage; the caller owns the returned shader handle.</summary>
internal static class SpirvStageLoader
{
    #region Loading
    /// <summary>Consumes a borrowed binary synchronously with exact typed specialization argument bits.</summary>
    public static (int Shader, GpuBindingContract Contract) Load(ShaderStageSelection selection, ShaderAssetReader read)
    {
        long started = Stopwatch.GetTimestamp();
        var stage = selection.Stage;
        var constants = selection.Specializations.Select(s => new GpuShaderModule.SpirvSpecializationConstant(
            unchecked((int)s.Id), unchecked((int)s.Value.Bits))).ToArray();
        ReadOnlySpan<byte> binary = read(selection.BinaryPath);
        double readMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!GpuShaderModule.TryLoadSpirv(ToShaderType(stage.Kind), binary, stage.EntryPoint, constants,
            out var module, out string error, stage.Identity) || module == null)
            throw new InvalidOperationException($"SPIR-V load/specialization failed for stage '{stage.Identity}', binary '{selection.BinaryPath}': {error}");
        using (module)
        {
            LastTiming = new(stage.Identity, readMilliseconds, GpuShaderModule.LastBinaryLoadMilliseconds, GpuShaderModule.LastSpecializeMilliseconds);
            return ((int)module.Detach(), stage.Bindings);
        }
    }

    /// <summary>Maps explicit contract kinds to their graphics API representation without filename inference.</summary>
    public static ShaderType ToShaderType(ShaderStageKind kind) => kind switch
    {
        ShaderStageKind.Vertex => ShaderType.VertexShader, ShaderStageKind.Fragment => ShaderType.FragmentShader,
        ShaderStageKind.Geometry => ShaderType.GeometryShader, ShaderStageKind.TessellationControl => ShaderType.TessControlShader,
        ShaderStageKind.TessellationEvaluation => ShaderType.TessEvaluationShader, ShaderStageKind.Compute => ShaderType.ComputeShader,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    #endregion

    /// <summary>Most recent render-thread loading observation; excludes linking and drawing.</summary>
    public static SpirvLoadTiming? LastTiming { get; private set; }
}
