using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns the GLSL 330 procedural galaxy executable and immutable per-tile inputs.</summary>
[ShaderProgram("Contract", "celestial_generate", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "celestial_generate.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "celestial_generate.fsh")]
internal sealed partial class CelestialGenerationProgram : GpuProgram, ICelestialGenerationBindings
{
    private readonly CelestialGenerationInputs inputs;
    #region Public API
    /// <summary>Registers a typed, offline-compiled program without runtime source patching.</summary>
    public CelestialGenerationProgram()
    {
        inputs = OwnUniformBuffer(new CelestialGenerationInputs());
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Returns the declaration shared by asset compilation and executable loading.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Supplies stable catalog storage.</summary>
    public partial GpuTextureRef Catalog { set; }
    /// <summary>Supplies tile-local index storage.</summary>
    public partial GpuTextureRef Indices { set; }
    /// <summary>Supplies parent cube storage independently of the scratch target.</summary>
    public partial GpuTextureRef ParentMap { set; }
    /// <summary>Returns the owned packed parameter block.</summary>
    CpuUniformBuffer ICelestialGenerationBindings.Inputs => inputs;
    /// <summary>Stages the complete geometry, source and mip parameters before one submission.</summary>
    internal void SetTile(int face, int size, int x, int y, int indexOffset, int indexCount,
        int parentLevel, int parentSize, CelestialSource source)
    {
        RequireInputMutation();
        inputs.SetTile(face, size, x, y, indexOffset, indexCount, parentLevel, parentSize, source);
    }
    #endregion
}
