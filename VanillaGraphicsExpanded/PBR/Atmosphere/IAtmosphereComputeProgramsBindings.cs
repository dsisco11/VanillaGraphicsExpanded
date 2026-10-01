using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Declares the GPU binding contract for AtmosphereComputePrograms.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal interface IAtmosphereComputeProgramsBindings
{
    #region Public API
    /// <summary>Declares the AtmosphereParameters StorageBlock slot.</summary>
    [ShaderBinding("AtmosphereParameters", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    ShaderStorageBlockBinding AtmosphereParameters { get; }
    /// <summary>Declares the AtmosphereScattering StorageBlock slot.</summary>
    [ShaderBinding("AtmosphereScattering", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute, Programs = new[] { "Scattering", "Sky" })]
    ShaderStorageBlockBinding AtmosphereScattering { get; }
    /// <summary>Declares the AtmosphereOutput StorageBlock slot.</summary>
    [ShaderBinding("AtmosphereOutput", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute, Programs = new[] { "Sky", "Lighting" })]
    ShaderStorageBlockBinding AtmosphereOutput { get; }
    #endregion
}
