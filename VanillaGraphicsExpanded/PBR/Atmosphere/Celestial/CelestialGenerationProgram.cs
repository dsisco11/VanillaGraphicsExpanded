using System;
using System.Numerics;
using System.Runtime.InteropServices;
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
    private readonly PackedUniformBuffer inputs;
    #region Public API
    /// <summary>Registers a typed, offline-compiled program without runtime source patching.</summary>
    public CelestialGenerationProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(64));
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Returns the declaration shared by asset compilation and executable loading.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Supplies stable catalog storage.</summary>
    public partial GpuTexture? Catalog { set; }
    /// <summary>Supplies tile-local index storage.</summary>
    public partial GpuTexture? Indices { set; }
    /// <summary>Supplies parent cube storage independently of the scratch target.</summary>
    public partial GpuTexture? ParentMap { set; }
    /// <summary>Returns the owned packed parameter block.</summary>
    CpuUniformBuffer ICelestialGenerationBindings.Inputs => inputs;
    /// <summary>Stages the complete geometry, source and mip parameters before one submission.</summary>
    internal void SetTile(int face, int size, int x, int y, int indexOffset, int indexCount,
        int parentLevel, int parentSize, CelestialSource source)
    {
        Span<Vector4> values = stackalloc Vector4[4];
        values[0] = new(face, size, x, y);
        values[1] = new(indexOffset, indexCount, parentLevel, parentSize);
        values[2] = new(source.GalaxyAmplitude, source.StorageScale, source.Quality.Octaves, source.MaximumNits);
        values[3] = new(CelestialResidual.Height, CelestialResidual.KernelMaximum, 0, 0);
        inputs.SetBytes(MemoryMarshal.AsBytes(values));
    }
    #endregion
}
