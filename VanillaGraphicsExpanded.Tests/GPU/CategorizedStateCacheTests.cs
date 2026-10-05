using System.Reflection;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
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
        var cache = StateCache.Current;
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
            long calls = cache.FixedFunctionCalls;
            cache.SetBlendEnabledIndexed(last, false);
            cache.SetBlendFuncIndexed(last, alpha);
            cache.SetColorMaskIndexed(last, red);
            Assert.Equal(calls, cache.FixedFunctionCalls);
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
            calls = cache.FixedFunctionCalls;
            cache.SetBlendEnabled(true);
        cache.SetBlendFunc(GlBlendFunc.Default);
        cache.SetColorMask(GlColorMask.All);
            Assert.Equal(calls, cache.FixedFunctionCalls);
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
        var cache = StateCache.Current;
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
        long calls = cache.FixedFunctionCalls; establish();
        Assert.Equal(calls, cache.FixedFunctionCalls);
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
        Assert.Equal(calls, cache.FixedFunctionCalls);
        Assert.True(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison));
        Assert.Equal((int)DepthFunction.Greater, GL.GetInteger(GetPName.DepthFunc));
        Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        cache.Invalidate(EPipelineState.LineWidth | EPipelineState.PatchVertices);
        Assert.False(Read<RasterizerStateKnowledge>(cache, "rasterizerKnown").HasFlag(RasterizerStateKnowledge.LineWidth));
        Assert.True(Read<RasterizerStateKnowledge>(cache, "rasterizerKnown").HasFlag(RasterizerStateKnowledge.PointSize));
        Assert.True(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.WriteEnabled)); establish();
        Assert.Equal(calls + 2, cache.FixedFunctionCalls);
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
        var cache = StateCache.Current;
        cache.InvalidateAll();
        EngineStateCalls.Viewport(3, 5, 17, 19);
        EngineStateCalls.ClearColor(.2f, .3f, .4f, .5f);
        EngineStateCalls.DepthFunc(DepthFunction.Greater);
        Assert.Equal(new[] { 3, 5, 17, 19 }, Viewport());
        var clear = new float[4];
        GL.GetFloat(GetPName.ColorClearValue, clear);
        Assert.Equal(new[] { .2f, .3f, .4f, .5f }, clear);
        var knowledge = Read<DepthStateKnowledge>(cache, "depthKnown");
        Assert.True(knowledge.HasFlag(DepthStateKnowledge.Comparison));
        Assert.False(knowledge.HasFlag(DepthStateKnowledge.TestEnabled));
        Assert.False(knowledge.HasFlag(DepthStateKnowledge.WriteEnabled));
        long calls = cache.FixedFunctionCalls;
        EngineStateCalls.Viewport(3, 5, 17, 19);
        EngineStateCalls.ClearColor(.2f, .3f, .4f, .5f);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        cache.Invalidate(EPipelineState.Depth);
        Assert.True(Read<DynamicDrawStateKnowledge>(cache, "dynamicKnown").HasFlag(DynamicDrawStateKnowledge.Viewport));
        cache.Invalidate(EPipelineState.Viewport);
        Assert.True(Read<bool>(cache, "clearColorKnown"));
        EngineStateCalls.Viewport(3, 5, 17, 19);
        Assert.Equal(calls + 1, cache.FixedFunctionCalls);
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
        var cache = StateCache.Current;
        cache.InvalidateAll();
        int[] limit = new int[2];
        GL.GetInteger(GetPName.MaxViewportDims, limit);
        var request = new DynamicDrawState { Width = limit[0] + 1, Height = limit[1] + 1 };
        cache.ApplyDynamic(request);
        Assert.Equal(new[] { 0, 0, limit[0], limit[1] }, Viewport());
        long calls = cache.FixedFunctionCalls;
        cache.ApplyDynamic(request);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        cache.SetClearColor(-2, 3, .5f, 4);
        float[] actual = new float[4];
        GL.GetFloat(GetPName.ColorClearValue, actual);
        var cached = Read<OpenTK.Mathematics.Vector4>(cache, "clearColor");
        Assert.Equal(new[] { cached.X, cached.Y, cached.Z, cached.W }, actual);
        calls = cache.FixedFunctionCalls;
        cache.SetClearColor(-2, 3, .5f, 4);
        Assert.Equal(calls, cache.FixedFunctionCalls);
        cache.ApplyDynamic(new DynamicDrawState { Width = 1, Height = 1 });
        cache.SetClearColor(0, 0, 0, 0);
        cache.InvalidateAll();
    }

    #endregion

    #region Context lifetime
    /// <summary>A native context switch and explicit same-handle retirement both withdraw prior knowledge.</summary>
    [Fact]
    public unsafe void ContextSwitchAndHandleReregistrationResetKnowledge()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        var original = GLFW.GetCurrentContext();
        long first = cache.ContextGeneration;
        Assert.True(first > 0);
        cache.SetDepthFunc(DepthFunction.Greater);
        int limit = cache.MaxDrawBuffers;
        long queries = GpuSupport.CaptureCount;
        GLFW.MakeContextCurrent(null);
        try
        {
            Assert.Equal(0, cache.ContextGeneration);
            Assert.False(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison));
        }
        finally { GLFW.MakeContextCurrent(original); }
        Assert.Equal(first, cache.ContextGeneration);
        Assert.False(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison));
        Assert.Equal(limit, cache.MaxDrawBuffers);
        Assert.Equal(queries, GpuSupport.CaptureCount);
        RenderContextRegistry.Retire(fixture);
        Assert.Equal(0, cache.ContextGeneration);
        long replacement = RenderContextRegistry.RegisterCurrent(fixture, static owner => ((HeadlessGLFixture)owner).IsContextValid);
        Assert.True(replacement > first);
        Assert.Equal(replacement, cache.ContextGeneration);
        Assert.Equal(limit, cache.MaxDrawBuffers);
        Assert.Equal(queries + 1, GpuSupport.CaptureCount);
        cache.SetDepthFunc(DepthFunction.Less);
        Assert.Equal((int)DepthFunction.Less, GL.GetInteger(GetPName.DepthFunc));
        cache.InvalidateAll();
    }
    /// <summary>A disposed owner withdraws authority while its native handle remains current.</summary>
    [Fact]
    public void DisposedOwnerWithdrawsRegistration()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current; object owner = new(); bool alive = true;
        RenderContextRegistry.RegisterCurrent(owner, _ => alive);
        cache.SetDepthFunc(DepthFunction.Greater); alive = false;
        try { Assert.Equal(0, cache.ContextGeneration);
        Assert.False(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison)); }
        finally
        {
            RenderContextRegistry.Retire(owner); RenderContextRegistry.RegisterCurrent(fixture, static target => ((HeadlessGLFixture)target).IsContextValid);
            cache.SetDepthFunc(DepthFunction.Less);
        cache.InvalidateAll();
        }
    }
    /// <summary>A genuinely different native context cannot inherit prior scalar knowledge or limits.</summary>
    [Fact]
    public unsafe void ReplacementNativeContextResolvesItsOwnKnowledge()
    {
        fixture.MakeCurrent();
        var cache = StateCache.Current;
        var original = GLFW.GetCurrentContext();
        GLFW.WindowHint(WindowHintBool.Visible, false);
        var replacement = GLFW.CreateWindow(1, 1, "State replacement test", null, null);
        Assert.True(replacement != null); object owner = new();
        long previous = cache.ContextGeneration;
        try
        {
            cache.SetDepthFunc(DepthFunction.Greater);
            GLFW.MakeContextCurrent(replacement);
            long generation = RenderContextRegistry.RegisterCurrent(owner, static _ => true);
            Assert.True(generation > previous);
        Assert.Equal(generation, cache.ContextGeneration);
            Assert.False(Read<DepthStateKnowledge>(cache, "depthKnown").HasFlag(DepthStateKnowledge.Comparison));
            Assert.Equal(GL.GetInteger(GetPName.MaxDrawBuffers), cache.MaxDrawBuffers);
            cache.SetDepthFunc(DepthFunction.Less);
        Assert.Equal((int)DepthFunction.Less, GL.GetInteger(GetPName.DepthFunc));
        }
        finally
        {
            RenderContextRegistry.Retire(owner); GLFW.MakeContextCurrent(original); GLFW.DestroyWindow(replacement);
            cache.SetDepthFunc(DepthFunction.Less);
        cache.InvalidateAll();
        }
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
    private static T Read<T>(StateCache cache, string name) =>
        (T)typeof(StateCache).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
    #endregion
}
