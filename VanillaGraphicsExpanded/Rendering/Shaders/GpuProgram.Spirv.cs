using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Spirv;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns binary graphics linking while retaining the engine's program registration and stage disposal contract.</summary>
public abstract partial class GpuProgram
{
    // Engine disposal is sealed and leaves handle fields intact. Its private flag is the
    // authoritative lifetime marker; GL.IsProgram cannot distinguish recycled object IDs.
    private static readonly HarmonyLib.AccessTools.FieldRef<Vintagestory.Client.NoObf.ShaderProgramBase, bool> EngineDisposed =
        HarmonyLib.AccessTools.FieldRefAccess<Vintagestory.Client.NoObf.ShaderProgramBase, bool>("disposed");

    #region Binary linking
    /// <summary>Routes engine-triggered reloads through the same strict binary loader as explicit reloads.</summary>
    public override bool Compile() => CompileAndLink();

    /// <summary>Loads all stages before replacing a working program; failure releases only newly allocated handles.</summary>
    private bool CompileSpirv(IReadOnlyDictionary<string, string?> configuration)
    {
        if (capi == null) throw new InvalidOperationException("Shader API is unavailable.");
#if DEBUG
        using var errors = new GlDebug.ErrorScope($"{ShaderName}: SPIR-V graphics loading/linking" );
#endif
        if (Disposed)
        {
            // Forget the disposed generation before allocating anything that can reuse its IDs.
            ProgramId = 0;
            if (VertexShader != null) VertexShader.ShaderId = 0;
            if (FragmentShader != null) FragmentShader.ShaderId = 0;
            if (GeometryShader != null) GeometryShader.ShaderId = 0;
            ProgramLayout.BinaryInterface = null;
            ProgramLayout.RebuildCache(0);
            uniformLocations.Clear();
            uniformLocationCache.Clear();
            uniformLocationCacheProgramId = 0;
        }
        string domain = string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain;
        ReadOnlySpan<byte> Read(string path) => capi.Assets.TryGet(AssetLocation.Create("shaders/" + path, domain), loadAsset: true)?.Data
            ?? throw new InvalidOperationException("Missing built shader asset: " + domain + ":shaders/" + path);
        var stages = new List<(int Shader, VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingContract Contract)>();
        int program = 0;
        try
        {
            stages.Add(SpirvStageLoader.Load(VertexStageShaderName + ".vsh", ShaderType.VertexShader, configuration, Read));
            stages.Add(SpirvStageLoader.Load(FragmentStageShaderName + ".fsh", ShaderType.FragmentShader, configuration, Read));
            if (geometryStage.EmittedSource != null)
                stages.Add(SpirvStageLoader.Load(GeometryStageShaderName + ".gsh", ShaderType.GeometryShader, configuration, Read));
            program = GL.CreateProgram();
            foreach (var stage in stages) GL.AttachShader(program, stage.Shader);
            long start = Stopwatch.GetTimestamp(); GL.LinkProgram(program);
            LastSpirvLinkMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException("SPIR-V link failed: " + GL.GetProgramInfoLog(program));
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} create/attach/link/status" );
#endif
            var binaryInterface = new GpuProgramInterface(program, stages.Select(stage => stage.Contract));
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} numeric interface reflection" );
#endif
            // The engine owns stage IDs after this transfer; discard the old generation only after a successful link.
            if (ProgramId != 0) GL.DeleteProgram(ProgramId);
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: glDeleteProgram previous={ProgramId}" );
#endif
            if (VertexShader?.ShaderId is > 0) GL.DeleteShader(VertexShader.ShaderId);
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: glDeleteShader previous vertex={VertexShader?.ShaderId}" );
#endif
            if (FragmentShader?.ShaderId is > 0) GL.DeleteShader(FragmentShader.ShaderId);
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: glDeleteShader previous fragment={FragmentShader?.ShaderId}" );
#endif
            if (GeometryShader?.ShaderId is > 0) GL.DeleteShader(GeometryShader.ShaderId);
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: glDeleteShader previous geometry={GeometryShader?.ShaderId}" );
#endif
            ProgramId = program; program = 0;
            ProgramLayout.BinaryInterface = binaryInterface;
            VertexShader!.ShaderId = stages[0].Shader; FragmentShader!.ShaderId = stages[1].Shader;
            if (stages.Count == 3) GeometryShader!.ShaderId = stages[2].Shader;
            else GeometryShader = null;
            stages.Clear();
            uniformLocations.Clear();
            foreach (string source in new[] { vertexStage.EmittedSource, fragmentStage.EmittedSource, geometryStage.EmittedSource }.OfType<string>())
                foreach (Match match in Regex.Matches(source, @"\buniform\s+\w+\s+(\w+)")) uniformLocations[match.Groups[1].Value] = -1;
            foreach (var pair in binaryInterface.Uniforms) uniformLocations[pair.Key] = pair.Value;
            // Binary linking bypasses the engine platform linker, so publish its lifetime state too.
            EngineDisposed(this) = false;
            return true;
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            foreach (var stage in stages) GL.DeleteShader(stage.Shader);
        }
    }

    /// <summary>Last driver linking duration, excluding binary loading, specialization and draws.</summary>
    internal double LastSpirvLinkMilliseconds { get; private set; }
    #endregion
}


