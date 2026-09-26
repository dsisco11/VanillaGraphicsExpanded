using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
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
            var inputs = DriverProgramCache.Prepare(plan, Read, () => ShaderDigestIndexCache.ForAssets(capi.Assets, domain));
            program = GL.CreateProgram();
            bool cached = DriverProgramCache.TryLoad(program, inputs);
            LastSpirvLinkMilliseconds = 0;
            if (!cached)
            {
                // A rejected binary candidate is never reused for normal linking.
                GL.DeleteProgram(program);
                program = 0;
                foreach (var selection in plan.Stages)
                {
                    var loaded = SpirvStageLoader.Load(selection, inputs.Read);
                    stages.Add((selection.Stage.Kind, loaded.Shader, loaded.Contract));
                }
                if (!TryCreate(stages, out program, out string infoLog, out double linkMilliseconds, inputs.Key != null))
                    throw new InvalidOperationException("SPIR-V link failed: " + infoLog);
                LastSpirvLinkMilliseconds = linkMilliseconds;
            }
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} create/attach/link/status");
#endif
            var layout = ProgramLayout.CreateCandidate();
            layout.BinaryInterface = new GpuProgramInterface(program, plan.Stages.Select(stage => stage.Stage.Bindings));
            layout.ApplyContract(program, LayoutWarn);
#if DEBUG
            layout.ValidateContract(program, LayoutWarn);
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} active interface and bindings");
#endif
            // Contracts include inactive uniforms; source-derived placeholder names are unnecessary.
            var locations = layout.BinaryInterface.Uniforms;
            if (!cached) DriverProgramCache.Save(program, inputs);

            // Stages unsupported by the engine's slots can be deleted after linking; the executable retains them.
            foreach (var stage in stages.Where(s => s.Kind is ShaderStageKind.TessellationControl or ShaderStageKind.TessellationEvaluation))
            {
                GL.DetachShader(program, stage.Shader);
                GL.DeleteShader(stage.Shader);
            }
            stages.RemoveAll(s => s.Kind is ShaderStageKind.TessellationControl or ShaderStageKind.TessellationEvaluation);
            // Prepare the optional engine slot before committing the candidate generation.
            var geometry = stages.FirstOrDefault(s => s.Kind == ShaderStageKind.Geometry);
            var geometrySlot = geometry.Shader == 0 ? null : GeometryShader ??
                (Vintagestory.Client.NoObf.Shader)capi.Shader.NewShader(Vintagestory.API.Client.EnumShaderType.GeometryShader);
            var vertexSlot = cached ? null : VertexShader ??
                (Vintagestory.Client.NoObf.Shader)capi.Shader.NewShader(Vintagestory.API.Client.EnumShaderType.VertexShader);
            var fragmentSlot = cached ? null : FragmentShader ??
                (Vintagestory.Client.NoObf.Shader)capi.Shader.NewShader(Vintagestory.API.Client.EnumShaderType.FragmentShader);
            int oldProgram = ProgramId;
            int[] oldStages = [VertexShader?.ShaderId ?? 0, FragmentShader?.ShaderId ?? 0, GeometryShader?.ShaderId ?? 0];
            ProgramId = program; program = 0;
            ProgramLayout.InstallCandidate(layout);
            // Engine disposal detaches every non-null stage. Cached executables have no attached stages.
            VertexShader = vertexSlot;
            FragmentShader = fragmentSlot;
            if (vertexSlot != null) vertexSlot.ShaderId = stages.Single(s => s.Kind == ShaderStageKind.Vertex).Shader;
            if (fragmentSlot != null) fragmentSlot.ShaderId = stages.Single(s => s.Kind == ShaderStageKind.Fragment).Shader;
            GeometryShader = geometrySlot;
            if (geometrySlot != null) geometrySlot.ShaderId = geometry.Shader;
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

    /// <summary>Links caller-owned graphics stages, transferring the program only after a successful link.</summary>
    private static bool TryCreate(IReadOnlyList<(ShaderStageKind Kind, int Shader, GpuBindingContract Contract)> stages,
        out int program, out string infoLog, out double linkMilliseconds, bool retrievableBinary = false)
    {
        program = 0;
        infoLog = string.Empty;
        linkMilliseconds = 0;
        int candidate = 0;
        try
        {
            candidate = GL.CreateProgram();
            if (candidate == 0) throw new InvalidOperationException("glCreateProgram returned 0.");
            DriverProgramCache.RequestRetrievable(candidate, retrievableBinary);
            foreach (var stage in stages) GL.AttachShader(candidate, stage.Shader);
            long started = Stopwatch.GetTimestamp();
            GL.LinkProgram(candidate);
            linkMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            GL.GetProgram(candidate, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0)
            {
                infoLog = GL.GetProgramInfoLog(candidate) ?? string.Empty;
                return false;
            }
            // The caller retains stages for engine disposal and owns the successfully linked candidate.
            program = candidate;
            candidate = 0;
            return true;
        }
        catch (Exception ex)
        {
            infoLog = (infoLog.Length == 0 ? string.Empty : infoLog + "\n") + ex.Message;
            return false;
        }
        finally { if (candidate != 0) GL.DeleteProgram(candidate); }
    }

    /// <summary>Last driver linking duration, excluding binary loading, specialization and draws; zero for executable cache hits.</summary>
    internal double LastSpirvLinkMilliseconds { get; private set; }
    #endregion
}
