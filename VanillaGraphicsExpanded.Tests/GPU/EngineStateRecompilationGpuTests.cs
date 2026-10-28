using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that warmed higher callers reach native observation without caller rewriting.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineStateRecompilationGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API


    /// <summary>Warmed helper and higher-caller bodies retain native table dispatch without recompilation.</summary>
    [Fact]
    public void WarmCullCallersTrackBothTransitionsWithoutRewriting()
    {
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        bool previous = GL.IsEnabled(EnableCap.CullFace);
        var harmony = new Harmony($"vge.tests.cull.recompile.{Guid.NewGuid():N}");
        try
        {
            // Optimize the higher caller before changing its aggressively inlineable
            // helper. Its existing native enable then survives patching the helper.
            EnableCaller(); DisableCaller();
            cache.InvalidateAll();
            cache.SetCapability(EnableCap.CullFace, false);
            EnableCaller();
            Assert.True(GL.IsEnabled(EnableCap.CullFace));
            DisableCaller();
            // Exact engine calls cannot be suppressed by stale knowledge from an inlined caller.
            Assert.False(GL.IsEnabled(EnableCap.CullFace));
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
