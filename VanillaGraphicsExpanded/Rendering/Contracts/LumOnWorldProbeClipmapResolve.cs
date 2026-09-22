namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnWorldProbeClipmapResolve
    /// <summary>Declares the lumon_worldprobe_clipmap_resolve resource slots.</summary>
    private static void LumOnWorldProbeClipmapResolve(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeLumOnWorldProbeResolveParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
    }
    #endregion
}
