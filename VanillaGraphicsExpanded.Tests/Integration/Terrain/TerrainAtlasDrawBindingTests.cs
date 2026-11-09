using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Verifies atlas binding coverage against installed terrain renderer methods.</summary>
public sealed class TerrainAtlasDrawBindingTests
{
    #region Public API
    /// <summary>Installed renderer assignments receive one callback while retaining the engine setter calls.</summary>
    [Theory]
    [InlineData("RenderOpaque", 5)]
    [InlineData("RenderShadow", 4)]
    [InlineData("RenderOIT", 2)]
    [InlineData("RenderAfterOIT", 1)]
    public void InstalledRendererAssignmentsReceiveCallbacks(string name, int expected)
    {
        // Arrange the named installed engine renderer and capture its original instruction stream.
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == name);
        var generator = new DynamicMethod("atlasLayout", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(method, generator).ToArray();

        // Act by applying the production terrain-atlas transpiler to that installed body.
        var rewritten = TerrainAtlasDrawBindingHook.Transpiler(original, generator, method).ToArray();

        // Assert every atlas assignment receives one callback and all non-callback calls retain their order.
        var callback = AccessTools.Method(typeof(TerrainAtlasDrawBindingHook), "BindAtlas");
        Assert.Equal(expected, rewritten.Count(instruction => instruction.Calls(callback)));
        var originalCalls = original.Where(instruction => instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt).Select(instruction => instruction.operand);
        var retainedCalls = rewritten.Where(instruction => (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && !instruction.Calls(callback)).Select(instruction => instruction.operand);
        Assert.Equal(originalCalls, retainedCalls);
    }
    #endregion
}
