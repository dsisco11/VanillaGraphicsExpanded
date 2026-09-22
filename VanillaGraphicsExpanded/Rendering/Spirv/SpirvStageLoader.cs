using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Loads contract-selected SPIR-V assets; the caller owns the returned shader handle.</summary>
internal static class SpirvStageLoader
{
    #region Loading
    /// <summary>Loads the declared variant and specializes its numeric inputs without runtime manifests.</summary>
    public static (int Shader, GpuBindingContract Contract) Load(string source, ShaderType type,
        IReadOnlyDictionary<string, string?>? defines, ShaderAssetReader read)
    {
        long started = Stopwatch.GetTimestamp();
        string extension = type switch
        {
            ShaderType.VertexShader => ".vsh", ShaderType.FragmentShader => ".fsh", ShaderType.ComputeShader => ".csh",
            ShaderType.GeometryShader => ".gsh", ShaderType.TessControlShader => ".tcsh", ShaderType.TessEvaluationShader => ".tesh",
            _ => throw new ArgumentException("Unsupported shader stage.", nameof(type))
        };
        if (!source.EndsWith(extension, StringComparison.Ordinal)) throw new ArgumentException("Shader stage mismatch.", nameof(source));
        var stage = GpuShaderContracts.CreateStage(source);
        var contract = GpuShaderContracts.Registry.FindStage(source).Bindings;
        var constants = stage.Constants(defines).Select(s => new GpuShaderModule.SpirvSpecializationConstant(s.Id,
            s.Type == "float" ? BitConverter.SingleToInt32Bits(float.Parse(LegacyShaderStageContract.Value(s.Name, s.Default, defines), CultureInfo.InvariantCulture)) :
            int.Parse(LegacyShaderStageContract.Value(s.Name, s.Default, defines), CultureInfo.InvariantCulture))).ToArray();
        // Consume the borrowed binary synchronously. No further reader calls occur before upload completes.
        ReadOnlySpan<byte> binary = read(stage.BinaryPath(source, defines));
        double readMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (!GpuShaderModule.TryLoadSpirv(type, binary, "main", constants, out var module, out string error, source) || module == null)
            throw new InvalidOperationException("SPIR-V load/specialization failed for " + source + ": " + error);
        using (module)
        {
            LastTiming = new(source, readMilliseconds, GpuShaderModule.LastBinaryLoadMilliseconds, GpuShaderModule.LastSpecializeMilliseconds);
            return ((int)module.Detach(), contract);
        }
    }
    #endregion

    /// <summary>Most recent render-thread loading observation; excludes linking and drawing.</summary>
    public static SpirvLoadTiming? LastTiming { get; private set; }
}
