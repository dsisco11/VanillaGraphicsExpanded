using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the isolated build validator's compute program.</summary>
[ExcludeFromShaderCatalog]
internal static class BuildValidationComputeShader
{
    internal static GpuShaderContract Contract { get; } =
        ShaderProgramDeclaration.Compute("fixture_compute", "fixture.csh");
}
