using System;
using System.Linq;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Prepares an immutable graphics executable and installs it only after asset and lifetime admission.</summary>
public abstract partial class GpuProgram
{
    #region Private
    /// <summary>Builds a candidate interface from captured assets while retaining the installed generation on failure.</summary>
    private bool CompileSpirv(Contracts.ShaderLoadPlan plan, ulong assets)
    {
        if (assetSource == null) throw new InvalidOperationException("Shader assets are unavailable.");
        var context = Integration.RenderContextRegistry.Current();
        string domain = string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain;
        RHIShader? candidate = null;
        try
        {
            using var preparation = ShaderLinkBatch.Take(assetSource.Identity, domain, plan)
                ?? new ShaderAssetCandidate(plan, assetSource.Read, assetSource.Digest);
            preparation.Complete();
            candidate = preparation.TakeShader();
            var graphics = candidate.Interface.Graphics ?? throw new InvalidOperationException("The candidate has no graphics interface.");
            var vertex = plan.Stages.Single(stage => stage.Stage.Kind == Contracts.ShaderStageKind.Vertex);
            var fragment = plan.Stages.SingleOrDefault(stage => stage.Stage.Kind == Contracts.ShaderStageKind.Fragment);
            GraphicsInterfaceValidation.ValidateDeclarations(graphics, preparation.Inputs.Interface(vertex).Inputs,
                fragment == null ? Array.Empty<PackagedInterfaceVariable>() : preparation.Inputs.Interface(fragment).Outputs);
            var layout = ProgramLayout.CreateCandidate();
            layout.InstallFixedExecutable(candidate);
            using var pipelines = PreparePipelineCandidates(candidate);
            // Driver completion can outlive the requested settings. Install only the still-admitted captured selection.
            lock (settingsLock)
            {
                if (lifetime.IsRetired || PreparationAdmission?.Invoke() == false || !RequestedPlan.SameInputs(plan)
                    || assets != assetGeneration || pipelines.Revision != pipelineContractRevision
                    || context != Integration.RenderContextRegistry.Current())
                {
                    PreparationFailure = new InvalidOperationException("Preparation was superseded by retirement, changed settings, assets or context.");
                    return false;
                }
                candidate.RequireUsable();
                var previous = executable;
                var previousPipelines = pipelines.Install();
                executable = candidate;
                ProgramLayout.InstallCandidate(layout);
                candidate.Program.SetPreparedLayout(ProgramLayout);
                lifetime.NativeExecutable = candidate.Program;
                candidate = null;
                installedPlan = plan;
                ExecutableContext = context;
                ExecutableRevision++;
                InstalledAssetGeneration = assets;
                PreparationFailure = null;
                CompletePreparation();
                // Retirement is post-commit cleanup, never a rejected replacement.
                try
                {
                    foreach (var pipeline in previousPipelines) pipeline.Dispose();
                    previous?.Dispose();
                }
                catch (Exception error) { assetSource.Error?.Invoke($"[VGE][{ShaderName}] Installed shader retirement failed: {error}"); }
                return true;
            }
        }
        finally { candidate?.Dispose(); }
    }
    #endregion
}
