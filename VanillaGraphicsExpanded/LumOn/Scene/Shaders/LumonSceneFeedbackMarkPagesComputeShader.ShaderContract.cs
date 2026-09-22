using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal partial class LumonSceneFeedbackMarkPagesComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumonscene_feedback_mark_pages program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("lumonscene_feedback_mark_pages", "lumonscene_feedback_mark_pages.csh");
    #endregion
}
