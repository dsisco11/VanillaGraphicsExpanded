using System.Reflection;

using VanillaGraphicsExpanded.LumOn;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Verifies debug-view routing requirements for shader programs and scene buffers.</summary>
public sealed class LumOnDebugRendererRoutingTests
{
    #region Public API
    /// <summary>Chunk-slot diagnostic modes select the scene G-buffer shader program.</summary>
    [Fact]
    public void GetShaderProgramKind_ChunkSlotDebugModes_AreSceneGBuffer()
    {
        // Arrange: locate the routing function exposed only as a private implementation detail.
        MethodInfo? method = typeof(LumOnDebugRenderer).GetMethod(
            "GetShaderProgramKind",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        // Act: query both chunk-slot diagnostic modes.
        var kindChunkSlot = (LumOnDebugShaderProgramKind)method!.Invoke(null, [LumOnDebugMode.LumonSceneChunkSlot])!;
        var kindSlotGen = (LumOnDebugShaderProgramKind)method!.Invoke(null, [LumOnDebugMode.LumonSceneSlotGeneration])!;

        // Assert: both modes use the scene G-buffer program family.
        Assert.Equal(LumOnDebugShaderProgramKind.SceneGBuffer, kindChunkSlot);
        Assert.Equal(LumOnDebugShaderProgramKind.SceneGBuffer, kindSlotGen);
    }

    /// <summary>The raw outcome view selects atlas bindings and requires initialized LumOn targets.</summary>
    [Fact]
    public void TraceOutcome_RoutesToAtlasAndRequiresLumOnBuffers()
    {
        // Arrange: resolve the program-family and target-requirement routing functions.
        var kind = typeof(LumOnDebugRenderer).GetMethod("GetShaderProgramKind", BindingFlags.NonPublic | BindingFlags.Static);
        var requiresBuffers = typeof(LumOnDebugRenderer).GetMethod("RequiresLumOnBuffers", BindingFlags.NonPublic | BindingFlags.Static);
        // Act and assert: the trace outcome retains its published enum value and atlas requirements.
        Assert.NotNull(kind);
        Assert.NotNull(requiresBuffers);
        Assert.Equal(69, (int)LumOnDebugMode.ProbeAtlasTraceOutcome);
        Assert.Equal(LumOnDebugShaderProgramKind.ProbeAtlas, (LumOnDebugShaderProgramKind)kind.Invoke(null, [LumOnDebugMode.ProbeAtlasTraceOutcome])!);
        Assert.True((bool)requiresBuffers.Invoke(null, [LumOnDebugMode.ProbeAtlasTraceOutcome])!);
    }

    /// <summary>The near-field geometry view uses the family's geometry bindings without requiring screen-space targets.</summary>
    [Fact]
    public void NearFieldGeometry_RoutesToWorldProbeWithoutRequiringLumOnBuffers()
    {
        // Arrange: resolve the program-family and target-requirement routing functions.
        var kind = typeof(LumOnDebugRenderer).GetMethod("GetShaderProgramKind", BindingFlags.NonPublic | BindingFlags.Static);
        var requiresBuffers = typeof(LumOnDebugRenderer).GetMethod("RequiresLumOnBuffers", BindingFlags.NonPublic | BindingFlags.Static);
        // Act and assert: near-field geometry routes to world probes without screen-space targets.
        Assert.NotNull(kind);
        Assert.NotNull(requiresBuffers);
        Assert.Equal(70, (int)LumOnDebugMode.NearFieldGeometry);
        Assert.Equal(LumOnDebugShaderProgramKind.WorldProbe, (LumOnDebugShaderProgramKind)kind.Invoke(null, [LumOnDebugMode.NearFieldGeometry])!);
        Assert.False((bool)requiresBuffers.Invoke(null, [LumOnDebugMode.NearFieldGeometry])!);
    }
    /// <summary>Transmission uses direct-light targets and remains available without indirect-light buffers.</summary>
    [Fact]
    public void Transmission_RoutesToDirectWithoutRequiringLumOnBuffers()
    {
        // Arrange: resolve the program-family and target-requirement routing functions.
        var kind = typeof(LumOnDebugRenderer).GetMethod("GetShaderProgramKind", BindingFlags.NonPublic | BindingFlags.Static);
        var requiresBuffers = typeof(LumOnDebugRenderer).GetMethod("RequiresLumOnBuffers", BindingFlags.NonPublic | BindingFlags.Static);
        // Act and assert: transmission uses the direct program with no LumOn-buffer requirement.
        Assert.NotNull(kind);
        Assert.NotNull(requiresBuffers);
        Assert.Equal(LumOnDebugShaderProgramKind.Direct, (LumOnDebugShaderProgramKind)kind.Invoke(null, [LumOnDebugMode.Transmission])!);
        Assert.False((bool)requiresBuffers.Invoke(null, [LumOnDebugMode.Transmission])!);
    }
    #endregion
}
