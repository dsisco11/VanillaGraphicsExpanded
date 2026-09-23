namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares the surface lighting producer's explicit GPU interfaces.</summary>
internal static partial class GpuShaderContracts
{
    #region Surface lighting layout
    /// <summary>Shares geometry bindings while reserving distinct read and write lighting resources.</summary>
    private static void SurfaceLighting(GpuBindingContract contract)
    {
        TraceGeometry(contract);
        contract.RegisterUniformBlockBinding("SurfaceLightingParams", GpuBindingRegistry.Ubo.Object);
        contract.RegisterSamplerUnit("capturedMaterial", 1);
        contract.RegisterSamplerUnit("previousOutgoing", 2);
        contract.RegisterSamplerUnit("lightColors", 3);
        contract.RegisterSamplerUnit("blockLevels", 4);
        contract.RegisterSamplerUnit("sunLevels", 5);
        contract.RegisterSamplerUnit("surfacePages", 6);
        contract.RegisterSamplerUnit("surfaces", 7);
        contract.RegisterImageUnit("indirectIrradiance", 0);
        contract.RegisterImageUnit("directIrradiance", 1);
        contract.RegisterImageUnit("nextOutgoing", 2);
        contract.RegisterShaderStorageBlockBinding("SurfaceWork", 0);
        contract.RegisterShaderStorageBlockBinding("SurfacePatches", 1);
        contract.RegisterShaderStorageBlockBinding("SurfaceSlots", 2);
        contract.RegisterShaderStorageBlockBinding("SurfaceReady", 3);
    }
    #endregion
}
