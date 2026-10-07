using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks temporal origin history uploaded by registered normal and debug render callbacks.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class LumOnTemporalRendererTests : RenderTestBase
{
    /// <summary>Uses the shared material-isolated graphics context.</summary>
    public LumOnTemporalRendererTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Frame history
    /// <summary>Moving the camera rebases the committed raw matrix in the actual uploaded frame buffer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegisteredRendererUploadsRebasedHistory(bool debug)
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false, scene);
        runtime.Cache.Config.LumOn.DebugMode = debug ? LumOnDebugMode.WorldProbeConfidence : LumOnDebugMode.Off;
        Type type = debug ? typeof(LumOnDebugRenderer) : typeof(LumOnRenderer);
        object renderer = Assert.Single(runtime.Cache.Events.Registrations.Select(item => item.Renderer).Distinct(), type.IsInstanceOfType);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var currentField = type.GetField("currentViewProjMatrix", fields)!;
        var buffers = (LumOnUniformBuffers)type.GetField("uniformBuffers", fields)!.GetValue(renderer)!;
        for (int i = 0; i < 8; i++) runtime.Frame();
        float[] previous = ((float[])currentField.GetValue(renderer)!).ToArray();
        Assert.Contains(previous, value => value != 0);
        scene.Position += new System.Numerics.Vector3(.125f, 0, -.0625f);
        scene.Bob = .0625f;
        runtime.Frame();
        using var mapped = buffers.FrameUbo.MapRange<float>(256, 16, MapBufferAccessMask.MapReadBit);
        Assert.True(mapped.IsMapped);
        for (int row = 0; row < 4; row++)
        {
            float expected = previous[row] * .125f + previous[4 + row] * .0625f
                + previous[8 + row] * -.0625f + previous[12 + row];
            Assert.InRange(mapped.Span[12 + row], expected - .00001f, expected + .00001f);
        }
    }

    /// <summary>The actual HZB consumer routes every mip and restores hostile engine state on repeated submissions.</summary>
    [Fact]
    public void RendererHzbSubmissionBuildsEveryMipUnderHostileState()
    {
        EnsureContextValid();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false, new SpatialLightingScene());
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        var renderer = Assert.Single(runtime.Cache.Events.Registrations.Select(item => item.Renderer).Distinct(),
            item => item is LumOnRenderer);
        var build = typeof(LumOnRenderer).GetMethod("BuildHzb", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var hzb = runtime.Screen.HzbDepthTex!;
        Assert.True(hzb.MipLevels > 1);
        foreach (float depth in new[] { .375f, .625f })
        {
            runtime.Cache.Terrain.Depth.UploadDataImmediate(Enumerable.Repeat(depth, 16).ToArray());
            using var hostile = new HostileFullscreenState();
            int draws = runtime.DrawnPrograms.Count;
            build.Invoke(renderer, [runtime.Cache.Terrain.Primary]);
            hostile.AssertRestored();
            Assert.Equal(hzb.MipLevels, runtime.DrawnPrograms.Count - draws);
            for (int mip = 0; mip < hzb.MipLevels; mip++)
                Assert.All(hzb.ReadPixels(mip), value => Assert.InRange(value, depth - .0001f, depth + .0001f));
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A foreign engine shader rejects submission without publishing or advancing temporal history, and the next valid frame recovers.</summary>
    [Fact]
    public void RejectedBoundaryDoesNotCommitOrSwapHistory()
    {
        EnsureContextValid();
        var scene = new SpatialLightingScene();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(false, scene);
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        var renderer = (LumOnRenderer)Assert.Single(runtime.Cache.Events.Registrations.Select(item => item.Renderer).Distinct(),
            item => item is LumOnRenderer);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var index = typeof(LumOnRenderer).GetField("frameIndex", fields)!;
        var first = typeof(LumOnRenderer).GetField("isFirstFrame", fields)!;
        int previousIndex = (int)index.GetValue(renderer)!;
        var current = runtime.Screen.ScreenProbeAtlasCurrentTex;
        var history = runtime.Screen.ScreenProbeAtlasHistoryTex;
        var temporal = (LumOnTemporalReprojection)typeof(LumOnRenderer).GetField("temporalReprojection", fields)!.GetValue(renderer)!;
        var committedX = typeof(LumOnTemporalReprojection).GetField("previousX", fields)!;
        double previousX = (double)committedX.GetValue(temporal)!;
        scene.Position += new System.Numerics.Vector3(.125f, 0, 0);
        var priorOwner = Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram;
        try
        {
            Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram = new Vintagestory.Client.NoObf.ShaderProgramParticlescube();
            renderer.OnRenderFrame(.016f, Vintagestory.API.Client.EnumRenderStage.Opaque);
            Assert.False(runtime.Screen.HasPublishedIndirect);
            Assert.Equal(previousIndex, (int)index.GetValue(renderer)!);
            Assert.Same(current, runtime.Screen.ScreenProbeAtlasCurrentTex);
            Assert.Same(history, runtime.Screen.ScreenProbeAtlasHistoryTex);
            Assert.Equal(previousX, (double)committedX.GetValue(temporal)!);
            Assert.True((bool)first.GetValue(renderer)!);
        }
        finally { Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram = priorOwner; }
        runtime.Frame();
        Assert.True(runtime.Screen.HasPublishedIndirect);
        Assert.True((int)index.GetValue(renderer)! > previousIndex);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
