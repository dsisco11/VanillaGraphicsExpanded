using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Checks categorized transitions against the native context, including all output aliases.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CategorizedStateCacheTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Blending and scalar state
    /// <summary>Mixed indexed values cannot suppress a global transition, including the highest supported output.</summary>
    [Fact]
    public void GlobalIndexedAliasesAndIndependentCopies()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        int last = cache.MaxDrawBuffers - 1;
        var alpha = new GlBlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha,
            BlendingFactorSrc.One, BlendingFactorDest.Zero);
        var red = GlColorMask.FromRgba(true, false, false, false);
        try
        {
            cache.SetBlendEnabled(true);
            cache.SetBlendFunc(GlBlendFunc.Default);
            cache.SetColorMask(GlColorMask.All);
            cache.SetBlendEnabledIndexed(last, false);
            cache.SetBlendFuncIndexed(last, alpha);
            cache.SetColorMaskIndexed(last, red);
            Assert.False(GL.IsEnabled(IndexedEnableCap.Blend, last));
            AssertOutput(last, alpha, new[] { true, false, false, false });
            AssertOutput(0, GlBlendFunc.Default, new[] { true, true, true, true });
            var saved = cache.CopyBlendValues();
#if DEBUG
            long calls = cache.FixedFunctionCalls;
#endif
            cache.SetBlendEnabledIndexed(last, false);
            cache.SetBlendFuncIndexed(last, alpha);
            cache.SetColorMaskIndexed(last, red);
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            cache.SetBlendEnabled(true);
            cache.SetBlendFunc(GlBlendFunc.Default);
            cache.SetColorMask(GlColorMask.All);
            for (int i = 0; i < cache.MaxDrawBuffers; i++)
            {
                Assert.True(GL.IsEnabled(IndexedEnableCap.Blend, i));
                AssertOutput(i, GlBlendFunc.Default, new[] { true, true, true, true });
            }
            Assert.False(saved[last].Enabled);
            Assert.Equal(alpha, saved[last].Factors);
            Assert.Equal(red, saved[last].WriteMask);
#if DEBUG
            calls = cache.FixedFunctionCalls;
#endif
            cache.SetBlendEnabled(true);
        cache.SetBlendFunc(GlBlendFunc.Default);
        cache.SetColorMask(GlColorMask.All);
#if DEBUG
            Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetColorMaskIndexed(cache.MaxDrawBuffers, red));
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        }
        finally { cache.SetBlendEnabled(false);
        cache.SetColorMask(GlColorMask.All);
        cache.InvalidateAll(); }
    }

    /// <summary>Scalar values agree with native state and retain independent validity.</summary>
    [Fact]
    public void ScalarFamiliesMatchNativeAndSuppressRepeats()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        Action establish = () =>
        {
            cache.SetCapability(EnableCap.DepthTest, true);
        cache.SetDepthFunc(DepthFunction.Greater);
        cache.SetDepthWriteMask(false);
            cache.SetCapability(EnableCap.CullFace, false);
        cache.SetCapability(EnableCap.ScissorTest, false);
            cache.SetLineWidth(1);
        cache.SetPointSize(2);
        cache.SetProvokingVertex(ProvokingVertexMode.FirstVertexConvention);
        cache.SetPatchVertices(4);
        };
        establish();
#if DEBUG
        long calls = cache.FixedFunctionCalls;
#endif
        establish();
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        Assert.True(GL.IsEnabled(EnableCap.DepthTest));
        Assert.False(GL.IsEnabled(EnableCap.CullFace));
        Assert.False(GL.IsEnabled(EnableCap.ScissorTest));
        Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
        Assert.False(GL.GetBoolean(GetPName.DepthWritemask));
        Assert.Equal(1f, GL.GetFloat(GetPName.LineWidth));
        Assert.Equal(2f, GL.GetFloat(GetPName.PointSize));
        Assert.Equal((int)ProvokingVertexMode.FirstVertexConvention, GL.GetInteger(GetPName.ProvokingVertex));
        Assert.Equal(4, GL.GetInteger(GetPName.PatchVertices));
        // Invalid requests must neither issue GL calls nor overwrite established knowledge.
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetDepthFunc((DepthFunction)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetProvokingVertex((ProvokingVertexMode)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetLineWidth(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetPointSize(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetPatchVertices(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetPatchVertices(GL.GetInteger(GetPName.MaxPatchVertices) + 1));
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        Assert.True(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison));
        Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
        Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        cache.Invalidate(EPipelineState.LineWidth | EPipelineState.PatchVertices);
        Assert.False(Read<RasterizerStateKnowledge>(cache, "rasterizerKnown").HasFlag(RasterizerStateKnowledge.LineWidth));
        Assert.True(Read<RasterizerStateKnowledge>(cache, "rasterizerKnown").HasFlag(RasterizerStateKnowledge.PointSize));
        Assert.True(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.WriteEnabled)); establish();
#if DEBUG
        Assert.Equal(calls + 2, cache.FixedFunctionCalls);
#endif
        cache.SetCapability(EnableCap.DepthTest, false);
        cache.SetDepthWriteMask(true);
        cache.SetDepthFunc(DepthFunction.Less);
        cache.SetPointSize(1);
        cache.SetPatchVertices(3);
        cache.SetProvokingVertex(ProvokingVertexMode.LastVertexConvention);
        cache.InvalidateAll();
    }

    #endregion

    #region Dynamic state and clear operations
    /// <summary>Observed dynamic/helper changes preserve independent validity and repeated-call suppression.</summary>
    [Fact]
    public void ViewportClearColorAndSelectiveKnowledge()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        EngineStateCalls.Viewport(3, 5, 17, 19);
        EngineStateCalls.ClearColor(.2f, .3f, .4f, .5f);
        EngineStateCalls.DepthFunc(DepthFunction.Greater);
        Assert.Equal(new[] { 3, 5, 17, 19 }, Viewport());
        var clear = new float[4];
        GL.GetFloat(GetPName.ColorClearValue, clear);
        Assert.Equal(new[] { .2f, .3f, .4f, .5f }, clear);
        // Exact legal external effects establish their own fields without inventing siblings.
        Assert.Equal(DepthStateKnowledge.Comparison, Read<DepthStateKnowledge>(cache, "depthKnown"));
        cache.SetDepthFunc(DepthFunction.Greater);
        cache.ApplyDynamic(new DynamicDrawState { X = 3, Y = 5, Width = 17, Height = 19 });
        cache.SetClearColor(.2f, .3f, .4f, .5f);
        var knowledge = Read<DepthStateKnowledge>(cache, "depthKnown");
        Assert.True(knowledge.HasFlag(DepthStateKnowledge.Comparison));
        Assert.False(knowledge.HasFlag(DepthStateKnowledge.TestEnabled));
        Assert.False(knowledge.HasFlag(DepthStateKnowledge.WriteEnabled));
#if DEBUG
        long calls = cache.FixedFunctionCalls;
#endif
        cache.ApplyDynamic(new DynamicDrawState { X = 3, Y = 5, Width = 17, Height = 19 });
        cache.SetClearColor(.2f, .3f, .4f, .5f);
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        cache.Invalidate(EPipelineState.Depth);
        Assert.True(Read<DynamicDrawStateKnowledge>(cache, "dynamicKnown").HasFlag(DynamicDrawStateKnowledge.Viewport));
        cache.Invalidate(EPipelineState.Viewport);
        Assert.True(Read<bool>(cache, "clearColorKnown"));
        cache.ApplyDynamic(new DynamicDrawState { X = 3, Y = 5, Width = 17, Height = 19 });
#if DEBUG
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
#endif
        cache.ApplyDynamic(new DynamicDrawState { Width = 1, Height = 1 });
        Assert.Equal(new[] { 0, 0, 1, 1 }, Viewport());
        Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        cache.InvalidateAll();
    }

    /// <summary>Clamped viewport requests and clear values match actual native storage.</summary>
    [Fact]
    public void ClampedViewportAndOutOfRangeClearMatchNative()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        int[] limit = new int[2];
        GL.GetInteger(GetPName.MaxViewportDims, limit);
        var request = new DynamicDrawState { Width = limit[0] + 1, Height = limit[1] + 1 };
        cache.ApplyDynamic(request);
        Assert.Equal(new[] { 0, 0, limit[0], limit[1] }, Viewport());
#if DEBUG
        long calls = cache.FixedFunctionCalls;
#endif
        cache.ApplyDynamic(request);
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        cache.SetClearColor(-2, 3, .5f, 4);
        float[] actual = new float[4];
        GL.GetFloat(GetPName.ColorClearValue, actual);
        var cached = Read<OpenTK.Mathematics.Vector4>(cache, "clearColor");
        Assert.Equal(new[] { cached.X, cached.Y, cached.Z, cached.W }, actual);
#if DEBUG
        calls = cache.FixedFunctionCalls;
#endif
        cache.SetClearColor(-2, 3, .5f, 4);
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        cache.ApplyDynamic(new DynamicDrawState { Width = 1, Height = 1 });
        cache.SetClearColor(0, 0, 0, 0);
        cache.InvalidateAll();
    }

    #endregion

    #region Cache lifetime
    /// <summary>Repeated cache access preserves knowledge until the owner explicitly invalidates it.</summary>
    [Fact]
    public void ExplicitInvalidationResolvesKnowledgeAgain()
    {
        fixture.MakeCurrent();
        var cache = OpenGlStateCache.Current;
        cache.InvalidateAll();
        cache.SetDepthFunc(DepthFunction.Greater);
#if DEBUG
        long calls = cache.FixedFunctionCalls;
#endif
        Assert.Same(cache, OpenGlStateCache.Current);
        OpenGlStateCache.Current.SetDepthFunc(DepthFunction.Greater);
#if DEBUG
        Assert.Equal(calls, cache.FixedFunctionCalls);
#endif
        Assert.True(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison));
        cache.InvalidateAll();
        Assert.False(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison));
        cache.SetDepthFunc(DepthFunction.Greater);
#if DEBUG
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
#endif
        Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
        cache.SetDepthFunc(DepthFunction.Less);
        cache.InvalidateAll();
    }
    #endregion

    #endregion
    #region Private
    /// <summary>Queries independent RGB/alpha factors and the output write mask from the driver.</summary>
    private static void AssertOutput(int index, GlBlendFunc factors, bool[] mask)
    {
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcRgb, index, out int srcRgb);
        GL.GetInteger((GetIndexedPName)GetPName.BlendDstRgb, index, out int dstRgb);
        GL.GetInteger((GetIndexedPName)GetPName.BlendSrcAlpha, index, out int srcAlpha);
        GL.GetInteger((GetIndexedPName)GetPName.BlendDstAlpha, index, out int dstAlpha);
        Assert.Equal((int)factors.SrcRgb, srcRgb);
        Assert.Equal((int)factors.DstRgb, dstRgb);
        Assert.Equal((int)factors.SrcAlpha, srcAlpha);
        Assert.Equal((int)factors.DstAlpha, dstAlpha);
        bool[] actual = new bool[4];
        GL.GetBoolean(GetIndexedPName.ColorWritemask, index, actual);
        Assert.Equal(mask, actual);
    }
    /// <summary>Reads the actual native viewport rectangle.</summary>
    private static int[] Viewport()
    {
        int[] values = new int[4];
        GL.GetInteger(GetPName.Viewport, values); return values;
    }
    /// <summary>Inspects validity without causing a query to resolve unknown fields.</summary>
    private static T Read<T>(OpenGlStateCache cache, string name) =>
        (T)typeof(OpenGlStateCache).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
    #endregion
}
