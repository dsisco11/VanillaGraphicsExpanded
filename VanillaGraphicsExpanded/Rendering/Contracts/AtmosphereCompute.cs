namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Defines the explicit storage interfaces for atmospheric transport.</summary>
internal static partial class GpuShaderContracts
{
    #region Atmosphere bindings
    /// <summary>Matches the common transport parameters and pass-specific outputs.</summary>
    private static void AtmosphereCompute(GpuBindingContract contract, string shader)
    {
        contract.RegisterShaderStorageBlockBinding("AtmosphereParameters", 0);
        if (shader != "atmosphere_lighting") contract.RegisterShaderStorageBlockBinding("AtmosphereScattering", 1);
        if (shader != "atmosphere_scattering") contract.RegisterShaderStorageBlockBinding("AtmosphereOutput", 2);
    }
    #endregion
}
