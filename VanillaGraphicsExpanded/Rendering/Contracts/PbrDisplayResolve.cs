namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region Display resolve
    /// <summary>Declares the HDR display resolve resource slots.</summary>
    private static void PbrDisplayResolve(GpuBindingContract contract)
    {
        contract.RegisterSamplerUnit("primaryScene", 0, required: true);
        contract.RegisterSamplerUnit("primaryDepth", 1, required: true);
    }
    #endregion
}
