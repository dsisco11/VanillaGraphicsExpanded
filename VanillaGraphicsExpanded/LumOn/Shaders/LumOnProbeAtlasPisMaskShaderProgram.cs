using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering.Contracts;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn probe-resolution PIS mask pass.
/// Writes a per-probe 64-bit mask (packed into RG32F) selecting which atlas texels to trace.
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_pis_mask", 8)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_trace.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_pis_mask.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Pis")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "PisMask")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(TexelsPerFrame), SpecializationId = 1)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(BatchSlicing))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(ExploreCount), SpecializationId = 8, When = "ImportanceSampling && !BatchSlicing && !UniformMask")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(ExploreFraction), SpecializationId = 9, When = "ImportanceSampling && !BatchSlicing && !UniformMask")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(ImportanceSampling))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(MinConfidenceWeight), SpecializationId = 7)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(UniformMask))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WeightEpsilon), SpecializationId = 10, When = "ImportanceSampling && !BatchSlicing && !UniformMask")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
[ShaderBindingSet(typeof(LumOnScreenProbeAtlasTraceShaderProgram), Stages = new[] { ShaderStageKind.Vertex })]
public sealed partial class LumOnProbeAtlasPisMaskShaderProgram : LumOnShaderProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the fragment stage's frame parameter block.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer LumOnFrame { set; }
    /// <summary>Declares the fragment stage's anchor position sampler.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment)]
    private partial GpuTexture ProbeAnchorPositionTexture { set; }
    /// <summary>Declares the fragment stage's anchor normal sampler.</summary>
    [ShaderBinding("probeAnchorNormal", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
    private partial GpuTexture ProbeAnchorNormalTexture { set; }
    /// <summary>Declares the fragment stage's history radiance sampler.</summary>
    [ShaderBinding("octahedralHistory", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment)]
    private partial GpuTexture OctahedralHistory { set; }
    /// <summary>Declares the fragment stage's history metadata sampler.</summary>
    [ShaderBinding("probeAtlasMetaHistory", ShaderBindingKind.Sampler, 3, ShaderStageKind.Fragment)]
    private partial GpuTexture ProbeAtlasMetaHistory { set; }
    #endregion

    #region Shader options
    /// <summary>Gets or sets the declared BatchSlicing shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.BatchSlicing))]
    public partial bool BatchSlicing { get; set; }

    /// <summary>Gets or sets the declared ExploreCount shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ExploreCount))]
    public partial int ExploreCount { get; set; }

    /// <summary>Gets or sets the declared ExploreFraction shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ExploreFraction))]
    public partial float ExploreFraction { get; set; }

    /// <summary>Gets or sets the declared ImportanceSampling shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ImportanceSampling))]
    public partial bool ImportanceSampling { get; set; }

    /// <summary>Gets or sets the declared MinConfidenceWeight shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.MinConfidenceWeight))]
    public partial float MinConfidenceWeight { get; set; }

    /// <summary>Gets or sets the declared UniformMask shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.UniformMask))]
    public partial bool UniformMask { get; set; }

    /// <summary>Gets or sets the declared WeightEpsilon shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WeightEpsilon))]
    public partial float WeightEpsilon { get; set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    public LumOnProbeAtlasPisMaskShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }



    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeAtlasPisMaskShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };

        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Texture Samplers

    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 0); }

    public GpuTexture? ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 1); }

    public GpuTexture? ScreenProbeAtlasHistory { set => BindTexture2D("octahedralHistory", value, 2); }

    public GpuTexture? ScreenProbeAtlasMetaHistory { set => BindTexture2D("probeAtlasMetaHistory", value, 3); }

    #endregion

    #region Temporal Distribution Defines

    /// <summary>Gets or sets the declared AtlasTexelsPerFrame shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AtlasTexelsPerFrame))]
    public partial int TexelsPerFrame { get; set; }

    #endregion

    #region Product Importance Sampling Defines (Phase 10)

    public bool EnsureProbePisDefines(
        bool enabled,
        float exploreFraction,
        int exploreCount,
        float minConfidenceWeight,
        float weightEpsilon,
        bool forceUniformMask,
        bool forceBatchSlicing)
    {
        bool changed = SetShaderOptions(options =>
        {
            options.Set(LumOnShaderOptions.ImportanceSampling, enabled);
            options.Set(LumOnShaderOptions.ExploreFraction, exploreFraction);
            options.Set(LumOnShaderOptions.ExploreCount, exploreCount);
            options.Set(LumOnShaderOptions.MinConfidenceWeight, minConfidenceWeight);
            options.Set(LumOnShaderOptions.WeightEpsilon, weightEpsilon);
            options.Set(LumOnShaderOptions.UniformMask, forceUniformMask);
            options.Set(LumOnShaderOptions.BatchSlicing, forceBatchSlicing);
        });
        return !changed;
    }

    #endregion
}
