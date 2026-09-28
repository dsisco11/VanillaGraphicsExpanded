using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies terrain atlas call-site coverage and exact operand preservation without a graphics context.</summary>
public sealed class TerrainAtlasDrawBindingTests
{
    #region Installed renderer contract
    /// <summary>Every installed terrain atlas assignment receives one callback while retaining its engine setter.</summary>
    [Theory]
    [InlineData("RenderOpaque", 5)]
    [InlineData("RenderShadow", 4)]
    [InlineData("RenderOIT", 2)]
    [InlineData("RenderAfterOIT", 1)]
    public void InstalledRendererAssignmentsReceiveCallbacks(string name, int expected)
    {
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == name);
        var generator = new DynamicMethod("atlasLayout", typeof(void), Type.EmptyTypes).GetILGenerator();
        var original = PatchProcessor.GetOriginalInstructions(method, generator).ToArray();
        var rewritten = TerrainAtlasDrawBindingHook.Transpiler(original, generator, method).ToArray();
        var callback = AccessTools.Method(typeof(TerrainAtlasDrawBindingHook), "BindAtlas");
        Assert.Equal(expected, rewritten.Count(instruction => instruction.Calls(callback)));
        var originalCalls = original.Where(instruction => instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt).Select(instruction => instruction.operand);
        var retainedCalls = rewritten.Where(instruction => (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && !instruction.Calls(callback)).Select(instruction => instruction.operand);
        Assert.Equal(originalCalls, retainedCalls);
    }

    /// <summary>A changed engine layout is rejected instead of silently leaving some terrain draws unbound.</summary>
    [Fact]
    public void MissingAssignmentIsRejected()
    {
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var generator = new DynamicMethod("missingAtlas", typeof(void), Type.EmptyTypes).GetILGenerator();
        var error = Assert.Throws<InvalidOperationException>(() => TerrainAtlasDrawBindingHook.Transpiler([], generator, method).ToArray());
        Assert.Contains("expected 1", error.Message);
    }
    #endregion

    #region Operand and control flow preservation
    /// <summary>Call prefixes cannot be separated from their target by injected operand captures.</summary>
    [Fact]
    public void PrefixedAssignmentIsRejected()
    {
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var generator = new DynamicMethod("prefixedAtlas", typeof(void), Type.EmptyTypes).GetILGenerator();
        var setter = AccessTools.PropertySetter(typeof(ShaderProgramChunkopaque), "TerrainTex2D");
        var input = new[] { new CodeInstruction(OpCodes.Tailcall), new CodeInstruction(OpCodes.Callvirt, setter) };
        var error = Assert.Throws<InvalidOperationException>(() => TerrainAtlasDrawBindingHook.Transpiler(input, generator, method).ToArray());
        Assert.Contains("prefixed call", error.Message);
    }

    /// <summary>Repeated page changes replay the exact receiver and atlas after the original setter, not a previous page.</summary>
    [Fact]
    public void CallbackFollowsSetterWithSameOperands()
    {
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var setter = AccessTools.PropertySetter(typeof(ShaderProgramChunkopaque), "TerrainTex2D");
        var dynamic = new DynamicMethod("atlasOperands", typeof(void), [typeof(ShaderProgramChunkopaque), typeof(int), typeof(List<object>)], typeof(TerrainAtlasDrawBindingTests), true);
        var generator = dynamic.GetILGenerator();
        var input = new[] { new CodeInstruction(OpCodes.Ldarg_0), new CodeInstruction(OpCodes.Ldarg_1), new CodeInstruction(OpCodes.Callvirt, setter), new CodeInstruction(OpCodes.Ret) };
        var binder = AccessTools.Method(typeof(TerrainAtlasDrawBindingHook), "BindAtlas");
        foreach (var instruction in TerrainAtlasDrawBindingHook.Transpiler(input, generator, method))
        {
            // Replace only the two external effects with recorders, leaving the transpiler's operand flow executable.
            if (instruction.Calls(setter) || instruction.Calls(binder))
            {
                generator.Emit(OpCodes.Ldarg_2);
                generator.Emit(OpCodes.Call, AccessTools.Method(typeof(TerrainAtlasDrawBindingTests), instruction.Calls(setter) ? nameof(RecordSetter) : nameof(RecordBinder)));
            }
            else if (instruction.operand is LocalBuilder local) generator.Emit(instruction.opcode, local);
            else generator.Emit(instruction.opcode);
        }
        var execute = dynamic.CreateDelegate<Action<ShaderProgramChunkopaque, int, List<object>>>();
        var program = (ShaderProgramChunkopaque)RuntimeHelpers.GetUninitializedObject(typeof(ShaderProgramChunkopaque));
        GC.SuppressFinalize(program);
        var records = new List<object>();
        execute(program, 17, records);
        execute(program, 29, records);
        Assert.Equal(new object[] { "setter", program, 17, "binder", program, 17, "setter", program, 29, "binder", program, 29 }, records);
    }

    /// <summary>Branch entry and exception boundaries wrap the whole injected sequence.</summary>
    [Fact]
    public void AssignmentLabelsAndExceptionBoundariesArePreserved()
    {
        var method = TerrainAtlasDrawBindingHook.TargetMethods().Single(candidate => candidate.Name == "RenderAfterOIT");
        var generator = new DynamicMethod("atlasMetadata", typeof(void), Type.EmptyTypes).GetILGenerator();
        var label = generator.DefineLabel();
        var setter = new CodeInstruction(OpCodes.Callvirt, AccessTools.PropertySetter(typeof(ShaderProgramChunkopaque), "TerrainTex2D"));
        setter.labels.Add(label);
        setter.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        setter.blocks.Add(new ExceptionBlock(ExceptionBlockType.EndExceptionBlock));
        var rewritten = TerrainAtlasDrawBindingHook.Transpiler([setter], generator, method).ToArray();
        Assert.Contains(label, rewritten[0].labels);
        Assert.Equal(ExceptionBlockType.BeginExceptionBlock, Assert.Single(rewritten[0].blocks).blockType);
        Assert.Equal(ExceptionBlockType.EndExceptionBlock, Assert.Single(rewritten[^1].blocks).blockType);
        Assert.Single(rewritten.SelectMany(instruction => instruction.labels));
    }

    /// <summary>Records the engine setter effect without accessing OpenGL.</summary>
    private static void RecordSetter(ShaderProgramChunkopaque program, int atlas, List<object> records) => records.AddRange(["setter", program, atlas]);

    /// <summary>Records the VGE binding effect without accessing OpenGL.</summary>
    private static void RecordBinder(ShaderProgramBase program, int atlas, List<object> records) => records.AddRange(["binder", program, atlas]);
    #endregion
}

