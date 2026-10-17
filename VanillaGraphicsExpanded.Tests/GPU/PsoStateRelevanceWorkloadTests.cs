using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies dormant-state preservation and necessary native transitions using real sky, cloud and point-resolve shaders.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PsoStateRelevanceWorkloadTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Actual nonzero outputs restore complete native state without dormant payload writes or stale point/blend state.</summary>
    [Theory]
    [InlineData("sky")]
    [InlineData("cloud")]
    [InlineData("point-resolve")]
    public void ProductionWorkloadsLeaveDormantNativeStateUntouched(string workload)
    {
        EnsureShaderTestAvailable();
        using var context = new PsoWorkloadContext(fixture);
        Measurement current = Run(workload);
        Assert.DoesNotContain(current.Events, item => item.StartsWith("application:LineWidth("));
        Assert.DoesNotContain(current.Events, item => item.StartsWith("application:Scissor("));
        if (workload != "point-resolve") Assert.DoesNotContain(current.Events, item => item.StartsWith("application:PointSize("));
        else
        {
            Assert.Contains(current.Events, item => item.StartsWith("application:PointSize(1"));
            // The real resolve stores physical RGB at persistent scale 1/16; validity/distance alpha stays unscaled.
            Assert.Equal(new[] { .046875f, .0078125f, .03125f, 1f }, current.Pixels[0].Skip((4 * 8 + 3) * 4).Take(4));
            Assert.Equal(4, current.Pixels[0].Count(value => value != 0));
        }
        if (workload == "cloud") Assert.Contains(current.Events, item => item.StartsWith("application:BlendFuncSeparate("));
        AssertDormantOutputPayloads(current.Events);
        output.WriteLine("PSO_RELEVANCE_WORKLOAD=" + JsonSerializer.Serialize(new { workload, current.FixedCalls, current.Events,
            renderer = GL.GetString(StringName.Renderer), version = GL.GetString(StringName.Version), exactNativeHandback = true }));
    }
    #endregion

    #region Private
    #region Workload admission
    /// <summary>Creates fresh deterministic workload inputs; deferred preparation remains in the observed setup segment.</summary>
    private Measurement Run(string workload)
    {
        if (workload == "sky")
        {
            using var scene = new ProductionSkyDrawScene(1);
            scene.Initialize();
            return Measure(() => scene.Draw(true), scene.Read);
        }
        if (workload == "cloud")
        {
            using var programs = new ComponentShaderPrograms();
            using var scene = new CloudReconstructionScene(8, 8, programs.Create<CloudVolumeProgram>(), programs.Create<CloudTraceProgram>(),
                programs.Create<CloudTemporalProgram>(), programs.Create<CloudResolveProgram>(), false, 4);
            scene.Capture(0, .125f, 10, 1, 0, "detail-lit");
            return Measure(() => scene.Render(0, false), () => [.. scene.ReadDefault(), .. scene.ReadReference(), .. scene.ReadTraceRecord(), .. scene.ReadHistory()]);
        }
        return PointResolve();
    }

    /// <summary>Runs real one-pixel radiance-atlas resolve through the shared complete pipeline, requiring fixed point-size activation.</summary>
    private Measurement PointResolve()
    {
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<LumOnWorldProbeRadianceTileResolveShaderProgram>();
        program.AtlasSize = new(8, 8);
        using var image = DynamicTexture2D.CreateWithData(8, 8, PixelInternalFormat.Rgba32f, new float[8 * 8 * 4]);
        using var target = DynamicGpuFramebuffer.CreateSingle(image)!;
        var pass = new RenderPassDesc(target, [new(0)]);
        using var metadata = new RenderPassTargets(pass);
        var layout = new VertexLayoutDesc([new(0, 2, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 24),
            new(1, 4, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 8, 24)]);
        using var buffer = GpuVbo.Create(); buffer.UploadData(new float[] { 3, 4, .75f, .125f, .5f, 1 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveType.Points, new Dictionary<int, GpuVbo> { [0] = buffer });
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = new GraphicsPipeline(lifetime, new(program.GraphicsIdentity!, layout, metadata.Signature,
            DynamicPipelineState.Viewport, assembly: new() { Topology = PrimitiveType.Points }), program);
        return Measure(() => Assert.True(GraphicsCommandContext.TryRun("Pso.PointResolve", [pipeline], true, commands =>
        {
            commands.BeginPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 1)); commands.EndPass();
        })), () => [image.ReadPixels()]);
    }
    #endregion

    #region Measurement
    /// <summary>Checks every observed draw separately so active resolve blending cannot hide writes during replacement tracing.</summary>
    private static void AssertDormantOutputPayloads(IEnumerable<string> events)
    {
        string[] slots = [];
        foreach (string item in events)
        {
            if (item.StartsWith("draw:")) { slots = item[5..].Split(','); continue; }
            if (!item.StartsWith("application:BlendFuncSeparate(") && !item.StartsWith("application:BlendEquationSeparate(")) continue;
            string argument = item[(item.IndexOf('(') + 1)..].Split(',')[0];
            Assert.True(int.TryParse(argument, out int slot), "Expected indexed native blending on this independent-blending context: " + item);
            Assert.InRange(slot, 0, slots.Length - 1);
            Assert.Equal("blend", slots[slot]);
        }
    }

    /// <summary>Separates hostile setup and raw native inspection from the bounded observed callback.</summary>
    private static Measurement Measure(Action draw, Func<float[][]> pixels)
    {
        PsoNativeStateFingerprint.MakeDormantHostile();
        string before = PsoNativeStateFingerprint.Read();
        long start = StateCache.Current.FixedFunctionCalls;
        string[] events;
        using (var capture = new PsoNativeCallCapture()) { draw(); events = capture.Events.ToArray(); }
        long calls = StateCache.Current.FixedFunctionCalls - start;
        string after = PsoNativeStateFingerprint.Read();
        Assert.Equal(before, after);
        float[][] images = pixels();
        Assert.Contains(images.SelectMany(v => v), value => value > 0);
        return new(calls, events, images);
    }
    #endregion
    #endregion

    /// <summary>Retains actual pixel results and bounded emitted-wrapper events after independent native handback checks.</summary>
    private sealed record Measurement(long FixedCalls, string[] Events, float[][] Pixels);
}
