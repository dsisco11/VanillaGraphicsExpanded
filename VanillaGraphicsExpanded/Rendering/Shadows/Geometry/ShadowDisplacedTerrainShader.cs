using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns packaged native terrain displacement with virtual projection after tessellation evaluation.</summary>
[ShaderProgram("Contract", "shadows/terrain-adaptive", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "shadows/terrain-adaptive.vsh")]
[ShaderStage("Contract", ShaderStageKind.TessellationControl, "shadows/terrain-adaptive.tcsh")]
[ShaderStage("Contract", ShaderStageKind.TessellationEvaluation, "shadows/terrain-adaptive.tesh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "shadows/terrain.fsh")]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Storage))]
[ShaderUse("Contract", ShaderStageKind.TessellationControl, nameof(Storage))]
[ShaderUse("Contract", ShaderStageKind.TessellationEvaluation, nameof(Storage))]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Waving))]
[ShaderUse("Contract", ShaderStageKind.TessellationControl, nameof(Waving))]
[ShaderUse("Contract", ShaderStageKind.TessellationEvaluation, nameof(Waving))]
internal sealed partial class ShadowDisplacedTerrainShader : ShadowNativeMaterialShader, IShadowDisplacedTerrainBindings
{
    private readonly ShadowSubdivisionParameters subdivision = new();
    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <inheritdoc />
    internal override bool PackedLayout => Storage != 0;
    /// <inheritdoc />
    internal override PrimitiveTopology Topology => PrimitiveTopology.Patches;
    /// <summary>Reports whether a complete subdivision material has been captured for submission.</summary>
    internal bool HasSubdivision { get; private set; }

    /// <summary>Selects the native ordinary or packed vertex declaration.</summary>
    [ShaderOption("VGE_TERRAIN_PACKED_INPUT", 0, Domain = new object[] { 0, 1 })]
    public partial int Storage { get; set; }
    /// <summary>Selects the native surface deformation policy.</summary>
    [ShaderOption("VGE_VERTEX_DEFORMATION_ENABLED", 1, Domain = new object[] { 0, 1 })]
    public partial int Waving { get; set; }

    /// <summary>Loads matching precompiled native vertex, control, evaluation and cutout stages.</summary>
    internal static ShadowDisplacedTerrainShader Create(ICoreAPI api, bool storage, bool waving)
    {
        var shader = new ShadowDisplacedTerrainShader { PassName = Contract.Identity, AssetDomain = Constants.ModId };
        try
        {
            shader.SetShaderOptions(options =>
            {
                options.Set(StorageOption, storage ? 1 : 0);
                options.Set(WavingOption, waving ? 1 : 0);
            });
            shader.Initialize(EngineShaderAssets.Create(api.Assets, Constants.ModId));
            if (!shader.CompileAndLink()) throw new InvalidOperationException("Packaged displaced terrain shadow variant could not be prepared.");
            return shader;
        }
        catch { shader.Dispose(); throw; }
    }

    /// <summary>Freezes existing native material textures and bounded subdivision policy before binding capture.</summary>
    internal void CaptureSubdivision(float edgePixels, float maximumLevel, float fadeStart, float fadeEnd,
        float focalPixels, bool enabled, GpuTextureRef indices, GpuTextureRef records, GpuTextureRef height)
    {
        RequireInputMutation();
        if (!indices.IsValid || !records.IsValid || !height.IsValid)
            throw new ArgumentException("Displacement requires current textures and finite native subdivision inputs.");
        ShadowSubdivisionParameters.Validate(edgePixels, maximumLevel, fadeStart, fadeEnd, focalPixels);
        var bindings = (IShadowDisplacedTerrainBindings)this;
        bindings.DisplacementIndices = indices; bindings.DisplacementRecords = records; bindings.NormalDepth = height;
        subdivision.SetValidated(edgePixels, maximumLevel, fadeStart, fadeEnd, focalPixels, enabled); HasSubdivision = true;
    }
    #endregion

    #region Private
    /// <summary>Registers complete stage interfaces and retains logical subdivision storage.</summary>
    private ShadowDisplacedTerrainShader()
    {
        foreach (var stage in Contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
        OwnUniformBuffer(subdivision);
    }
    /// <summary>Publishes the native vertex material snapshot.</summary>
    CpuUniformBuffer IShadowTerrainBindings.Parameters => MaterialParameters;
    /// <summary>Publishes the frozen subdivision policy through generated resource capture.</summary>
    CpuUniformBuffer IShadowDisplacedTerrainBindings.SubdivisionParameters => subdivision;
    #endregion
}
