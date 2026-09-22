namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region VgeWorldProbeOrbsPoints
    /// <summary>Declares the vge_worldprobe_orbs_points resource slots.</summary>
    private static void VgeWorldProbeOrbsPoints(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterUniformBlockBinding("LumOnWorldProbeUBO", GpuBindingRegistry.Ubo.WorldProbe, required: true);
        contract.RegisterUniformBlockBinding("VgeWorldProbeOrbsPointsParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
    }
    #endregion
}
