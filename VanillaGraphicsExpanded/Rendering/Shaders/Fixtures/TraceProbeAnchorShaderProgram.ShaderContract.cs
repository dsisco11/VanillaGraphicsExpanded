using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal static class TraceProbeAnchorShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the tests/trace_probe_anchor program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Pair("tests/trace_probe_anchor", "lumon_probe_anchor.vsh", global::VanillaGraphicsExpanded.LumOn.LumOnScreenProbeAtlasTraceShaderProgram.Contract);
    #endregion
}
