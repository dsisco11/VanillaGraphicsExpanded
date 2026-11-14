using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks temporal origin history uploaded by registered normal and debug render callbacks.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnTemporalRendererTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared material-isolated graphics context.</summary>
    public LumOnTemporalRendererTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Moving the camera rebases the committed raw matrix in the actual uploaded frame buffer.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Scenario", "HeadlessRuntime")]
    public void RegisteredRendererUploadsRebasedHistory(bool debug)
    {
        // Arrange the production renderer registration and let it commit a stable initial camera history.
        EnsureContextValid();
        var scene = new SpatialLightingScene();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false, scene);
        runtime.Cache.Config.LumOn.DebugMode = debug ? LumOnDebugMode.WorldProbeConfidence : LumOnDebugMode.Off;
        Type type = debug ? typeof(LumOnDebugRenderer) : typeof(LumOnRenderer);
        object renderer = Assert.Single(runtime.Cache.Events.Registrations.Select(item => item.Renderer).Distinct(), type.IsInstanceOfType);
        // Act through the registered callback until the temporal history has a committed raw matrix.
        for (int i = 0; i < 8; i++) runtime.Frame();
        // Assert the seed history is populated before changing the camera origin.
        float[] previous = VgeFrameRenderer.Current.CurrentViewProjection.ToArray();
        Assert.Contains(previous, value => value != 0);
        scene.Position += new System.Numerics.Vector3(.125f, 0, -.0625f);
        scene.Bob = .0625f;
        // Act with a moved and bobbed camera, then inspect the uploaded rebased matrix.
        runtime.Frame();
        float[] alignedPrevious = VgeFrameRenderer.Current.PreviousViewProjection.ToArray();
        // Assert each rebased translation row matches the previous matrix transformed by the camera delta.
        for (int row = 0; row < 4; row++)
        {
            float expected = previous[row] * .125f + previous[4 + row] * .0625f
                + previous[8 + row] * -.0625f + previous[12 + row];
            Assert.InRange(alignedPrevious[12 + row], expected - .00001f, expected + .00001f);
        }
    }

    /// <summary>A foreign engine shader rejects submission without publishing or advancing temporal history, and the next valid frame recovers.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void RejectedBoundaryDoesNotCommitOrSwapHistory()
    {
        // Arrange a stable history, then make one frame ineligible by binding a foreign engine shader.
        EnsureContextValid();
        var scene = new SpatialLightingScene();
        using var runtime = new SurfaceLightingConsumerRuntimeFixture(ImageAssertions, false, scene);
        for (int frame = 0; frame < 8; frame++) runtime.Frame();
        var renderer = (LumOnRenderer)Assert.Single(runtime.Cache.Events.Registrations.Select(item => item.Renderer).Distinct(),
            item => item is LumOnRenderer);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        uint previousIndex = VgeFrameRenderer.Current.FrameIndex;
        var first = typeof(LumOnRenderer).GetField("isFirstFrame", fields)!;

        var current = runtime.Screen.ScreenProbeAtlasCurrentTex;
        var history = runtime.Screen.ScreenProbeAtlasHistoryTex;
        byte[] previousCamera = VgeFrameRenderer.Current.Bytes.ToArray();
        scene.Position += new System.Numerics.Vector3(.125f, 0, 0);
        var priorOwner = Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram;
        try
        {
            // The rejected submission must leave publication, ping-pong roles, camera bytes, and first-frame state intact.
            Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram = new Vintagestory.Client.NoObf.ShaderProgramParticlescube();
            // Act: submit one frame while the foreign shader owns the engine binding.
            renderer.OnRenderFrame(.016f, Vintagestory.API.Client.EnumRenderStage.Opaque);
            // Assert the rejected frame leaves all committed history observations untouched.
            Assert.False(runtime.Screen.HasPublishedIndirect);
            Assert.Equal(previousIndex, VgeFrameRenderer.Current.FrameIndex);
            Assert.Same(current, runtime.Screen.ScreenProbeAtlasCurrentTex);
            Assert.Same(history, runtime.Screen.ScreenProbeAtlasHistoryTex);
            Assert.Equal(previousCamera, VgeFrameRenderer.Current.Bytes.ToArray());
            Assert.True((bool)first.GetValue(renderer)!);
        }
        finally { Vintagestory.Client.NoObf.ShaderProgramBase.CurrentShaderProgram = priorOwner; }
        // A following valid frame proves the rejection was non-committing rather than permanently disabling the renderer.
        // Act: submit the next frame after restoring the engine shader owner.
        runtime.Frame();
        // Assert the valid frame resumes publication and advances the temporal index.
        Assert.True(runtime.Screen.HasPublishedIndirect);
        Assert.True(VgeFrameRenderer.Current.FrameIndex > previousIndex);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
    }

    #endregion

}
