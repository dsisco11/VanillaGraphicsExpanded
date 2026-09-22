using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
public partial class LumOnDebugShaderProgram
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumon_debug_composite program.</summary>
    internal static GpuShaderContract CompositeContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_composite", "lumon_debug_composite.vsh", "lumon_debug_composite.fsh", 16, [Visibility, Composite, Ao]);
    /// <summary>Immutable declaration for the lumon_debug_direct program.</summary>
    internal static GpuShaderContract DirectContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_direct", "lumon_debug_direct.vsh", "lumon_debug_direct.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_gbuffer program.</summary>
    internal static GpuShaderContract GbufferContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_gbuffer", "lumon_debug_gbuffer.vsh", "lumon_debug_gbuffer.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_indirect program.</summary>
    internal static GpuShaderContract IndirectContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_indirect", "lumon_debug_indirect.vsh", "lumon_debug_indirect.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_probe_anchors program.</summary>
    internal static GpuShaderContract ProbeAnchorsContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_probe_anchors", "lumon_debug_probe_anchors.vsh", "lumon_debug_probe_anchors.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_probe_atlas program.</summary>
    internal static GpuShaderContract ProbeAtlasContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_probe_atlas", "lumon_debug_probe_atlas.vsh", "lumon_debug_probe_atlas.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_sh program.</summary>
    internal static GpuShaderContract ShContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_sh", "lumon_debug_sh.vsh", "lumon_debug_sh.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_temporal program.</summary>
    internal static GpuShaderContract TemporalContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_temporal", "lumon_debug_temporal.vsh", "lumon_debug_temporal.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_velocity program.</summary>
    internal static GpuShaderContract VelocityContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_velocity", "lumon_debug_velocity.vsh", "lumon_debug_velocity.fsh", 2, [Visibility]);
    /// <summary>Immutable declaration for the lumon_debug_worldprobe program.</summary>
    internal static GpuShaderContract WorldprobeContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug_worldprobe", "lumon_debug_worldprobe.vsh", "lumon_debug_worldprobe.fsh", 4, [Visibility, World, WorldGather], LumOnShaderConstants.World(true));
    /// <summary>Immutable declaration for the lumon_debug program.</summary>
    internal static GpuShaderContract DispatcherContract { get; } = ShaderProgramDeclaration.Graphics("lumon_debug", "lumon_debug.vsh", "lumon_debug.fsh", 32, [Visibility, Composite, Ao, World, WorldGather], LumOnShaderConstants.World(true));

    /// <summary>All program declarations owned by this shader family.</summary>
    internal static System.Collections.Generic.IReadOnlyList<GpuShaderContract> Contracts { get; } = System.Array.AsReadOnly<GpuShaderContract>([CompositeContract, DirectContract, GbufferContract, IndirectContract, ProbeAnchorsContract, ProbeAtlasContract, ShContract, TemporalContract, VelocityContract, WorldprobeContract, DispatcherContract]);
    #endregion
}
