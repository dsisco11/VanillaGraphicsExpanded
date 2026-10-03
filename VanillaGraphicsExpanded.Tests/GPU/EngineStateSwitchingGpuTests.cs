using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes transpiled call sites against a real context and verifies cache/native agreement.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class EngineStateSwitchingGpuTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Binding a previously unknown VAO queries its existing element-buffer association.</summary>
    [Fact]
    public void UnknownVertexArrayPreservesExistingElementBuffer()
    {
        EnsureContextValid();
        int array = GL.GenVertexArray();
        int buffer = GL.GenBuffer();
        var cache = StateCache.Current;
        try
        {
            // Populate native VAO state before the cache observes this object.
            GL.BindVertexArray(array);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, buffer);
            GL.BindVertexArray(0);
            cache.InvalidateAll();
            EngineStateCalls.BindVertexArray(array);
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
        EnsureContextValid();
        var cache = StateCache.Current;
        int texture = GL.GenTexture();
        int sampler = GL.GenSampler();
        int buffer = GL.GenBuffer();
        int framebuffer = GL.GenFramebuffer();
        int array = GL.GenVertexArray();
        try
        {
            EngineStateCalls.ActiveTexture(TextureUnit.Texture0);
            EngineStateCalls.BindTexture(TextureTarget.Texture2D, texture);
            EngineStateCalls.BindSampler(0, sampler);
            EngineStateCalls.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            EngineStateCalls.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            EngineStateCalls.BindVertexArray(array);
            EngineStateCalls.DeleteTexture(texture);
            Assert.True(cache.TryGetCachedActiveTextureUnit(out var activeUnit));
            Assert.Equal(0, activeUnit);
            EngineStateCalls.DeleteSampler(sampler);
            Assert.True(cache.TryGetCachedCurrentVao(out var cachedArray));
            Assert.Equal(array, cachedArray);
            EngineStateCalls.DeleteBuffers(1, ref buffer);
            Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out var cachedFramebuffer));
            Assert.Equal(framebuffer, cachedFramebuffer);
            EngineStateCalls.DeleteFramebuffer(framebuffer);
            Assert.True(cache.TryGetCachedCurrentVao(out cachedArray));
            Assert.Equal(array, cachedArray);
            EngineStateCalls.DeleteVertexArray(array);
            Assert.True(cache.TryGetCachedActiveTextureUnit(out activeUnit));
            Assert.Equal(0, activeUnit);
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
    public void PatchedDepthCallRestoresPreviouslyCachedValue()
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        var harmony = Patch(nameof(ChangeDepth));
        try
        {
            cache.SetDepthFunc(DepthFunction.Less);
            ChangeDepth(DepthFunction.Greater);
            Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
            cache.SetDepthFunc(DepthFunction.Less);
            Assert.Equal((int)DepthFunction.Less, GL.GetInteger(GetPName.DepthFunc));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); cache.InvalidateAll(); }
    }

    /// <summary>Indexed changes invalidate global blend knowledge before a global restore.</summary>
    [Fact]
    public void IndexedBlendChangesCannotSuppressGlobalRestore()
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        var harmony = Patch(nameof(ChangeIndexedBlend));
        try
        {
            cache.SetBlendFunc(GlBlendFunc.Default);
            EngineStateCalls.Enable(EnableCap.Blend);
            ChangeIndexedBlend();
            EngineStateCalls.Enable(EnableCap.Blend);
            cache.SetBlendFunc(GlBlendFunc.Default);
            Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, 0));
            Assert.Equal((int)BlendingFactorSrc.One, GL.GetInteger(GetPName.BlendSrcRgb));
            Assert.Equal((int)BlendingFactorDest.Zero, GL.GetInteger(GetPName.BlendDstRgb));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); GL.Disable(EnableCap.Blend); cache.InvalidateAll(); }
    }

    /// <summary>Texture and sampler bindings remain independent through patched engine-style calls.</summary>
    [Fact]
    public void PatchedTextureBindingPreservesSamplerAndTracksActiveUnit()
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        int texture = GL.GenTexture();
        int sampler = GL.GenSampler();
        var harmony = Patch(nameof(ChangeTexture));
        try
        {
            cache.BindSampler(3, sampler);
            long bindsBefore = cache.TextureBindCount;
            ChangeTexture(texture);
            Assert.Equal(bindsBefore + 1, cache.TextureBindCount);
            Assert.True(cache.TryGetCachedActiveTextureUnit(out int unit));
            Assert.Equal(3, unit);
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 3, out int cachedTexture));
            Assert.Equal(texture, cachedTexture);
            Assert.Equal(texture, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(sampler, cache.GetBoundSampler(3));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            GL.DeleteTexture(texture);
            GL.DeleteSampler(sampler);
            GL.ActiveTexture(TextureUnit.Texture0);
            cache.InvalidateAll();
        }
    }

    /// <summary>An unknown active unit stays unknown while only potentially stale target snapshots are discarded.</summary>
    [Fact]
    public void UnknownActiveTextureUnitBindsWithoutQueryAndInvalidatesOnlyAffectedTarget()
    {
        EnsureContextValid();
        var cache = StateCache.Current;
        int previousTexture = GL.GenTexture();
        int nextTexture = GL.GenTexture();
        int cubeTexture = GL.GenTexture();
        int sampler = GL.GenSampler();
        try
        {
            cache.InvalidateAll();
            cache.BindTexture(TextureTarget.Texture2D, 1, previousTexture);
            cache.BindTexture(TextureTarget.Texture2D, 3, previousTexture);
            cache.BindTexture(TextureTarget.TextureCubeMap, 3, cubeTexture);
            cache.BindSampler(3, sampler);

            // Simulate losing only active-unit knowledge while retaining snapshots on multiple units.
            cache.Invalidate(EPipelineState.ActiveTextureUnit);
            long bindsBefore = cache.TextureBindCount;
            EngineStateCalls.BindTexture(TextureTarget.Texture2D, nextTexture);

            Assert.Equal(bindsBefore + 1, cache.TextureBindCount);
            Assert.False(cache.TryGetCachedActiveTextureUnit(out _));
            Assert.False(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 1, out _));
            Assert.False(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 3, out _));
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
        EnsureContextValid();
        var harmony = Patch(nameof(ChangeCapability));
        bool previous = GL.IsEnabled(EnableCap.Dither);
        try
        {
            ChangeCapability(true);
            Assert.True(GL.IsEnabled(EnableCap.Dither));
            ChangeCapability(false);
            Assert.False(GL.IsEnabled(EnableCap.Dither));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            if (previous) GL.Enable(EnableCap.Dither); else GL.Disable(EnableCap.Dither);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Installs the production transpiler on one isolated representative engine-style body.</summary>
    private static Harmony Patch(string method)
    {
        var harmony = new Harmony($"VGE.Tests.EngineStateSwitching.{method}");
        harmony.Patch(AccessTools.Method(typeof(EngineStateSwitchingGpuTests), method), transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        return harmony;
    }
    /// <summary>Provides a native call site that must remain uninlined for runtime patching.</summary>
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
    /// <summary>Exercises runtime capability selection through the same adapter.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ChangeCapability(bool enabled)
    {
        if (enabled) GL.Enable(EnableCap.Dither); else GL.Disable(EnableCap.Dither);
    }
    #endregion
}
