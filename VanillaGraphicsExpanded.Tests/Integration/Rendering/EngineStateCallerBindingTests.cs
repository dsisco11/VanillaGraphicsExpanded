using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks that optimized callers keep native capability calls and cached state synchronized.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineStateCallerBindingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Optimized caller bodies continue through bound OpenGL entries and update the state cache.</summary>
    [Fact]
    public void OptimizedCullCallersKeepCacheAndNativeStateSynchronized()
    {
        // Arrange: Retain the original cull state and compile both optimized caller paths before observing cache behavior.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        bool previous = GL.IsEnabled(EnableCap.CullFace);
        try
        {
            // Compile both aggressive-optimization caller bodies before checking later bound calls.
            // Act: Alternate native optimized callers with cached requests that would be suppressed by stale knowledge.
            EnableCaller(); DisableCaller();
            cache.InvalidateAll();
            cache.SetCapability(EnableCap.CullFace, false);
            EnableCaller();
            // Assert: Each repeated cache request reaches the driver when the optimized caller changed native state.
            Assert.True(GL.IsEnabled(EnableCap.CullFace));
            // A stale cached false would suppress this requested disable and leave native state enabled.
            cache.SetCapability(EnableCap.CullFace, false);
            Assert.False(GL.IsEnabled(EnableCap.CullFace));
            cache.SetCapability(EnableCap.CullFace, true);
            DisableCaller();
            Assert.False(GL.IsEnabled(EnableCap.CullFace));
            // A stale cached true would suppress this requested enable and leave native state disabled.
            cache.SetCapability(EnableCap.CullFace, true);
            Assert.True(GL.IsEnabled(EnableCap.CullFace));
            cache.SetCapability(EnableCap.CullFace, false);
            Assert.False(GL.IsEnabled(EnableCap.CullFace));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            cache.InvalidateAll();
            cache.SetCapability(EnableCap.CullFace, previous);
        }
    }
    #endregion

    #region Private
    /// <summary>Keeps an aggressively optimized caller around its potentially inlined native enable helper.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void EnableCaller() => EnableHelper();

    /// <summary>Keeps the corresponding optimized disable caller separate from its helper.</summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
    private static void DisableCaller() => DisableHelper();

    /// <summary>Provides the native enable call that the optimized caller may inline.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void EnableHelper() => GL.Enable(EnableCap.CullFace);

    /// <summary>Provides the native disable call that the optimized caller may inline.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DisableHelper() => GL.Disable(EnableCap.CullFace);
    #endregion
}
