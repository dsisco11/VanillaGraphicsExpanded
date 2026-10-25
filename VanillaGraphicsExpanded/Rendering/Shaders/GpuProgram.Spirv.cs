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
namespace VanillaGraphicsExpanded.Rendering.Shaders;
/// <summary>Prepares binary graphics generations and transfers ownership only after successful linking and interface preparation.</summary>
public abstract partial class GpuProgram
{

    #region Binary linking

    /// <summary>Prepares every stage from one plan, preserving the installed program and interface on failure.</summary>
    private bool CompileSpirv(ShaderLoadPlan plan, ulong assets)
    {
        if (assetSource == null) throw new InvalidOperationException("Shader API is unavailable.");
#if DEBUG
        using var errors = new GlDebug.ErrorScope($"{ShaderName}: SPIR-V graphics loading/linking");
#endif
        var context = Integration.RenderContextRegistry.Current();
        string domain = string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain;

        var stages = new List<(ShaderStageKind Kind, int Shader, GpuBindingContract Contract, GpuShaderModule Owner)>();
        GpuProgramObject? candidate = null;
        int program = 0;
        try
        {
            using var pending = ShaderLinkBatch.Take(assetSource.Identity, domain, plan);
            var inputs = pending?.Inputs ?? DriverProgramCache.Prepare(plan, assetSource.Read, assetSource.Digest);
            // Synchronous asset callbacks must not allocate or transfer names on a changed context.
            if (context != Integration.RenderContextRegistry.Current())
                throw new InvalidOperationException("Preparation requires its originating native context generation.");
            candidate = pending?.TakeProgram() ?? GpuProgramObject.Create();
            program = candidate.ProgramId;
            bool cached = pending?.Cached ?? DriverProgramCache.TryLoad(candidate, inputs);
#if DEBUG
            LastSpirvLinkMilliseconds = pending?.LinkSubmissionMilliseconds ?? 0;
#endif
            if (!cached && pending == null)
            {
                // A rejected binary candidate is never reused for normal linking.
                candidate.Dispose();
                candidate = null;
                program = 0;
                foreach (var selection in plan.Stages)
                {
                    var loaded = SpirvStageLoader.Load(selection, inputs.Read);
                    stages.Add((selection.Stage.Kind, loaded.ShaderId, selection.Stage.Bindings, loaded));
                }
                if (!TryCreate(stages, out candidate, out string infoLog,
#if DEBUG
                    out double linkMilliseconds,
#endif
                    inputs.Key != null))
                    throw new InvalidOperationException("SPIR-V link failed: " + infoLog);
                program = candidate!.ProgramId;
#if DEBUG
                LastSpirvLinkMilliseconds = linkMilliseconds;
#endif
            }
#if DEBUG
            GlDebug.ThrowIfErrors($"{ShaderName}: program {program} create/attach/link/status");
#endif
            var layout = ProgramLayout.CreateCandidate();
            var graphicsInterface = new GraphicsExecutableInterface(program, plan, inputs.Interface);
            GraphicsInterfaceValidation.ValidateDeclarations(graphicsInterface);
            layout.BinaryInterface = new GpuProgramInterface(program, plan.Stages.Select(stage => stage.Stage.Bindings),
                graphicsInterface, GpuResourceManager.RequireProgramSnapshot(candidate));
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
                stage.Owner.Dispose();
            }
            stages.Clear();
            // A configuration edit can arrive while the driver works. Never publish superseded inputs.
            lock (settingsLock)
            {
                if (lifetime.IsRetired || PreparationAdmission?.Invoke() == false || !RequestedPlan.SameInputs(plan) || assets != assetGeneration ||
                    context != Integration.RenderContextRegistry.Current())
                {
                    PreparationFailure = new InvalidOperationException("Preparation was superseded by retirement, changed settings, assets or context.");
                    return false;
                }
                if (candidate!.ResourceManager?.GetResourceRegistration(candidate)
                    is GpuResourceMetadataEntry<GpuProgramMetadata> { MetadataAvailable: true } entry)
                    layout.UseNativeMetadata(program, entry.Metadata, entry.MetadataRevision);
                program = 0;
                var previous = executable;
                executable = candidate;
                ProgramLayout.InstallCandidate(layout);
                candidate.SetPreparedLayout(ProgramLayout);
                candidate = null;
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
            candidate?.Dispose();
            foreach (var stage in stages) stage.Owner.Dispose();
        }
    }

    /// <summary>Links caller-owned graphics stages, transferring the program only after a successful link.</summary>
    private static bool TryCreate(IReadOnlyList<(ShaderStageKind Kind, int Shader, GpuBindingContract Contract, GpuShaderModule Owner)> stages,
        out GpuProgramObject? program, out string infoLog,
#if DEBUG
        out double linkMilliseconds,
#endif
        bool retrievableBinary = false)
    {
        program = null;
        infoLog = string.Empty;
#if DEBUG
        linkMilliseconds = 0;
#endif
        GpuProgramObject? candidate = null;
        try
        {
            candidate = ShaderProgramLink.Submit(stages.Select(stage => stage.Shader).ToArray(), retrievableBinary
#if DEBUG
                , out linkMilliseconds
#endif
                );
            if (!ShaderProgramLink.Validate(candidate, out infoLog)) return false;
            // The caller retains temporary stages for retirement and owns the successfully linked candidate.
            program = candidate;
            candidate = null;
            return true;
        }
        catch (Exception ex)
        {
            infoLog = (infoLog.Length == 0 ? string.Empty : infoLog + "\n") + ex.Message;
            return false;
        }
        finally { candidate?.Dispose(); }
    }

#if DEBUG
    /// <summary>Last driver linking duration, excluding binary loading, specialization and draws; zero for executable cache hits.</summary>
    internal double LastSpirvLinkMilliseconds { get; private set; }
#endif
    #endregion
}
