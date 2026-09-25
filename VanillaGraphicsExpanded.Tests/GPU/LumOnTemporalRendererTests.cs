using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
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
    #endregion
}
