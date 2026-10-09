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
public partial class LumOnUpsampleShaderProgram : LumOnShaderProgram, ILumOnUpsampleShaderProgramBindings
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnUpsampleParamsUbo? paramsUbo;


    /// <summary>Registers the upsample resource contract while leaving retained inputs at their defaults.</summary>
    public LumOnUpsampleShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Retains one packed parameter block with the shader's input-edit guard.</summary>
    private LumOnUpsampleParamsUbo Params
    {
        get
        {
            if (paramsUbo is null)
            {
                paramsUbo = new LumOnUpsampleParamsUbo();
                paramsUbo.SetWriteGuard(RequireInputMutation);
            }
            return paramsUbo;
        }
    }

    #region Static

    /// <summary>Declares the persistent upsample owner with the shader registry.</summary>
    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnUpsampleShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    // Per-frame state (matrices, sizes, zNear/zFar) is provided via LumOnFrameUBO.

    #region Texture Samplers

    /// <summary>
    /// Half-resolution indirect diffuse texture.
    /// </summary>
    public partial GpuTexture? IndirectHalf { set; }

    /// <summary>
    /// Primary depth texture for edge-aware upsampling.
    /// </summary>
    public partial int PrimaryDepth { set; }

    /// <summary>
    /// G-buffer normals for edge-aware upsampling.
    /// </summary>
    public partial GpuTexture? GBufferSurface { set; }

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
        }
    }

    #endregion
    #region Binding sources
    /// <summary>Supplies shared frame storage through the existing uniform-block contract.</summary>
    /// <summary>Reuses the shared camera snapshot rather than the effect-specific lighting buffer.</summary>
    CpuUniformBuffer ILumOnUpsampleShaderProgramBindings.FrameInputs => SharedCamera;
    GpuUniformBuffer? ILumOnUpsampleShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies packed parameters for one generated publication per use.</summary>
    CpuUniformBuffer ILumOnUpsampleShaderProgramBindings.Parameters => Params;
    #endregion
}