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
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
internal static partial class AtmosphereComputePrograms
{

    #region Private: GPU binding declarations
    /// <summary>Declares the AtmosphereParameters StorageBlock slot.</summary>
    [ShaderBinding("AtmosphereParameters", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    private static partial ShaderStorageBlockBinding AtmosphereParameters { get; }
    /// <summary>Declares the AtmosphereScattering StorageBlock slot.</summary>
    [ShaderBinding("AtmosphereScattering", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute, Programs = new[] { "Scattering", "Sky" })]
    private static partial ShaderStorageBlockBinding AtmosphereScattering { get; }
    /// <summary>Declares the AtmosphereOutput StorageBlock slot.</summary>
    [ShaderBinding("AtmosphereOutput", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute, Programs = new[] { "Sky", "Lighting" })]
    private static partial ShaderStorageBlockBinding AtmosphereOutput { get; }
    #endregion
}
