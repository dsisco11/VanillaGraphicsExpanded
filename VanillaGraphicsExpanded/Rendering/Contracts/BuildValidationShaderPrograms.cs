using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Selects isolated validator shader contracts without adding them to the packaged catalog.</summary>
internal static class BuildValidationShaderPrograms
{
    #region Selection
    /// <summary>Creates the explicit graphics-only or graphics-and-compute validation scope.</summary>
    public static ShaderVariantResolver Create(bool includeCompute = true) =>
        new(GeneratedShaderCatalog.Programs("build-validation")
            .Where(program => includeCompute || program.Stages.All(stage => stage.Kind != ShaderStageKind.Compute)));
    #endregion
}
