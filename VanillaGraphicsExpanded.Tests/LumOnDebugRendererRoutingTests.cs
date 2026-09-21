using System.Reflection;

using VanillaGraphicsExpanded.LumOn;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class LumOnDebugRendererRoutingTests
{
    [Fact]
    public void GetShaderProgramKind_ChunkSlotDebugModes_AreSceneGBuffer()
    {
        MethodInfo? method = typeof(LumOnDebugRenderer).GetMethod(
            "GetShaderProgramKind",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var kindChunkSlot = (LumOnDebugShaderProgramKind)method!.Invoke(null, [LumOnDebugMode.LumonSceneChunkSlot])!;
        var kindSlotGen = (LumOnDebugShaderProgramKind)method!.Invoke(null, [LumOnDebugMode.LumonSceneSlotGeneration])!;

        Assert.Equal(LumOnDebugShaderProgramKind.SceneGBuffer, kindChunkSlot);
        Assert.Equal(LumOnDebugShaderProgramKind.SceneGBuffer, kindSlotGen);
    }

    /// <summary>The raw outcome view selects atlas bindings and requires initialized LumOn targets.</summary>
    [Fact]
    public void TraceOutcome_RoutesToAtlasAndRequiresLumOnBuffers()
    {
        var kind = typeof(LumOnDebugRenderer).GetMethod("GetShaderProgramKind", BindingFlags.NonPublic | BindingFlags.Static);
        var requiresBuffers = typeof(LumOnDebugRenderer).GetMethod("RequiresLumOnBuffers", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(kind);
        Assert.NotNull(requiresBuffers);
        Assert.Equal(69, (int)LumOnDebugMode.ProbeAtlasTraceOutcome);
        Assert.Equal(LumOnDebugShaderProgramKind.ProbeAtlas, (LumOnDebugShaderProgramKind)kind.Invoke(null, [LumOnDebugMode.ProbeAtlasTraceOutcome])!);
        Assert.True((bool)requiresBuffers.Invoke(null, [LumOnDebugMode.ProbeAtlasTraceOutcome])!);
    }
}

