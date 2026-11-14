using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns packaged native standard-material shadows with explicit preservation of original object instances.</summary>
[ShaderProgram("Contract", "shadows/static", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "shadows/static.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "shadows/terrain.fsh")]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Instanced))]
[ShaderUse("Contract", ShaderStageKind.Vertex, nameof(Waving))]
internal sealed partial class ShadowStaticShader : ShadowNativeMaterialShader, IShadowStaticBindings
{
    private readonly ShadowObjectParameters flags = new();
    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <inheritdoc />
    internal override bool PackedLayout => false;
    /// <inheritdoc />
    internal override PrimitiveTopology Topology => PrimitiveTopology.Triangles;
    /// <summary>Selects an existing native instance-transform stream; no instance mesh data is copied.</summary>
    [ShaderOption("VGE_OBJECT_INSTANCED", 0, Domain = new object[] { 0, 1 })]
    public partial int Instanced { get; set; }
    /// <summary>Uses the native material's deformation switch.</summary>
    [ShaderOption("VGE_VERTEX_DEFORMATION_ENABLED", 1, Domain = new object[] { 0, 1 })]
    public partial int Waving { get; set; }

    /// <summary>Freezes exact native renderer flags before resource capture.</summary>
    internal void CaptureFlags(int addedFlags, int dontWarpVertices)
    {
        RequireInputMutation();
        flags.SetFlags(addedFlags, dontWarpVertices);
    }

    /// <summary>Loads an exact precompiled native material variant without runtime source compilation.</summary>
    internal static ShadowStaticShader Create(ICoreAPI api, bool instanced, bool waving)
    {
        var shader = new ShadowStaticShader { PassName = Contract.Identity, AssetDomain = Constants.ModId };
        try
        {
            shader.SetShaderOptions(options => { options.Set(InstancedOption, instanced ? 1 : 0); options.Set(WavingOption, waving ? 1 : 0); });
            shader.Initialize(EngineShaderAssets.Create(api.Assets, Constants.ModId));
            if (!shader.CompileAndLink()) throw new InvalidOperationException("Packaged native static shadow variant could not be prepared.");
            return shader;
        }
        catch { shader.Dispose(); throw; }
    }
    #endregion

    #region Private
    /// <summary>Registers generated resource capture and retains logical material versions.</summary>
    private ShadowStaticShader()
    {
        foreach (var stage in Contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
        OwnUniformBuffer(flags);
    }
    /// <inheritdoc />
    CpuUniformBuffer IShadowTerrainBindings.Parameters => MaterialParameters;
    /// <inheritdoc />
    CpuUniformBuffer IShadowStaticBindings.ObjectFlags => flags;
    #endregion
}
