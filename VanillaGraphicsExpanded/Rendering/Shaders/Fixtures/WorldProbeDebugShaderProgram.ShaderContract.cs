using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class WorldProbeDebugShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/worldprobe_debug program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Pair("tests/worldprobe_debug", "lumon_debug.vsh", global::VanillaGraphicsExpanded.LumOn.LumOnDebugShaderProgram.WorldprobeContract);
    #endregion
}
