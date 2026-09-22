using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the isolated build validator's graphics program.</summary>
[ExcludeFromShaderCatalog]
internal static class BuildValidationGraphicsShader
{
    internal static GpuShaderContract Contract { get; } =
        ShaderProgramDeclaration.Graphics("fixture", "fixture.vsh", "fixture.fsh", 1);
}
