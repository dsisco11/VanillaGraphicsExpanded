using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises native OpenGL call sites against a live context and verifies cache/native agreement.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class EngineStateSwitchingGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Exact engine state calls execute even when the managed cache already knows the same value.</summary>
    [Fact]
    public void RepeatedEngineDepthMasksAreNeverSuppressed()
    {
        // Arrange: Seed the managed depth-mask cache with the same value the native calls will repeat.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        cache.SetDepthWriteMask(true);
        using var calls = new BindingCallCapture();
        // Managed equality cannot suppress an intercepted call or alter its observable native effects.
        // Act: Submit two identical intercepted engine calls while cached state already matches.
        GL.DepthMask(true);
        GL.DepthMask(true);
        // Assert: Both calls reach the driver and the native mask remains enabled.
        Assert.Equal(2, calls.Calls(nameof(GL.DepthMask)));
        Assert.True(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Invalid native arguments preserve native rejection rather than becoming a managed validation error.</summary>
    [Fact]
    public void EngineViewportPreservesNativeErrorsAndArguments()
    {
        // Arrange: Establish a valid viewport and matching cache state before sending invalid native arguments.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        cache.ApplyDynamic(new VanillaGraphicsExpanded.Rendering.Pipeline.State.DynamicDrawState
            { X = 0, Y = 0, Width = 16, Height = 16 });
        try
        {
            // Negative width is rejected by GL and must never be clamped or rejected by the adapter itself.
            // Act: Submit a rejected negative width, then a valid viewport through the same adapter.
            GL.Viewport(0, 0, -1, 16);
            // Assert: GL owns rejection semantics and retains the previous viewport until the valid update.
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            var viewport = new int[4];
            GL.GetInteger(GetPName.Viewport, viewport);
            Assert.Equal(new[] { 0, 0, 16, 16 }, viewport);
            cache.ApplyDynamic(new VanillaGraphicsExpanded.Rendering.Pipeline.State.DynamicDrawState
                { X = 0, Y = 0, Width = 8, Height = 8 });
            GL.GetInteger(GetPName.Viewport, viewport);
            Assert.Equal(new[] { 0, 0, 8, 8 }, viewport);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.Invalidate(EPipelineState.Viewport); }
    }

    /// <summary>Binding a previously unknown VAO queries its existing element-buffer association.</summary>
    [Fact]
    public void UnknownVertexArrayPreservesExistingElementBuffer()
    {
        // Arrange: Associate an element buffer with a VAO before invalidating the cache's knowledge of that object.
        EnsureContextValid();
        int array = GL.GenVertexArray();
        int buffer = GL.GenBuffer();
        var cache = OpenGlStateCache.Current;
        try
        {
            // Populate native VAO state before the cache observes this object.
            // Act: Rebind the previously unknown VAO so the cache must recover its existing element-buffer association.
            GL.BindVertexArray(array);
            // Assert: Cache and driver both report the association established before observation.
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
            GL.BindVertexArray(0);
            cache.InvalidateAll();
            GL.BindVertexArray(array);
            Assert.Equal(buffer, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(buffer, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteVertexArray(array); GL.DeleteBuffer(buffer); cache.InvalidateAll(); }
    }

    /// <summary>Deleting bound engine objects invalidates snapshots before the next cached observation.</summary>
    [Fact]
    public void RetiredBindingsResolveToNativeZero()
    {
        // Arrange: Bind texture, sampler, buffer, framebuffer, and VAO names before deleting them in sequence.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int texture = GL.GenTexture();
        int sampler = GL.GenSampler();
        int buffer = GL.GenBuffer();
        int framebuffer = GL.GenFramebuffer();
        int array = GL.GenVertexArray();
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.BindSampler(0, sampler);
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.BindVertexArray(array);
            // Exact external selectors leave affected facts unknown until a consumer recovers them.
            Assert.Equal(array, cache.GetCurrentVao());
            Assert.Equal(framebuffer, cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
            // Act: Delete each bound name through native engine call sites, then query the resulting binding state.
            GL.DeleteTexture(texture);
            Assert.True(cache.TryGetCachedActiveTextureUnit(out var activeUnit));
            Assert.Equal(0, activeUnit);
            GL.DeleteSampler(sampler);
            Assert.True(cache.TryGetCachedCurrentVao(out var cachedArray));
            Assert.Equal(array, cachedArray);
            GL.DeleteBuffers(1, ref buffer);
            Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out var cachedFramebuffer));
            Assert.Equal(framebuffer, cachedFramebuffer);
            GL.DeleteFramebuffer(framebuffer);
            Assert.True(cache.TryGetCachedCurrentVao(out cachedArray));
            Assert.Equal(array, cachedArray);
            GL.DeleteVertexArray(array);
            Assert.True(cache.TryGetCachedActiveTextureUnit(out activeUnit));
            Assert.Equal(0, activeUnit);
            // Assert: Cached observations resolve all retired bindings to zero after their native deletion.
            Assert.Equal(0, cache.GetBoundTexture(TextureTarget.Texture2D, 0));
            Assert.Equal(0, cache.GetBoundSampler(0));
            Assert.Equal(0, cache.GetBoundBuffer(BufferTarget.ArrayBuffer));
            Assert.Equal(0, cache.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
            Assert.Equal(0, cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(0, cache.GetCurrentVao());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.DeleteTexture(texture); GL.DeleteSampler(sampler); GL.DeleteBuffer(buffer);
            GL.DeleteFramebuffer(framebuffer); GL.DeleteVertexArray(array);
            cache.InvalidateAll();
        }
    }

    /// <summary>Native depth mutations update the cache so a subsequent cached restore cannot be suppressed.</summary>
    [Fact]
    public void NativeDepthCallRestoresPreviouslyCachedValue()
    {
        // Arrange: Cache the less-than depth function before changing the native driver value directly.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        try
        {
            // Act: Change the driver to greater, then restore less through the managed cache API.
            cache.SetDepthFunc(DepthFunction.Less);
            ChangeDepth(DepthFunction.Greater);
            // Assert: Both the external mutation and requested restoration are visible on the driver.
            Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
            cache.SetDepthFunc(DepthFunction.Less);
            Assert.Equal((int)DepthFunction.Less, GL.GetInteger(GetPName.DepthFunc));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache.InvalidateAll(); }
    }

    /// <summary>Indexed changes invalidate global blend knowledge before a global restore.</summary>
    [Fact]
    public void IndexedBlendChangesCannotSuppressGlobalRestore()
    {
        // Arrange: Cache global blend defaults before applying different indexed output state.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        try
        {
            // Act: Change one indexed output externally, then restore the global defaults through the cache.
            cache.SetBlendFunc(GlBlendFunc.Default);
            GL.Enable(EnableCap.Blend);
            ChangeIndexedBlend();
            GL.Enable(EnableCap.Blend);
            cache.SetBlendFunc(GlBlendFunc.Default);
            // Assert: Indexed state and global factors both match the requested restored values.
            Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 0));
            Assert.Equal((int)BlendingFactorSrc.One, GL.GetInteger(GetPName.BlendSrcRgb));
            Assert.Equal((int)BlendingFactorDest.Zero, GL.GetInteger(GetPName.BlendDstRgb));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.Disable(EnableCap.Blend); cache.InvalidateAll(); }
    }

    /// <summary>Texture and sampler bindings remain independent through native OpenTK calls.</summary>
    [Fact]
    public void NativeTextureBindingPreservesSamplerAndTracksActiveUnit()
    {
        // Arrange: Bind a sampler on unit three before the native texture call changes active-unit selection.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int texture = GL.GenTexture();
        int sampler = GL.GenSampler();
        try
        {
            OpenGLImageBindings.BindSampler(cache, 3, sampler);
#if DEBUG
            long bindsBefore = cache.TextureBindCount;
#endif
            // Act: Select unit three and bind a texture through the native OpenTK caller.
            ChangeTexture(texture);
#if DEBUG
            Assert.Equal(bindsBefore + 1, cache.TextureBindCount);
#endif
            // Assert: The texture and active unit are tracked without disturbing the independently bound sampler.
            Assert.True(cache.TryGetCachedActiveTextureUnit(out int unit));
            Assert.Equal(3, unit);
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 3, out int observedTexture));
            Assert.Equal(texture, observedTexture);
            Assert.Equal(texture, cache.GetBoundTexture(TextureTarget.Texture2D, 3));
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, cache.GetBoundSampler(3));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.DeleteTexture(texture);
            GL.DeleteSampler(sampler);
            GL.ActiveTexture(TextureUnit.Texture0);
            cache.InvalidateAll();
        }
    }

    /// <summary>An initially missing active unit is acquired while unrelated units and targets remain known.</summary>
    [Fact]
    public void UnknownActiveTextureUnitIsAcquiredWithoutDiscardingOtherUnits()
    {
        // Arrange: Cache bindings on units one and three, then invalidate only active-unit knowledge.
        EnsureContextValid();
        var cache = OpenGlStateCache.Current;
        int previousTexture = GL.GenTexture();
        int nextTexture = GL.GenTexture();
        int cubeTexture = GL.GenTexture();
        int sampler = GL.GenSampler();
        try
        {
            cache.InvalidateAll();
            OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, 1, previousTexture);
            OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, 3, previousTexture);
            OpenGLImageBindings.BindTexture(cache, TextureTarget.TextureCubeMap, 3, cubeTexture);
            OpenGLImageBindings.BindSampler(cache, 3, sampler);

            // Simulate losing only active-unit knowledge while retaining snapshots on multiple units.
            cache.Invalidate(EPipelineState.ActiveTextureUnit);
#if DEBUG
            long bindsBefore = cache.TextureBindCount;
#endif
            // Act: Bind on the native current unit, forcing only that missing selector to be acquired.
            GL.BindTexture(TextureTarget.Texture2D, nextTexture);

#if DEBUG
            Assert.Equal(bindsBefore + 1, cache.TextureBindCount);
#endif
            // Assert: Unit three updates while unit-one and cube-map snapshots remain intact.
            Assert.True(cache.TryGetCachedActiveTextureUnit(out int activeUnit));
            Assert.Equal(3, activeUnit);
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 1, out int unchanged));
            Assert.Equal(previousTexture, unchanged);
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 3, out int observedTexture));
            Assert.Equal(nextTexture, observedTexture);
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.TextureCubeMap, 3, out int cachedCube));
            Assert.Equal(cubeTexture, cachedCube);
            Assert.Equal((int)TextureUnit.Texture3, GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(nextTexture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, GL.GetInteger(GetPName.SamplerBinding));
            GL.ActiveTexture(TextureUnit.Texture1);
            Assert.Equal(previousTexture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.DeleteTexture(previousTexture);
            GL.DeleteTexture(nextTexture);
            GL.DeleteTexture(cubeTexture);
            GL.DeleteSampler(sampler);
            GL.ActiveTexture(TextureUnit.Texture0);
            cache.InvalidateAll();
        }
    }

    /// <summary>Capabilities outside the cache's supported set retain their native behavior.</summary>
    [Fact]
    public void UnsupportedCapabilityPassesThrough()
    {
        // Arrange: Retain the original Dither state, a capability outside the cache's managed set.
        EnsureContextValid();
        bool previous = GL.IsEnabled(EnableCap.Dither);
        try
        {
            // Act: Enable then disable Dither through native bound entry points.
            ChangeCapability(true);
            // Assert: The driver applies both calls and reports no GL error.
            Assert.True(GL.IsEnabled(EnableCap.Dither));
            ChangeCapability(false);
            Assert.False(GL.IsEnabled(EnableCap.Dither));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (previous) GL.Enable(EnableCap.Dither); else GL.Disable(EnableCap.Dither);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Keeps the native call at a distinct OpenTK binding call site.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChangeDepth(DepthFunction function) => GL.DepthFunc(function);
    /// <summary>Changes indexed state after a global state has already been cached.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChangeIndexedBlend()
    {
        GL.Disable(IndexedEnableCap.Blend, 0);
        GL.BlendFunc(0, BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
    }
    /// <summary>Selects a texture without changing the sampler independently owned by the caller.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChangeTexture(int texture)
    {
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, texture);
    }
    /// <summary>Exercises native capability calls through the bound OpenTK entry points.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChangeCapability(bool enabled)
    {
        if (enabled) GL.Enable(EnableCap.Dither); else GL.Disable(EnableCap.Dither);
    }
    #endregion
}
