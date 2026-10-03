using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies engine call-site rewriting independently of native graphics execution.</summary>
[Collection("GPU")]
public sealed class EngineStateSwitchingTests
{
    #region Public API
    /// <summary>The engine texture path cannot query or reselect the active texture unit.</summary>
    [Fact]
    public void TextureAdapterBindsWithoutActiveUnitOperations()
    {
        // Inspect both sides of the adapter boundary so an accidental return to the explicit-unit API fails.
        var adapter = AccessTools.Method(typeof(EngineStateCalls), nameof(EngineStateCalls.BindTexture), [typeof(TextureTarget), typeof(int)]);
        var binding = AccessTools.Method(typeof(GlStateCache), "BindTextureOnActiveUnit");
        var adapterCalls = PatchProcessor.GetOriginalInstructions(adapter).Select(instruction => instruction.operand).OfType<MethodInfo>().ToArray();
        Assert.Contains(binding, adapterCalls);
        var bindingCalls = PatchProcessor.GetOriginalInstructions(binding).Select(instruction => instruction.operand).OfType<MethodInfo>().ToArray();
        Assert.Single(bindingCalls, method => method.DeclaringType == typeof(GL) && method.Name == nameof(GL.BindTexture));
        Assert.DoesNotContain(adapterCalls.Concat(bindingCalls), method => method.Name is "GetActiveTextureUnit" or "ActiveTexture" or "GetInteger");
    }

    /// <summary>All discovered base-game call sites accept the real Harmony patch without running engine code.</summary>
    [Fact]
    public void DiscoveredEngineMethodsCanAllBePatched()
    {
        using var dependencies = new EngineDependencyResolution();
        string gamePath = Environment.GetEnvironmentVariable("VINTAGE_STORY")!;
        foreach (string name in new[] { "VSEssentials", "VSSurvivalMod", "VSCreativeMod" })
            Assembly.LoadFrom(Path.Combine(gamePath, "Mods", name + ".dll"));
        var targets = EngineStateSwitchingHook.TargetMethods().ToArray();
        Assert.NotEmpty(targets);
        Assert.All(targets, target => Assert.Contains(target.DeclaringType!.Assembly.GetName().Name, new[] { "VintagestoryLib", "VSEssentials", "VSSurvivalMod", "VSCreativeMod" }));
        var harmony = new Harmony("VGE.Tests.EngineStateSwitching.InstalledCoverage");
        var coverage = new List<string>();
        try
        {
            foreach (var target in targets)
            {
                var original = PatchProcessor.GetOriginalInstructions(target);
                int calls = original.Count(instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo native && EngineStateCallMap.Replacements.ContainsKey(native));
                Assert.True(calls > 0, target.ToString());
                var rewritten = EngineStateSwitchingHook.Transpiler(original).ToArray();
                Assert.DoesNotContain(rewritten, instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo native && EngineStateCallMap.Replacements.ContainsKey(native));
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
                Assert.Contains(PatchProcessor.GetPatchInfo(target).Transpilers, patch => patch.owner == harmony.Id);
                coverage.Add($"{target.DeclaringType!.Assembly.GetName().Name}: {target.DeclaringType.FullName}.{target.Name} | {calls}");
            }
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts"));
            Directory.CreateDirectory(directory);
            File.WriteAllLines(Path.Combine(directory, "engine-state-patched-methods.txt"), coverage);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
    }

    /// <summary>Every replacement preserves the complete managed calling signature.</summary>
    [Fact]
    public void AdaptersPreserveNativeSignatures()
    {
        Assert.NotEmpty(EngineStateCallMap.Replacements);
        foreach (var (native, replacement) in EngineStateCallMap.Replacements)
        {
            Assert.Equal(typeof(GL), native.DeclaringType);
            Assert.Equal(typeof(EngineStateCalls), replacement.DeclaringType);
            Assert.Equal(native.ReturnType, replacement.ReturnType);
            Assert.Equal(native.GetParameters().Select(p => p.ParameterType), replacement.GetParameters().Select(p => p.ParameterType));
        }
    }

    /// <summary>Rewriting retains branch targets and exception metadata while preserving unsupported calls.</summary>
    [Fact]
    public void TranspilerPreservesInstructionMetadataAndUnsupportedCalls()
    {
        var generator = new DynamicMethod("Metadata", typeof(void), []).GetILGenerator();
        var native = AccessTools.Method(typeof(GL), nameof(GL.DepthMask), [typeof(bool)]);
        var original = new CodeInstruction(OpCodes.Call, native);
        original.labels.Add(generator.DefineLabel());
        original.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var unsupported = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GL), nameof(GL.CullFace), [typeof(TriangleFace)]));
        var rewritten = EngineStateSwitchingHook.Transpiler([original, unsupported]).ToArray();
        Assert.Equal(EngineStateCallMap.Replacements[native], rewritten[0].operand);
        Assert.Equal(original.labels, rewritten[0].labels);
        Assert.Equal(original.blocks, rewritten[0].blocks);
        Assert.Equal(native, original.operand);
        Assert.Equal(unsupported.operand, rewritten[1].operand);
        Assert.Equal(unsupported.opcode, rewritten[1].opcode);
    }

    /// <summary>Real installed engine methods lose all mapped native calls without changing body length.</summary>
    [Theory]
    [InlineData("BindTexture2d")]
    [InlineData("SetupDefaultFrameBuffers")]
    [InlineData("RenderPostprocessingEffects")]
    public void InstalledEngineBodiesReplaceEverySupportedCall(string name)
    {
        var method = AccessTools.Method(typeof(ClientPlatformWindows), name);
        var original = PatchProcessor.GetOriginalInstructions(method).ToArray();
        var rewritten = EngineStateSwitchingHook.Transpiler(original).ToArray();
        var mapped = original.Count(instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo target && EngineStateCallMap.Replacements.ContainsKey(target));
        Assert.True(mapped > 0);
        Assert.Equal(original.Length, rewritten.Length);
        Assert.DoesNotContain(rewritten, instruction => instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo target && EngineStateCallMap.Replacements.ContainsKey(target));
        Assert.Equal(mapped, rewritten.Count(instruction => instruction.operand is MethodInfo target && target.DeclaringType == typeof(EngineStateCalls)));
    }
    #endregion
}
