namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region LumOnHzbCopy
    /// <summary>Declares the lumon_hzb_copy resource slots.</summary>
    private static void LumOnHzbCopy(GpuBindingContract contract)
    {
        contract.RegisterSamplerUnit("primaryDepth", 0, required: true);
    }
    #endregion
}
