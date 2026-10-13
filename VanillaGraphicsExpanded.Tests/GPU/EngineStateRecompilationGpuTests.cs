using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that rebuilding warmed higher callers repairs native state routes at their owner.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineStateRecompilationGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The existing owner rebuilds every independently audited installed cull caller without duplicate registration.</summary>
    [Fact]
    public void OwnerRebuildsAuditedInstalledCallersWithoutDuplicateRouting()
    {
        using var dependencies = new EngineDependencyResolution();
        var engine = typeof(Vintagestory.Client.NoObf.ClientMain).Assembly;
        var audited = new[]
        {
            ("Vintagestory.Client.RenderAPIBase", "GlDisableCullFace"),
            ("Vintagestory.Client.RenderAPIBase", "GlEnableCullFace"),
            ("Vintagestory.Client.ScreenManager", "Render"),
            ("Vintagestory.Client.SystemClientCommands", "OnRenderBlockItemPngs"),
            ("Vintagestory.Client.NoObf.ClientMain", "Start"),
            ("Vintagestory.Client.NoObf.ClientMain", "MainRenderLoop"),
            ("Vintagestory.Client.NoObf.ClientPlatformWindows", "LoadFrameBuffer"),
            ("Vintagestory.Client.NoObf.TextureAtlas", "Export"),
            ("Vintagestory.Client.NoObf.ChunkRenderer", "RenderShadow"),
            ("Vintagestory.Client.NoObf.ChunkRenderer", "RenderOpaque"),
            ("Vintagestory.Client.NoObf.ChunkRenderer", "RenderAfterOIT"),
            ("Vintagestory.Client.NoObf.InventoryItemRenderer", "CreateFrameBuffer"),
            ("Vintagestory.Client.NoObf.SystemRenderDecals", "OnRenderFrame3D"),
            ("Vintagestory.Client.NoObf.SystemRenderEntities", "OnRenderOpaque3D"),
            ("Vintagestory.Client.NoObf.SystemRenderEntities", "OnRenderAfterOIT"),
            ("Vintagestory.Client.NoObf.SystemRenderInsideBlock", "OnRenderFrame3D"),
            ("Vintagestory.Client.NoObf.SystemRenderNightSky", "OnRenderFrame3D"),
            ("Vintagestory.Client.NoObf.SystemRenderSunMoon", "OnRenderFrame3D"),
            ("Vintagestory.Client.NoObf.SystemRenderSunMoon", "OnRenderFrame3DPost")
        }.Select(entry =>
        {
            // Pin the independently audited overloads, including the enum framebuffer route.
            Type[] parameters = entry.Item2 switch
            {
                "GlDisableCullFace" or "GlEnableCullFace" or "Start" => Type.EmptyTypes,
                "LoadFrameBuffer" => [typeof(Vintagestory.API.Client.EnumFrameBuffer)],
                "Export" => [typeof(string), typeof(Vintagestory.Client.NoObf.ClientMain), typeof(int)],
                "CreateFrameBuffer" => [typeof(Vintagestory.API.MathTools.Size2i), typeof(int)],
                _ => [typeof(float)]
            };
            return AccessTools.DeclaredMethod(engine.GetType(entry.Item1, true), entry.Item2, parameters);
        }).ToArray();
        var targets = EngineRenderApiStatePatches.TargetMethods().ToArray();
        Assert.Equal(targets.Length, targets.Distinct().Count());
        Assert.Equal(19, audited.Length);
        Assert.All(audited, method => Assert.Contains(method, targets));
        var harmony = new Harmony($"vge.tests.cull.owner.{Guid.NewGuid():N}");
        var transpiler = AccessTools.Method(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler));
        try
        {
            // Follow startup ordering: route the small native helpers first, then
            // rebuild their known higher owners. Repeating the rebuild stays idempotent.
            foreach (string helper in new[] { "GlEnableCullFace", "GlDisableCullFace" })
                harmony.Patch(AccessTools.Method(typeof(Vintagestory.Client.NoObf.ClientPlatformWindows), helper),
                    transpiler: new HarmonyMethod(transpiler));
            for (int repeat = 0; repeat < 2; repeat++)
            {
                EngineRenderApiStatePatches.Apply(harmony);
                foreach (var method in audited)
                    Assert.Single(Harmony.GetPatchInfo(method)!.Transpilers,
                        patch => patch.owner == harmony.Id && patch.PatchMethod == transpiler);
            }
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>Helper patches cannot repair an existing inlined caller until that higher body is rebuilt.</summary>
    [Fact]
    public void RebuiltCullCallersTrackBothTransitionsWithoutInvalidation()
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        bool previous = GL.IsEnabled(EnableCap.CullFace);
        var harmony = new Harmony($"vge.tests.cull.recompile.{Guid.NewGuid():N}");
        var transpiler = new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler));
        try
        {
            // Optimize the higher caller before changing its aggressively inlineable
            // helper. Its existing native enable then survives patching the helper.
            EnableCaller(); DisableCaller();
            harmony.Patch(AccessTools.Method(typeof(EngineStateRecompilationGpuTests), nameof(EnableHelper)), transpiler: transpiler);
            harmony.Patch(AccessTools.Method(typeof(EngineStateRecompilationGpuTests), nameof(DisableHelper)), transpiler: transpiler);
            harmony.Patch(AccessTools.Method(typeof(EngineStateRecompilationGpuTests), nameof(DisableCaller)), transpiler: transpiler);
            cache.InvalidateAll();
            cache.SetCapability(EnableCap.CullFace, false);
            EnableCaller();
            Assert.True(GL.IsEnabled(EnableCap.CullFace));
            DisableCaller();
            // This is the captured failure: native enabled, cache still disabled;
            // the correctly routed disable is therefore suppressed.
            Assert.True(GL.IsEnabled(EnableCap.CullFace));

            harmony.Patch(AccessTools.Method(typeof(EngineStateRecompilationGpuTests), nameof(EnableCaller)), transpiler: transpiler);
            // Reestablish the same seed once. No invalidation occurs between the
            // following native preparation, celestial disable and cached restores.
            cache.InvalidateAll();
            cache.SetCapability(EnableCap.CullFace, false);
            EnableCaller();
            Assert.True(GL.IsEnabled(EnableCap.CullFace));
            DisableCaller();
            Assert.False(GL.IsEnabled(EnableCap.CullFace));
            cache.SetCapability(EnableCap.CullFace, true);
            Assert.True(GL.IsEnabled(EnableCap.CullFace));
            cache.SetCapability(EnableCap.CullFace, false);
            Assert.False(GL.IsEnabled(EnableCap.CullFace));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            cache.InvalidateAll();
            cache.SetCapability(EnableCap.CullFace, previous);
        }
    }
    #endregion

    #region Private
    /// <summary>Models an optimized preparation body compiled before its platform helper is patched.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void EnableCaller() => EnableHelper();

    /// <summary>Models the celestial body rebuilt after its native helper has been routed.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void DisableCaller() => DisableHelper();

    /// <summary>Models the installed platform's small raw native enable implementation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnableHelper() => GL.Enable(EnableCap.CullFace);

    /// <summary>Models the installed platform's small raw native disable implementation.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DisableHelper() => GL.Disable(EnableCap.CullFace);
    #endregion
}
