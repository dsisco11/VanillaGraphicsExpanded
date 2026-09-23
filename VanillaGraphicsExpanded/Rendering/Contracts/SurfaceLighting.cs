namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Declares the surface lighting producer's explicit GPU interfaces.</summary>
internal static partial class GpuShaderContracts
{
    #region Surface lighting layout
    /// <summary>Shares geometry bindings while reserving distinct read and write lighting resources.</summary>
    private static void SurfaceLighting(GpuBindingContract contract)
    {
        TraceGeometry(contract);
        SurfaceLightingInputs(contract);

        contract.RegisterSamplerUnit("lightColors", 3);
        contract.RegisterSamplerUnit("blockLevels", 4);
        contract.RegisterSamplerUnit("sunLevels", 5);

        contract.RegisterSamplerUnit("surfaces", 7);
        contract.RegisterImageUnit("indirectIrradiance", 0);
        contract.RegisterImageUnit("directIrradiance", 1);
        contract.RegisterImageUnit("nextOutgoing", 2);
        contract.RegisterShaderStorageBlockBinding("SurfaceWork", 0);

    }
    /// <summary>Declares one shared cache lookup interface for producers and geometry-hit consumers.</summary>
    private static void SurfaceLightingInputs(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("SurfaceLightingParams", GpuBindingRegistry.Ubo.Lights, required: false);
        contract.RegisterSamplerUnit("capturedMaterial", 16, required: false);
        contract.RegisterSamplerUnit("previousOutgoing", 17, required: false);
        contract.RegisterSamplerUnit("surfacePages", 18, required: false);
        contract.RegisterShaderStorageBlockBinding("SurfacePatches", 1, required: false);
        contract.RegisterShaderStorageBlockBinding("SurfaceSlots", 2, required: false);
        contract.RegisterShaderStorageBlockBinding("SurfaceReady", 3, required: false);
    }
    #endregion
}
