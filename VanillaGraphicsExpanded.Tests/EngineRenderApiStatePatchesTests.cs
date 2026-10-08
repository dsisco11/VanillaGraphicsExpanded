using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies complete installed engine render-API coverage and ownership of the second patch pass.</summary>
[Collection("GPU")]
public sealed class EngineRenderApiStatePatchesTests
{
    #region Public API
    /// <summary>The structural selection covers the installed API hierarchy, including constructors and ordinary accessors.</summary>
    [Fact]
    public void TargetsCoverInstalledRenderApiHierarchy()
    {
        using var dependencies = new EngineDependencyResolution();
        var targets = EngineRenderApiStatePatches.TargetMethods().ToArray();
        Assert.Equal(216, targets.Length);
        Assert.Equal(216, targets.Distinct().Count());
        Assert.Equal(new[] { "Vintagestory.Client.Gui.MainMenuRenderAPI", "Vintagestory.Client.NoObf.RenderAPIGame", "Vintagestory.Client.RenderAPIBase" },
            targets.Select(method => method.DeclaringType!.FullName).Distinct().Order());
        Assert.Equal(3, targets.Count(method => method.IsConstructor));
        Assert.Contains(AccessTools.Method(typeof(RenderAPIBase), "PushScissor"), targets);
        Assert.Contains(AccessTools.Method(typeof(RenderAPIBase), "PopScissor"), targets);
        Assert.Contains(AccessTools.Method(typeof(RenderAPIBase), "GlScissor"), targets);
        Assert.Contains(AccessTools.Method(typeof(RenderAPIBase), "GlScissorFlag"), targets);
        Assert.Contains(AccessTools.PropertyGetter(typeof(RenderAPIGame), "Api"), targets);
        Assert.DoesNotContain(AccessTools.PropertyGetter(typeof(RenderAPIBase), "Api"), targets);
        Assert.All(targets, method =>
        {
            Assert.Equal(typeof(ClientPlatformWindows).Assembly, method.DeclaringType!.Assembly);
            Assert.True(typeof(IRenderAPI).IsAssignableFrom(method.DeclaringType));
            Assert.False(method.IsAbstract || method.ContainsGenericParameters || (method.IsConstructor && method.IsStatic));
            Assert.NotNull(method.GetMethodBody());
        });
    }

    /// <summary>Repeated application recompiles ordinary callers without duplicate transpilers and uses normal owner teardown.</summary>
    [Fact]
    public void ApplyingAfterNativeRoutingIsIdempotentAndOwnerScoped()
    {
        using var dependencies = new EngineDependencyResolution();
        var harmony = new Harmony("VGE.Tests.RenderApiStatePatches.Ownership");
        var targets = EngineRenderApiStatePatches.TargetMethods().ToArray();
        try
        {
            harmony.CreateClassProcessor(typeof(EngineStateSwitchingHook)).Patch();
            EngineRenderApiStatePatches.Apply(harmony);
            EngineRenderApiStatePatches.Apply(harmony);
            foreach (var target in targets)
            {
                var patch = Assert.Single(Harmony.GetPatchInfo(target).Transpilers, patch => patch.owner == harmony.Id);
                Assert.Equal(AccessTools.Method(typeof(EngineStateSwitchingHook), "Transpiler"), patch.PatchMethod);
            }
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        Assert.All(targets, target => Assert.DoesNotContain(Harmony.GetPatchInfo(target)?.Transpilers ?? [], patch => patch.owner == harmony.Id));
    }
    #endregion
}
