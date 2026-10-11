using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Prepares binary graphics generations and transfers ownership only after successful linking and interface preparation.</summary>
public abstract partial class GpuProgram
{

    #region Binary linking

    /// <summary>Prepares every stage from one plan, preserving the installed program and interface on failure.</summary>
    private bool CompileSpirv(ShaderLoadPlan plan, ulong assets)
    {
        if (capi == null) throw new InvalidOperationException("Shader API is unavailable.");
#if DEBUG
        using var errors = new GlDebug.ErrorScope($"{ShaderName}: SPIR-V graphics loading/linking");
#endif
        var context = Integration.RenderContextRegistry.Current();
        string domain = string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain;
        ReadOnlySpan<byte> Read(string path) => capi.Assets.TryGet(AssetLocation.Create("shaders/" + path, domain), loadAsset: true)?.Data
            ?? throw new InvalidOperationException("Missing built shader asset: " + domain + ":shaders/" + path);
        var stages = new List<(ShaderStageKind Kind, int Shader, GpuBindingContract Contract)>();
        int program = 0;
        try
        {
            using var pending = ShaderLinkBatch.Take(capi.Assets, domain, plan);
            var inputs = pending?.Inputs ?? DriverProgramCache.Prepare(plan, Read, () => ShaderDigestIndexCache.ForAssets(capi.Assets, domain));
            // Synchronous asset callbacks must not allocate or transfer names on a changed context.
            if (context != Integration.RenderContextRegistry.Current())
                throw new InvalidOperationException("Preparation requires its originating native context generation.");
            program = pending?.DetachProgram() ?? GL.CreateProgram();
            bool cached = pending?.Cached ?? DriverProgramCache.TryLoad(program, inputs);
            LastSpirvLinkMilliseconds = pending?.LinkSubmissionMilliseconds ?? 0;
            if (!cached && pending == null)
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
            var graphicsInterface = new GraphicsExecutableInterface(program, plan, inputs.Interface);
            GraphicsInterfaceValidation.ValidateDeclarations(graphicsInterface);
            layout.BinaryInterface = new GpuProgramInterface(program, plan.Stages.Select(stage => stage.Stage.Bindings),
                graphicsInterface);
            layout.ApplyContract(program, LayoutWarn);
#if DEBUG
            layout.ValidateContract(program, LayoutWarn);
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} active interface and bindings");
#endif
            var graphicsIdentity = new ShaderPipelineIdentity(domain, plan);
            if (!cached) DriverProgramCache.Save(program, inputs);
            // Linked stages are temporary resources, including every graphics stage kind.
            foreach (var stage in stages)
            {
                GL.DetachShader(program, stage.Shader);
                GL.DeleteShader(stage.Shader);
            }
            stages.Clear();
            // A configuration edit can arrive while the driver works. Never publish superseded inputs.
            lock (settingsLock)
            {
                if (lifetime.IsRetired || !RequestedPlan.SameInputs(plan) || assets != assetGeneration ||
                    context != Integration.RenderContextRegistry.Current())
                {
                    PreparationFailure = new InvalidOperationException("Preparation was superseded by retirement, changed settings, assets or context.");
                    return false;
                }
                var candidate = GpuProgramObject.Adopt(program);
                program = 0;
                var previous = executable;
                executable = candidate;
                ProgramLayout.InstallCandidate(layout);
                installedPlan = plan;
                GraphicsIdentity = graphicsIdentity;
                ExecutableContext = context;
                ExecutableRevision++;
                InstalledAssetGeneration = assets;
                PreparationFailure = null;
                CompletePreparation();
                previous?.Dispose();
                return true;
            }
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
            candidate = ShaderProgramLink.Submit(stages.Select(stage => stage.Shader).ToArray(), retrievableBinary, out linkMilliseconds);
            if (!ShaderProgramLink.Validate(candidate, out infoLog)) return false;
            // The caller retains temporary stages for retirement and owns the successfully linked candidate.
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
