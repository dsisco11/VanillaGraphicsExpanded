using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Terrain;

/// <summary>Checks malformed synthetic IL and operand flow for terrain atlas binding.</summary>
public sealed class TerrainAtlasDrawBindingTests
{
    #region Public API
    #region Rewrite guards
    /// <summary>A missing assignment is rejected instead of leaving a terrain draw unbound.</summary>
    [Fact]
    public void MissingAssignmentIsRejected()
    {
        // Arrange the real target metadata and an empty synthetic body.
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var generator = new DynamicMethod("missingAtlas", typeof(void), Type.EmptyTypes).GetILGenerator();

        // Act and assert the production transpiler reports its expected assignment count.
        var error = Assert.Throws<InvalidOperationException>(() => TerrainAtlasDrawBindingHook.Transpiler([], generator, method).ToArray());
        Assert.Contains("expected 1", error.Message);
    }

    /// <summary>Call prefixes stay attached to their target rather than being split by injected captures.</summary>
    [Fact]
    public void PrefixedAssignmentIsRejected()
    {
        // Arrange a synthetic tail-prefixed setter call using the engine property's actual setter metadata.
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var generator = new DynamicMethod("prefixedAtlas", typeof(void), Type.EmptyTypes).GetILGenerator();
        var setter = AccessTools.PropertySetter(typeof(ShaderProgramChunkopaque), "TerrainTex2D");
        var input = new[] { new CodeInstruction(OpCodes.Tailcall), new CodeInstruction(OpCodes.Callvirt, setter) };

        // Act and assert the unsupported prefix boundary is rejected explicitly.
        var error = Assert.Throws<InvalidOperationException>(() => TerrainAtlasDrawBindingHook.Transpiler(input, generator, method).ToArray());
        Assert.Contains("prefixed call", error.Message);
    }
    #endregion

    #region Operand and control-flow preservation
    /// <summary>Repeated page changes replay the same receiver and atlas after each original setter.</summary>
    [Fact]
    public void CallbackFollowsSetterWithSameOperands()
    {
        // Arrange executable IL whose setter and injected callback effects are replaced only by recorders.
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var setter = AccessTools.PropertySetter(typeof(ShaderProgramChunkopaque), "TerrainTex2D");
        var dynamic = new DynamicMethod("atlasOperands", typeof(void), [typeof(ShaderProgramChunkopaque), typeof(int), typeof(List<object>)], typeof(TerrainAtlasDrawBindingTests), true);
        var generator = dynamic.GetILGenerator();
        var input = new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Callvirt, setter), new CodeInstruction(OpCodes.Ret) };
        var binder = AccessTools.Method(typeof(TerrainAtlasDrawBindingHook), "BindAtlas");
        foreach (var instruction in TerrainAtlasDrawBindingHook.Transpiler(input, generator, method))
        {
            // Preserve the transpiler's operand flow and replace only external setter/binder effects.
            if (instruction.Calls(setter) || instruction.Calls(binder))
            {
                generator.Emit(OpCodes.Ldarg_2);
                generator.Emit(OpCodes.Call, AccessTools.Method(typeof(TerrainAtlasDrawBindingTests), instruction.Calls(setter) ? nameof(RecordSetter) : nameof(RecordBinder)));
            }
            else if (instruction.operand is LocalBuilder local) generator.Emit(instruction.opcode, local);
            else generator.Emit(instruction.opcode);
        }

        // Act on two page changes so stale receiver or atlas captures become observable.
        var execute = dynamic.CreateDelegate<Action<ShaderProgramChunkopaque, int, List<object>>>();
        var program = (ShaderProgramChunkopaque)RuntimeHelpers.GetUninitializedObject(typeof(ShaderProgramChunkopaque));
        GC.SuppressFinalize(program);
        var records = new List<object>();
        execute(program, 17, records);
        execute(program, 29, records);

        // Assert setter and binder observe the original receiver and current atlas on both calls.
        Assert.Equal(new object[] { "setter", program, 17, "binder", program, 17, "setter", program, 29, "binder", program, 29 }, records);
    }

    /// <summary>Branch entry and exception boundaries wrap the full injected instruction sequence.</summary>
    [Fact]
    public void AssignmentLabelsAndExceptionBoundariesArePreserved()
    {
        // Arrange one setter instruction carrying a branch label and both exception-boundary markers.
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var generator = new DynamicMethod("atlasMetadata", typeof(void), Type.EmptyTypes).GetILGenerator();
        var label = generator.DefineLabel();
        var setter = new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(ShaderProgramChunkopaque), "TerrainTex2D"));
        setter.labels.Add(label);
        setter.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        setter.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));

        // Act by inserting the binding callback around that labeled assignment.
        var rewritten = TerrainAtlasDrawBindingHook.Transpiler([setter], generator, method).ToArray();

        // Assert the label enters the rewritten sequence and exception boundaries enclose it exactly once.
        Assert.Contains(label, rewritten[0].labels);
        Assert.Equal(ExceptionBlockType.BeginExceptionBlock, Assert.Single(rewritten[0].blocks).blockType);
        Assert.Equal(ExceptionBlockType.EndExceptionBlock, Assert.Single(rewritten[^1].blocks).blockType);
        Assert.Single(rewritten.SelectMany(instruction => instruction.labels));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Records the original engine setter effect without entering OpenGL.</summary>
    private static void RecordSetter(ShaderProgramChunkopaque program, int atlas, List<object> records) => records.AddRange(["setter", program, atlas]);

    /// <summary>Records the VGE binding effect without entering OpenGL.</summary>
    private static void RecordBinder(ShaderProgramBase program, int atlas, List<object> records) => records.AddRange(["binder", program, atlas]);
    #endregion
}
