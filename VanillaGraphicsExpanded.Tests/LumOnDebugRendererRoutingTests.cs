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
}

