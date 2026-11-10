using System;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Loads baseline compute sources without optional SPIR-V driver ingestion.</summary>
internal sealed partial class ComputeShaderAsset
{
    #region Internal API
    /// <summary>Creates a GLSL executable after applying fixed defines through the existing source preprocessor.</summary>
    internal static ComputeShaderAsset CreateBaselineFromAssets(ICoreAPI api, ShaderSettings settings,
        GpuProgramLayout layout, string label)
    {
        var plan = new ShaderLoadPlan(settings);
        if (plan.Stages.Count != 1) throw new ArgumentException("Baseline compute requires exactly one stage.", nameof(settings));
        var stage = plan.Stages[0].Stage;
        if (stage.Kind != ShaderStageKind.Compute || stage.Structural.Count != 0 || stage.Specializations.Count != 0
            || stage.EntryPoint != "main")
            throw new ArgumentException("Baseline source has unapplied structural or specialization options.", nameof(settings));
        int extension = stage.Source.LastIndexOf('.');
        if (extension <= 0) throw new ArgumentException("Baseline stage requires a source extension.", nameof(settings));
        // Generated contracts always carry VGE_SPIRV_BUILD even for sources without configurable options.
        // Use the same preprocessing owner and fixed macro spellings as the offline catalog, not a second injector.
        var fixedDefines = stage.FixedDefines.ToDictionary(pair => pair.Key, pair => (string?)pair.Value.MacroLiteral);
        string source = ShaderSourceCode.Load(api, stage.Source[..extension], stage.Source[(extension + 1)..], fixedDefines).EmittedSource;
        var context = RHICommandContext.Current;
        var owner = new ComputeShaderAsset(layout);
        try
        {
            // The same generated interface is validated by the offline catalog and the RHI driver reflection.
            owner.shader = context.CreateShader(new(ShaderKind.Compute,
                [new RHIShaderStageDescription(ShaderStage.Compute, source)], stage.Bindings, label: label)).RequireResource();
            owner.pipeline = context.CreateComputePipeline(new(owner.shader, label)).RequireResource();
            owner.ProgramLayout.RegisterContract(stage.Bindings);
            owner.ProgramLayout.InstallFixedExecutable(owner.shader);
            owner.InstalledSettings = settings;
            owner.ExecutableContext = Integration.RenderContextRegistry.Current();
            owner.ExecutableRevision = 1;
            owner.RenderingLifetime = api is ICoreClientAPI client ? Integration.ClientRenderingLifetime.Find(client) : null;
            owner.RebuildDiagnostic = () => owner.preparationLog;
            owner.Rebuild = () =>
            {
                try { return CreateBaselineFromAssets(api, settings, layout, label); }
                catch (Exception error) { owner.preparationLog = error.Message; return null; }
            };
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }
    #endregion
}
