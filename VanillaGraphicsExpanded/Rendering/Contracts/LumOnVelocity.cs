namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnVelocity
    /// <summary>Declares the lumon_velocity resource slots.</summary>
    private static void LumOnVelocity(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("LumOnFrameUBO", GpuBindingRegistry.Ubo.Frame, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 0, required: true);
    }
    #endregion
}
