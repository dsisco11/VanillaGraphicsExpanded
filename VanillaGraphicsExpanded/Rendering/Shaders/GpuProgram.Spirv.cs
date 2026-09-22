using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Prepares binary graphics generations and transfers ownership only after successful linking and interface preparation.</summary>
public abstract partial class GpuProgram
{
    private static readonly HarmonyLib.AccessTools.FieldRef<Vintagestory.Client.NoObf.ShaderProgramBase, bool> EngineDisposed =
        HarmonyLib.AccessTools.FieldRefAccess<Vintagestory.Client.NoObf.ShaderProgramBase, bool>("disposed");

    #region Binary linking
    /// <summary>Routes engine-triggered reloads through the same strict binary loader as explicit reloads.</summary>
    public override bool Compile() => CompileAndLink();

    /// <summary>Prepares every stage from one plan, preserving the installed program and interface on failure.</summary>
    private bool CompileSpirv(ShaderLoadPlan plan)
    {
        if (capi == null) throw new InvalidOperationException("Shader API is unavailable.");
#if DEBUG
        using var errors = new GlDebug.ErrorScope($"{ShaderName}: SPIR-V graphics loading/linking");
#endif
        if (Disposed)
        {
            // The engine leaves deleted IDs behind. Forget them before new allocations can reuse them.
            ProgramId = 0;
            if (VertexShader != null) VertexShader.ShaderId = 0;
            if (FragmentShader != null) FragmentShader.ShaderId = 0;
            if (GeometryShader != null) GeometryShader.ShaderId = 0;
            ProgramLayout.BinaryInterface = null;
            ProgramLayout.RebuildCache(0);
            uniformLocations.Clear();
            uniformLocationCache.Clear();
            uniformLocationCacheProgramId = 0;
            lock (settingsLock) installedPlan = null;
        }
        string domain = string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain;
        ReadOnlySpan<byte> Read(string path) => capi.Assets.TryGet(AssetLocation.Create("shaders/" + path, domain), loadAsset: true)?.Data
            ?? throw new InvalidOperationException("Missing built shader asset: " + domain + ":shaders/" + path);
        var stages = new List<(ShaderStageKind Kind, int Shader, GpuBindingContract Contract)>();
        int program = 0;
        try
        {
            foreach (var selection in plan.Stages)
            {
                var loaded = SpirvStageLoader.Load(selection, Read);
                stages.Add((selection.Stage.Kind, loaded.Shader, loaded.Contract));
            }
            program = GL.CreateProgram();
            foreach (var stage in stages) GL.AttachShader(program, stage.Shader);
            long start = Stopwatch.GetTimestamp();
            GL.LinkProgram(program);
            LastSpirvLinkMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException("SPIR-V link failed: " + GL.GetProgramInfoLog(program));
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} create/attach/link/status");
#endif
            var layout = ProgramLayout.CreateCandidate();
            layout.BinaryInterface = new GpuProgramInterface(program, stages.Select(stage => stage.Contract));
            layout.ApplyContract(program, LayoutWarn);
#if DEBUG
            layout.ValidateContract(program, LayoutWarn);
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} active interface and bindings");
#endif
            var locations = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var stage in stages)
            {
                string? source = stage.Kind switch
                {
                    ShaderStageKind.Vertex => vertexStage.EmittedSource,
                    ShaderStageKind.Fragment => fragmentStage.EmittedSource,
                    ShaderStageKind.Geometry => geometryStage.EmittedSource,
                    _ => null
                };
                if (source != null)
                    foreach (Match match in Regex.Matches(source, @"\buniform\s+\w+\s+(\w+)")) locations[match.Groups[1].Value] = -1;
            }
            foreach (var pair in layout.BinaryInterface.Uniforms) locations[pair.Key] = pair.Value;

            // Stages unsupported by the engine's slots can be deleted after linking; the executable retains them.
            foreach (var stage in stages.Where(s => s.Kind is ShaderStageKind.TessellationControl or ShaderStageKind.TessellationEvaluation))
            {
                GL.DetachShader(program, stage.Shader);
                GL.DeleteShader(stage.Shader);
            }
            stages.RemoveAll(s => s.Kind is ShaderStageKind.TessellationControl or ShaderStageKind.TessellationEvaluation);
            int oldProgram = ProgramId;
            int[] oldStages = [VertexShader?.ShaderId ?? 0, FragmentShader?.ShaderId ?? 0, GeometryShader?.ShaderId ?? 0];
            ProgramId = program; program = 0;
            ProgramLayout.InstallCandidate(layout);
            VertexShader!.ShaderId = stages.Single(s => s.Kind == ShaderStageKind.Vertex).Shader;
            FragmentShader!.ShaderId = stages.Single(s => s.Kind == ShaderStageKind.Fragment).Shader;
            var geometry = stages.FirstOrDefault(s => s.Kind == ShaderStageKind.Geometry);
            if (geometry.Shader != 0) GeometryShader!.ShaderId = geometry.Shader;
            else GeometryShader = null;
            stages.Clear();
            uniformLocations.Clear();
            foreach (var pair in locations) uniformLocations[pair.Key] = pair.Value;
            uniformLocationCache.Clear();
            uniformLocationCacheProgramId = 0;
            EngineDisposed(this) = false;
            lock (settingsLock) installedPlan = plan;
            if (oldProgram != 0) GL.DeleteProgram(oldProgram);
            foreach (int shader in oldStages) if (shader != 0) GL.DeleteShader(shader);
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
