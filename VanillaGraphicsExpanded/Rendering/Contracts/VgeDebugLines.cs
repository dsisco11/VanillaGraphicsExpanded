namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Shared build and runtime resource declarations.</summary>
internal static partial class GpuShaderContracts
{
    #region VgeDebugLines
    /// <summary>Declares the vge_debug_lines resource slots.</summary>
    private static void VgeDebugLines(GpuBindingContract contract)
    {
        contract.RegisterUniformBlockBinding("VgeDebugLinesParamsUBO", GpuBindingRegistry.Ubo.Object, required: true);
    }
    #endregion
}
