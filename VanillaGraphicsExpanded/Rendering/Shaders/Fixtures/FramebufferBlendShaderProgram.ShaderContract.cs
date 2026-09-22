using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class FramebufferBlendShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/framebuffer_blend program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("tests/framebuffer_blend", "tests/GpuFramebufferBlendStateIntegrationTests_1.vsh", "tests/framebuffer_blend.fsh", 1);
    #endregion
}
