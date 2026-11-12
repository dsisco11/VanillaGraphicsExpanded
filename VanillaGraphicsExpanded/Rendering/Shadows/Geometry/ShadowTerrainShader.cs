using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns packaged native terrain material variants with final-deformation virtual shadow writing.</summary>
[ShaderProgram("Contract", "shadows/terrain", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "shadows/terrain.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "shadows/terrain.fsh")]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Storage))]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Waving))]
internal sealed partial class ShadowTerrainShader : ShadowNativeMaterialShader, IShadowTerrainBindings
{
    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <inheritdoc />
    internal override bool PackedLayout => Storage != 0;
    /// <inheritdoc />
    internal override PrimitiveTopology Topology => PrimitiveTopology.Triangles;
    /// <summary>Selects the engine's ordinary or packed face-data vertex decoding.</summary>
    [ShaderOption("VGE_TERRAIN_PACKED_INPUT", 0, Domain = new object[] { 0, 1 })]
    public partial int Storage { get; set; }
    /// <summary>Selects the same native deformation switch used by surface materials.</summary>
    [ShaderOption("VGE_VERTEX_DEFORMATION_ENABLED", 1, Domain = new object[] { 0, 1 })]
    public partial int Waving { get; set; }

    /// <summary>Loads an exact precompiled native material selection; missing binaries fail without a source fallback.</summary>
    internal static ShadowTerrainShader Create(ICoreAPI api, bool storage, bool waving)
    {
        var shader = new ShadowTerrainShader { PassName = Contract.Identity, AssetDomain = Constants.ModId };
        try
        {
            shader.SetShaderOptions(options =>
            {
                options.Set(StorageOption, storage ? 1 : 0);
                options.Set(WavingOption, waving ? 1 : 0);
            });
            shader.Initialize(EngineShaderAssets.Create(api.Assets, Constants.ModId));
            if (!shader.CompileAndLink()) throw new InvalidOperationException("Packaged native terrain shadow variant could not be prepared.");
            return shader;
        }
        catch { shader.Dispose(); throw; }
    }

    #endregion

    #region Private
    /// <summary>Registers generated bindings and owns their logical parameter storage.</summary>
    private ShadowTerrainShader()
    {
        foreach (var stage in Contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Publishes the immutable logical snapshot through ordinary generated resource capture.</summary>
    CpuUniformBuffer IShadowTerrainBindings.Parameters => MaterialParameters;
    #endregion
}
