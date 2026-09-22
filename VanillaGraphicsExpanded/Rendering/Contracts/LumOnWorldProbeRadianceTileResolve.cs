namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnWorldProbeRadianceTileResolve
    /// <summary>Declares the lumon_worldprobe_radiance_tile_resolve resource slots.</summary>
    private static void LumOnWorldProbeRadianceTileResolve(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLumOnWorldProbeResolveParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
    }
    #endregion
}
