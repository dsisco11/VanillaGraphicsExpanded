using VanillaGraphicsExpanded.Rendering.Contracts;
using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn Upsample pass.
/// Bilateral upsamples half-res indirect diffuse to full resolution.
/// </summary>
[ShaderProgram("Contract", "lumon_upsample", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_upsample.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_upsample.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Upsample")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(DenoiseEnabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(HoleFillEnabled))]
public partial class LumOnUpsampleShaderProgram : LumOnShaderProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    private LumOnUpsampleParamsUbo? paramsUbo;

    public LumOnUpsampleShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    private LumOnUpsampleParamsUbo Params => paramsUbo ??= new LumOnUpsampleParamsUbo();

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnUpsampleShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram(Contract.Identity, instance);
    }

    #endregion

    // Per-frame state (matrices, sizes, zNear/zFar) is provided via LumOnFrameUBO.

    #region Texture Samplers

    /// <summary>
    /// Half-resolution indirect diffuse texture.
    /// </summary>
    public GpuTexture? IndirectHalf { set => BindTexture2D("indirectHalf", value, 0); }

    /// <summary>
    /// Primary depth texture for edge-aware upsampling.
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 1, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-buffer normals for edge-aware upsampling.
    /// </summary>
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 2, GpuSamplers.NearestClamp); }

    #endregion

    #region Quality Uniforms / Defines

    /// <summary>
    /// Whether edge-aware denoising is enabled.
    /// Compile-time define for better performance.
    /// </summary>
    /// <summary>Gets or sets the declared UpsampleDenoise shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.UpsampleDenoise))]
    public partial bool DenoiseEnabled { get; set; }

    /// <summary>
    /// Depth similarity sigma for bilateral upsample.
    /// Controls how strictly depth differences affect upsampling.
    /// Default: 0.1 (from SPG-008 spec Section 3.1)
    /// </summary>
    public float UpsampleDepthSigma
    {
        set
        {
            Params.UpsampleDepthSigma = value;
            Params.BindTo(this, LumOnUpsampleParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Normal similarity power for bilateral upsample.
    /// Controls how strictly normal differences affect upsampling.
    /// Default: 16.0 (from SPG-008 spec Section 3.1)
    /// </summary>
    public float UpsampleNormalSigma
    {
        set
        {
            Params.UpsampleNormalSigma = value;
            Params.BindTo(this, LumOnUpsampleParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Spatial kernel sigma for optional spatial denoise.
    /// Controls blur radius of spatial filter.
    /// Default: 1.0 (from SPG-008 spec Section 3.1)
    /// </summary>
    public float UpsampleSpatialSigma
    {
        set
        {
            Params.UpsampleSpatialSigma = value;
            Params.BindTo(this, LumOnUpsampleParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion

    #region Hole Fill Uniforms / Defines

    /// <summary>
    /// Whether low-confidence hole filling is enabled.
    /// Compile-time define for better performance.
    /// </summary>
    /// <summary>Gets or sets the declared UpsampleHoleFill shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.UpsampleHoleFill))]
    public partial bool HoleFillEnabled { get; set; }

    /// <summary>
    /// Neighborhood radius in half-res pixels used for hole filling.
    /// Kept as uniform since it controls loop iteration bounds at runtime.
    /// </summary>
    public int HoleFillRadius
    {
        set
        {
            Params.HoleFillRadius = value;
            Params.BindTo(this, LumOnUpsampleParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Minimum confidence (alpha) required for a neighbor sample to contribute to hole filling.
    /// </summary>
    public float HoleFillMinConfidence
    {
        set
        {
            Params.HoleFillMinConfidence = value;
            Params.BindTo(this, LumOnUpsampleParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion
}
