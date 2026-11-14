using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns packaged skinned native material executables and immutable animation snapshots.</summary>
[ShaderProgram("Contract", "shadows/entity", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "shadows/entity.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "shadows/entity.fsh")]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Waving))]
internal sealed partial class ShadowEntityShader : GpuProgram, IShadowEntityBindings
{
    private readonly ShadowMaterialParameters parameters = new();
    private readonly ShadowAnimationParameters animation = new();
    private readonly ShadowObjectParameters flags = new();

    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <summary>Uses the native surface material's deformation switch.</summary>
    [ShaderOption("VGE_VERTEX_DEFORMATION_ENABLED", 1, Domain = new object[] { 0, 1 })]
    public partial int Waving { get; set; }

    /// <summary>Copies a complete finite material and palette snapshot before generated resource capture.</summary>
    internal void CaptureMaterial(ReadOnlySpan<byte> material, ReadOnlySpan<float> joints, int renderFlags)
    {
        // Validate every participating block before committing any part of the entity inputs.
        ShadowMaterialParameters.Validate(material);
        ShadowAnimationParameters.Validate(joints);
        RequireInputMutation();
        parameters.SetValidatedMaterial(material);
        animation.SetValidatedPalette(joints);
        flags.SetFlags(renderFlags);
    }

    /// <summary>Loads only a precompiled material variant, preserving the production SPIR-V loading requirement.</summary>
    internal static ShadowEntityShader Create(ICoreAPI api, bool waving)
    {
        var shader = new ShadowEntityShader { PassName = Contract.Identity, AssetDomain = Constants.ModId };
        try
        {
            shader.SetShaderOptions(options => options.Set(WavingOption, waving ? 1 : 0));
            shader.Initialize(EngineShaderAssets.Create(api.Assets, Constants.ModId));
            if (!shader.CompileAndLink()) throw new InvalidOperationException("Packaged native entity shadow variant could not be prepared.");
            return shader;
        }
        catch { shader.Dispose(); throw; }
    }
    #endregion

    #region Private
    /// <summary>Registers the generated resource interface and owns logical versions until their RHI consumers retire.</summary>
    private ShadowEntityShader()
    {
        foreach (var stage in Contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
        OwnUniformBuffer(parameters); OwnUniformBuffer(animation); OwnUniformBuffer(flags);
    }

    /// <inheritdoc />
    CpuUniformBuffer IShadowTerrainBindings.Parameters => parameters;
    /// <inheritdoc />
    CpuUniformBuffer IShadowEntityBindings.Animation => animation;
    /// <inheritdoc />
    CpuUniformBuffer IShadowEntityBindings.Flags => flags;
    #endregion
}
