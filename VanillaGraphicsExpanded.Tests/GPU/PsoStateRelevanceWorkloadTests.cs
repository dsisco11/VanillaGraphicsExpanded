using System;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        if (workload == "cloud") Assert.Contains("validated:active-blend-factors",current.Events);
        AssertDormantOutputPayloads(current.Events);
        output.WriteLine("PSO_RELEVANCE_WORKLOAD=" + JsonSerializer.Serialize(new { workload,
#if DEBUG
            current.FixedCalls,
#endif
            current.Events,
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
            using var scene = new CloudStoredVolumeViewScene(programs,8,8);
            var settings=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeViewSettings.Default;
            float[][] pixels=[];return Measure(() => pixels=scene.Draw(settings), () => pixels);
        }
        return PointResolve();
    }

    /// <summary>Runs real one-pixel radiance-atlas resolve through the shared complete pipeline, requiring fixed point-size activation.</summary>
    private Measurement PointResolve()
    {
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<LumOnWorldProbeRadianceTileResolveShaderProgram>();
        program.AtlasSize = new(8, 8);
        using var image = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 8, Height = 8, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 8, 8), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[8 * 8 * 4]).AsSpan()).ToArray())]).RequireResource();
        using var target = DynamicGpuFramebuffer.CreateSingle(image)!;
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0)]);
        var metadata = RHICommandContext.Current.DescribeRenderTargets(pass);
        var layout = new RHIVertexLayout([new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 24),
            new(1, 4, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 8, 24)]);
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); buffer.UploadData(new float[] { 3, 4, .75f, .125f, .5f, 1 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Points, new Dictionary<int, GpuVbo> { [0] = buffer });
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(program.Shader, layout, metadata,
            DynamicPipelineState.Viewport, assembly: new() { Topology = PrimitiveTopology.Points })).RequireResource();
        return Measure(() => {
    var commands=RHICommandContext.Current;
    using var scope73=commands.EnterBoundary(new("Pso.PointResolve",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope73.Run(()=>{
            commands.BeginRenderPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 1)); commands.EndRenderPass();
        });
}, () => [image.ReadPixels()]);
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
#if DEBUG
        long start = StateCache.Current.FixedFunctionCalls;
#endif
        string[] events;
        using (var capture = new PsoNativeCallCapture()) { draw(); events = capture.Events.ToArray(); }
#if DEBUG
        long calls = StateCache.Current.FixedFunctionCalls - start;
#endif
        string after = PsoNativeStateFingerprint.Read();
        Assert.Equal(before, after);
        float[][] images = pixels();
        Assert.Contains(images.SelectMany(v => v), value => value > 0);
        return new(
#if DEBUG
            calls,
#endif
            events, images);
    }
    #endregion
    #endregion

    /// <summary>Retains actual pixel results and bounded emitted-wrapper events after independent native handback checks.</summary>
    private sealed record Measurement(
#if DEBUG
        long FixedCalls,
#endif
        string[] Events, float[][] Pixels);
}
