using System;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>The shared catalog of explicitly declared owned programs and stages.</summary>
internal static partial class GpuShaderContracts
{
    private static readonly Lazy<ShaderVariantResolver> registry = new(BuildRegistry);
    public static ShaderVariantResolver Registry => registry.Value;

    #region Registry construction
    /// <summary>Validates the generated references to shader-owned declarations and their shared stages.</summary>
    private static ShaderVariantResolver BuildRegistry() =>
        new(GeneratedShaderCatalog.Programs());
    #endregion

    #region Compatibility access
    /// <summary>Retains resource-layout access for existing owners, including two named layout aliases.</summary>
    public static GpuBindingContract Create(string identity)
    {
        if (identity == "pbr_heightbake") identity = "pbr_heightbake_combine";
        if (identity == "GpuUniformRingBufferIntegrationTests_1") identity = "tests/GpuUniformRingBufferIntegrationTests_1";
        var program = Registry.FindProgram(identity);
        return program.Stages.FirstOrDefault(s => s.Kind == ShaderStageKind.Fragment)?.Bindings ?? program.Stages[0].Bindings;
    }
    #endregion
}
