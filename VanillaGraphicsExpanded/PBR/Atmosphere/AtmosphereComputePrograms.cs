using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Declares the dependent atmospheric transport passes in the production SPIR-V catalog.</summary>
[ShaderProgram("Scattering", "atmosphere_scattering", 1)]
[ShaderStage("Scattering", ShaderStageKind.Compute, "atmosphere_scattering.csh")]
[ShaderProgram("Sky", "atmosphere_sky", 1)]
[ShaderStage("Sky", ShaderStageKind.Compute, "atmosphere_sky.csh")]
[ShaderProgram("Lighting", "atmosphere_lighting", 1)]
[ShaderStage("Lighting", ShaderStageKind.Compute, "atmosphere_lighting.csh")]

internal sealed partial class AtmosphereComputePrograms : GpuComputeProgram, IAtmosphereComputeProgramsBindings
{
    /// <summary>Adopts one linked atmosphere pass and its generated resource API.</summary>
    public AtmosphereComputePrograms(GpuComputePipeline pipeline) : base(pipeline) { }
}
