using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "tests/vge_global_defines_smoke", 16)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/vge_global_defines_smoke.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/vge_global_defines_smoke.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Lighting")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Composite")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Ao")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(AmbientOcclusion))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Enabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(PbrComposite))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(ShortRangeAo))]
internal static partial class GlobalDefinesShaderProgram
{
    #region Shader options
    /// <summary>Exposes the shared AmbientOcclusion shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AmbientOcclusion))]
    internal static partial ShaderOption<bool> AmbientOcclusion { get; }

    /// <summary>Exposes the shared Enabled shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.Enabled))]
    internal static partial ShaderOption<bool> Enabled { get; }

    /// <summary>Exposes the shared PbrComposite shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.PbrComposite))]
    internal static partial ShaderOption<bool> PbrComposite { get; }

    /// <summary>Exposes the shared ShortRangeAo shader option key.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ShortRangeAo))]
    internal static partial ShaderOption<bool> ShortRangeAo { get; }
    #endregion

}
