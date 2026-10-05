using System.Collections;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks invalidation contracts without creating a GL context or issuing native commands.</summary>
public sealed class GlStateCacheInvalidationTests
{
    #region Public API
    /// <summary>Feedback invalidation preserves indexed storage-buffer knowledge while forgetting feedback-owned slots.</summary>
    [Fact]
    public void FeedbackInvalidationPreservesUnrelatedIndexedBindings()
    {
        var cache = CreateSeeded();
        var bindings = (IDictionary)Read(cache, "indexedBufferBindings")!;
        var bindingType = bindings.GetType().GetGenericArguments()[1];
        bindings.Add((BufferRangeTarget.TransformFeedbackBuffer, 0), Activator.CreateInstance(bindingType));
        bindings.Add((BufferRangeTarget.ShaderStorageBuffer, 1), Activator.CreateInstance(bindingType));
        cache.Invalidate(EPipelineState.TransformFeedback);
        Assert.Null(Read(cache, "currentTransformFeedback"));
        Assert.False(bindings.Contains((BufferRangeTarget.TransformFeedbackBuffer, 0)));
        Assert.True(bindings.Contains((BufferRangeTarget.ShaderStorageBuffer, 1)));
        Assert.Equal(3, Read(cache, "activeTextureUnit"));
        cache.Invalidate(EPipelineState.BufferBindings);
        Assert.Empty(bindings);
    }

    /// <summary>Each category forgets its own snapshots while preserving independent state.</summary>
    [Theory]
    [InlineData(EPipelineState.ActiveTextureUnit, "activeTextureUnit", "textureBindingsByUnit", "samplerBindingByUnit")]
    [InlineData(EPipelineState.TextureBindings, "textureBindingsByUnit", "activeTextureUnit", "samplerBindingByUnit")]
    [InlineData(EPipelineState.SamplerBindings, "samplerBindingByUnit", "activeTextureUnit", "textureBindingsByUnit")]
    [InlineData(EPipelineState.Program, "currentProgram", "currentProgramPipeline", "activeTextureUnit")]
    [InlineData(EPipelineState.FramebufferBindings, "currentReadFramebuffer", "currentRenderbuffer", "activeTextureUnit")]
    [InlineData(EPipelineState.RenderbufferBinding, "currentRenderbuffer", "currentDrawFramebuffer", "activeTextureUnit")]
    public void CategoriesPreserveIndependentKnowledge(EPipelineState state, string forgotten, string keptA, string keptB)
    {
        var cache = CreateSeeded();
        var a = Read(cache, keptA);
        var b = Read(cache, keptB);
        cache.Invalidate(state);
        Assert.Null(Read(cache, forgotten));
        Assert.Equal(a, Read(cache, keptA));
        Assert.Equal(b, Read(cache, keptB));
    }

    /// <summary>Fixed-function invalidation forgets only selected knowledge and preserves concrete values.</summary>
    [Theory]
    [InlineData(EPipelineState.Depth)]
    [InlineData(EPipelineState.Blend)]
    public void FixedFunctionCategoriesPreserveIndependentKnowledge(EPipelineState state)
    {
        var cache = CreateSeeded();
        cache.Invalidate(state);
        Assert.Equal(state != EPipelineState.Depth, ((DepthStateKnowledge)Read(cache, "depthKnown")!).HasFlag(DepthStateKnowledge.Comparison));
        Assert.All((BlendStateKnowledge[])Read(cache, "blendKnown")!, value =>
        {
            Assert.Equal(state != EPipelineState.Blend, value.HasFlag(BlendStateKnowledge.Enabled));
            Assert.Equal(state != EPipelineState.Blend, value.HasFlag(BlendStateKnowledge.Factors));
            Assert.True(value.HasFlag(BlendStateKnowledge.WriteMask));
        });
        Assert.Equal(DepthFunction.Less, ((DepthState)Read(cache, "depth")!).Comparison);
        Assert.Equal(3, Read(cache, "activeTextureUnit"));
        Assert.NotNull(Read(cache, "currentProgram"));
    }

    /// <summary>Empty and invalid masks cannot partially destroy known state.</summary>
    [Fact]
    public void NoneAndInvalidMasksPreserveKnowledge()
    {
        var cache = CreateSeeded();
        cache.Invalidate(EPipelineState.None);
        Assert.Equal(3, Read(cache, "activeTextureUnit"));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.Invalidate(EPipelineState.Depth | (EPipelineState)(1UL << 63)));
        Assert.Equal(DepthFunction.Less, ((DepthState)Read(cache, "depth")!).Comparison);
        Assert.True(((DepthStateKnowledge)Read(cache, "depthKnown")!).HasFlag(DepthStateKnowledge.Comparison));
        Assert.Equal(3, Read(cache, "activeTextureUnit"));
    }

    /// <summary>Combined invalidation clears every overlapping blend snapshot and both framebuffer directions.</summary>
    [Fact]
    public void CombinedCategoriesClearDependentSnapshots()
    {
        var cache = CreateSeeded();
        cache.Invalidate(EPipelineState.Depth | EPipelineState.Blend | EPipelineState.FramebufferBindings);
        foreach (string name in new[] { "currentFramebuffer", "currentReadFramebuffer", "currentDrawFramebuffer" })
            Assert.Null(Read(cache, name));
        Assert.Equal(default, (DepthStateKnowledge)Read(cache, "depthKnown")!);
        Assert.All((BlendStateKnowledge[])Read(cache, "blendKnown")!, value => { Assert.False(value.HasFlag(BlendStateKnowledge.Enabled)); Assert.False(value.HasFlag(BlendStateKnowledge.Factors)); Assert.True(value.HasFlag(BlendStateKnowledge.WriteMask)); });
        Assert.Equal(3, Read(cache, "activeTextureUnit"));
    }

    /// <summary>VAO and buffer invalidation both forget element associations but only VAO invalidation forgets selection.</summary>
    [Theory]
    [InlineData(EPipelineState.VertexArray, true)]
    [InlineData(EPipelineState.BufferBindings, false)]
    public void ElementBufferDependenciesAreInvalidated(EPipelineState state, bool forgetVao)
    {
        var cache = CreateSeeded();
        cache.Invalidate(state);
        Assert.Empty((IDictionary)Read(cache, "elementArrayBufferByVao")!);
        Assert.Equal(forgetVao ? null : (object)7, Read(cache, "currentVao"));
        Assert.Equal(3, Read(cache, "activeTextureUnit"));
    }

    /// <summary>All and the compatibility entry point clear every seeded mutable category.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllClearsMutableState(bool compatibility)
    {
        var cache = CreateSeeded();
        if (compatibility) cache.InvalidateAll(); else cache.Invalidate(EPipelineState.All);
        foreach (var field in typeof(StateCache).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (Nullable.GetUnderlyingType(field.FieldType) is not null && field.Name != "storageBufferOffsetAlignment")
                Assert.Null(field.GetValue(cache));
            if (field.GetValue(cache) is IDictionary dictionary) Assert.Empty(dictionary);
        }
        Assert.Null(Read(cache, "textureBindingsByUnit"));
        Assert.Null(Read(cache, "samplerBindingByUnit"));
        Assert.Equal(default, (DepthStateKnowledge)Read(cache, "depthKnown")!);
        Assert.All((BlendStateKnowledge[])Read(cache, "blendKnown")!, value => Assert.Equal(default, value));
    }
    #endregion

    #region Private
    /// <summary>Seeds snapshots directly so tests cannot depend on native GL availability.</summary>
    private static StateCache CreateSeeded()
    {
        var cache = (StateCache)Activator.CreateInstance(typeof(StateCache), nonPublic: true)!;
        foreach (var field in typeof(StateCache).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            var type = Nullable.GetUnderlyingType(field.FieldType);
            if (type is not null) field.SetValue(cache, Activator.CreateInstance(type));
        }
        Set(cache, "activeTextureUnit", 3);
        Set(cache, "currentVao", 7);
        Set(cache, "depth", new DepthState { Comparison = DepthFunction.Less });
        Set(cache, "depthKnown", DepthStateKnowledge.All);
        Set(cache, "textureBindingsByUnit", new Dictionary<TextureTarget, int>[] { new() { [TextureTarget.Texture2D] = 5 } });
        Set(cache, "samplerBindingByUnit", new int?[] { 6 });
        Set(cache, "blend", new BlendState[] { new() { Enabled = true, Factors = GlBlendFunc.Default }, new() { Enabled = false, Factors = GlBlendFunc.Default } });
        Set(cache, "blendKnown", new BlendStateKnowledge[] { BlendStateKnowledge.All, BlendStateKnowledge.All });
        ((Dictionary<int, int>)Read(cache, "elementArrayBufferByVao")!)[7] = 8;
        return cache;
    }

    /// <summary>Reads private state for context-free contract checks.</summary>
    private static object? Read(StateCache cache, string name) => typeof(StateCache).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache);

    /// <summary>Seeds private state without mutating the native context.</summary>
    private static void Set(StateCache cache, string name, object value) => typeof(StateCache).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(cache, value);
    #endregion
}
