using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnWorldProbeClipmapResolveShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_worldprobe_clipmap_resolve program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Graphics("lumon_worldprobe_clipmap_resolve", "lumon_worldprobe_clipmap_resolve.vsh", "lumon_worldprobe_clipmap_resolve.fsh", 1);
    #endregion
}
