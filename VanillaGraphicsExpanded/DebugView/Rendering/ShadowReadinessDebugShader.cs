using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Displays whole-light GPU status directly, without downloading page tables or shadow images.</summary>
[ShaderProgram("Contract","shadows/readiness-debug",4)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"debug_texture.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"shadows/readiness-debug.fsh")]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(DisplayCandidates))]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(DisplayPages))]
internal sealed partial class ShadowReadinessDebugShader:GpuProgram,IShadowReadinessDebugBindings
{
    /// <inheritdoc />
    internal override GpuShaderContract ProgramContract=>Contract;
    #region Public API
    /// <summary>Registers generated bindings through the shared program layout.</summary>
    public ShadowReadinessDebugShader()=>ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    /// <summary>Selects direct GPU candidate continuation counters instead of light readiness rows.</summary>
    [ShaderOption("VGE_SHADOW_DEBUG_CANDIDATES",false)]
    public partial bool DisplayCandidates { get; set; }
    /// <summary>Selects physical-page residency and validity rather than whole-light status.</summary>
    [ShaderOption("VGE_SHADOW_DEBUG_PAGES",false)]
    public partial bool DisplayPages { get; set; }
    #endregion
}

