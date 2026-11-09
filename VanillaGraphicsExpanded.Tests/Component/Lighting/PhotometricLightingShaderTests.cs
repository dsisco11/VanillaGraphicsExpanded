using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Lighting;

/// <summary>Exercises direct production quantity helpers against independent double-precision expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class PhotometricLightingShaderTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>An inward solar ray at the planet surface is occluded even when its intersection distance is zero.</summary>
    [Fact]
    public void SurfaceInwardSunlightIsOccluded()
    {
        // Arrange: Select the surface-inward solar-ray probe, whose planet intersection begins at zero distance.
        EnsureContextValid();
        // Act: submit the production atmosphere transport helper with its original 64-byte parameter block.
        var image = Render(PhotometricProbeOperation.SurfaceInwardSunlight);
        // Assert: the zero-distance inward surface intersection blocks every solar channel.
        Assert.Equal(new float[] { 0, 0, 0, 0 }, GpuLocalRead.NativeColorTuple(image, 0, 0));
    }

    /// <summary>Planet intersections retain metre-scale segments from the actual stored Float32 position.</summary>
    [Fact]
    public void GroundGeometryUsesDeclaredFloatPosition()
    {
        // Arrange: Select the ground-intersection probe at a metre-scale altitude represented by Float32.
        EnsureContextValid();
        // Act: evaluate the shipped intersection helper at the authored Float32 altitude.
        var image = Render(PhotometricProbeOperation.GroundGeometry);
        var pixels = GpuLocalRead.NativeColorTuple(image, 0, 0);
        float representedHeight = (float)(6360f + .001f) - 6360f;
        // Assert: retain the original relative intersection tolerance and exact represented altitude.
        Assert.InRange(pixels[0], representedHeight * .99999, representedHeight * 1.00001);
        Assert.Equal(representedHeight, pixels[1]);
    }

    /// <summary>Distinct installed byte decoders retain equal torch illumination across their truncation boundary.</summary>
    [Fact]
    public void ActiveBlockDecoderCalibrationMatchesBothPublicationRoutes()
    {
        // Arrange: Give the two installed byte decoders distinct calibration endpoints around their truncation boundary.
        EnsureContextValid();
        float[] block = Enumerable.Repeat(.03f, 32).ToArray();
        block[13] = .501f; block[14] = .701f;
        float[] sun = Enumerable.Range(0, 32).Select(i => .03f + i * .03f).ToArray();
        LightingCalibration calibration = new EngineLightingCalibration().Capture(block, sun, new byte[8], new byte[8]);
        Assert.NotEqual(calibration.BlockReferenceLux, calibration.AccessorBlockReferenceLux);
        // Act: both direct decoder routes share one published frame with distinct calibration endpoints.
        var image = Render(PhotometricProbeOperation.ActiveBlockDecoder, calibration: calibration);
        double expected = 44 / (4 * Math.PI), tolerance = 2e-6 * Math.Max(1, Math.Abs(expected));
        // Assert: retain the original whole-domain finite and calibrated-range check.
        ImageAssertions.Statistics.Inspect(image, expected - tolerance, expected + tolerance, channelMask: 7).AssertFiniteRange();
    }

    /// <summary>Both field decoders preserve the torch anchor and suppress weak light with vanishing floor slope.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlockFalloffIsSmoothForBothDecoders(bool accessor)
    {
        EnsureContextValid();
        // Arrange: distinct floors and coefficients expose accidental use of the other installed route.
        LightingCalibration terrain = LightingCalibration.FromDecodedTorch(new(145f / 255, 99f / 255, 71f / 255),
            new(9, 6, 4), 10f / 255, 7f / 255, 1, .03f, 1);
        LightingCalibration alternate = LightingCalibration.FromDecodedTorch(new(144f / 255, 98f / 255, 70f / 255),
            new(9, 6, 4), 9f / 255, 7f / 255, 1, .03f, 1);
        LightingCalibration calibration = terrain with { AccessorBlockReferenceLux = alternate.BlockReferenceLux,
            AccessorBlockNeutralFloor = alternate.BlockNeutralFloor };
        // Act: select each direct decoder in turn without changing its authored texel inputs.
        var image = Render(PhotometricProbeOperation.BlockFalloff, selector: accessor ? 1 : 0, calibration: calibration);
        // Assert: the same predicate retains the original floor derivatives, chroma and reference anchor.
        ImageAssertions.Predicates.Predicate(image, PhotometricPredicates.BlockFalloff,
            parameters: new(accessor ? 1 : 0, 0, 0, 0)).AssertEqual();
    }

    /// <summary>The broad fade attenuates intermediate amplitudes while bounding gain above the torch reference.</summary>
    [Fact]
    public void BlockFadeSpansIntermediateFieldWithoutExcessFullFieldGain()
    {
        // Arrange: Prepare the production fade probe over a fixed sequence of intermediate source amplitudes.
        EnsureContextValid();
        // Act: Render the fade helper and derive independent expected values plus strict upper bounds for each sample.
        var image = Render(PhotometricProbeOperation.BlockFade);
        double[] peaks = [.08, .12, .2, .3, .4, .5, .8, 1];
        List<GpuScalarInterval> bounds = [];
        for (int column = 0; column < peaks.Length; column++)
        {
            double amplitude = peaks[column] - 10.0 / 255;
            double expected = 12.8574078602199 * Math.Pow(amplitude, 4) / (Math.Pow(amplitude, 3) + .064);
            bounds.Add(Near(column * 4, expected));
            // Preserve strict upper bounds independently of the helper response equation.
            double previousGentle = 12.3458217833822 * Math.Pow(amplitude, 1.5);
            if (column <= 3) bounds.Add(new(column, 0, double.NegativeInfinity, Math.BitDecrement(previousGentle * .5)));
            if (column == 4) bounds.Add(new(column, 0, double.NegativeInfinity, Math.BitDecrement(previousGentle * .75)));
            double originalLinear = 8.982904808459303 * amplitude;
            bounds.Add(new(column, 0, double.NegativeInfinity, Math.BitDecrement(originalLinear * 1.35)));
        }
        // Assert: preserve every independent value, strict bound and monotonicity predicate.
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
        ImageAssertions.Predicates.Predicate(image, PhotometricPredicates.IncreasingRed).AssertEqual();
    }

    /// <summary>Terrain and stored-sky decoders remove their distinct neutral floors and preserve full availability.</summary>
    [Fact]
    public void SkyAvailabilityUsesDecoderSpecificEndpoints()
    {
        // Arrange: Select terrain and stored-sky decoder probes with distinct neutral-floor endpoints.
        EnsureContextValid();
        // Act and assert: Render both decoder regions and compare full, zero, and half availability against the original endpoint tuples.
        var image = Render(PhotometricProbeOperation.SkyAvailability);
        // Assert: retain both original 8x2 regions, endpoint tuples and half-availability tolerance.
        ImageAssertions.Comparisons.CompareRegions(image,
            [new(new(default, 0, 0, 0, 2, 2, 1), new(0), 1),
             new(new(default, 2, 0, 0, 1, 2, 1), new(.5f), 1),
             new(new(default, 3, 0, 0, 5, 2, 1), new(1), 1)], tolerance: 2e-6).AssertEqual();
    }

    /// <summary>Physical point output obeys inverse-square units and engine payloads retain an explicit adapter.</summary>
    [Fact]
    public void PointAndEmissionHelpersMatchIndependentUnits()
    {
        // Arrange: Select point-light distances and emission values for the production helper probe.
        EnsureContextValid();
        // Act: Render once, then build independent inverse-square, candela-adapter, and emission expectations.
        var image = Render(PhotometricProbeOperation.PointAndEmission);
        List<GpuScalarInterval> bounds = [];
        for (int channel = 0; channel < 3; channel++)
        {
            bounds.Add(Near(channel, 1000 / (4 * Math.PI)));
            bounds.Add(Near(4 + channel, 1000 / (16 * Math.PI)));
            bounds.Add(Near(8 + channel, 4 * LightingCalibration.EnginePointCandelaPerUnit));
            bounds.Add(new(3, channel, 0, 0));
            bounds.Add(Near(16 + channel, new[] { 16d, 32d, 64d }[channel]));
            bounds.Add(new(5, channel, 0, 0));
        }
        // Assert: Compare point distances, engine adapter output, and emission values against independent scalar bounds.
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
    }

    /// <summary>Storage containment keeps exceptional payloads finite without declaring NaN data valid.</summary>
    [Fact]
    public void StorageProtectionPreservesValidityAndDestinationLimits()
    {
        // Arrange: Select exceptional source values and finite destination bounds for storage-containment probes.
        EnsureContextValid();
        // Act: Run the production storage helper over the authored exceptional values.
        var image = Render(PhotometricProbeOperation.StorageProtection);
        // Assert: Exceptional inputs remain finite without being marked valid, and each destination stays within its storage limit.
        ImageAssertions.Comparisons.CompareIntervals(image,
            [Exact(3, 1), Exact(7, 1), Exact(8, 65504), Exact(11, 2), Exact(12, 65504),
             Exact(13, 0), Exact(15, 0), Exact(19, 0), Exact(20, 0), Exact((8 + 3) * 4 + 1, -65504),
             Exact((8 + 5) * 4, -65504), Exact(24, 65024), Exact(27, 1), Exact(28, 64512), Exact(31, 2)]).AssertEqual();
        ImageAssertions.Statistics.Inspect(image).AssertFiniteRange();
    }

    /// <summary>Block conversion retains chroma, neutral zero and monotonic intensity without camera exposure.</summary>
    [Fact]
    public void BlockAndPersistentHelpersMatchPhysicalContracts()
    {
        // Arrange: Prepare neutral, torch, chromatic, negative, and extreme block samples for both output domains.
        EnsureContextValid();
        // Act: Render block conversion and persistent-source helpers with their distinct physical contracts.
        var image = Render(PhotometricProbeOperation.BlockAndPersistent);
        double[][] inputs = [[0, 0, 0], [10.0 / 255, 10.0 / 255, 10.0 / 255], [.2, .1, .05], [.4, .2, .1],
            [145.0 / 255, 99.0 / 255, 71.0 / 255], [-1, .4, .2], [1, 1, 1], [1048064, 1048064, 1048064]];
        List<GpuScalarInterval> bounds = [];
        for (int column = 0; column < inputs.Length; column++)
        {
            double peak = inputs[column].Select(x => Math.Max(x, 0)).Max();
            for (int channel = 0; channel < 3; channel++)
            {
                double amplitude = Math.Max(peak - LightingCalibration.Default.BlockNeutralFloor, 0);
                double expected = peak > 0 ? Math.Max(inputs[column][channel], 0)
                    * Math.Pow(amplitude, 4) / (Math.Pow(amplitude, 3) + .064) / peak
                    * LightingCalibration.Default.BlockReferenceLux : 0;
                bounds.Add(Near(column * 4 + channel, expected));
                if (column < 6) bounds.Add(Near((8 + column) * 4 + channel, expected / Math.PI));
            }
        }
        bounds.AddRange([Exact((8 + 6) * 4, -32), Exact((8 + 6) * 4 + 1, 131072),
            Exact((8 + 6) * 4 + 2, 1048064), Exact((8 + 6) * 4 + 3, 1), Exact((8 + 7) * 4 + 3, 0)]);
        // Assert: Preserve each helper's physical output and the exact exceptional persistent-source metadata.
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
        var torch = GpuLocalRead.NativeColorTuple(image, 4, 0);
        double torchY = .2126 * torch[0] + .7152 * torch[1] + .0722 * torch[2];
        Close(44 / (4 * Math.PI), (float)torchY);
        Assert.True(GpuLocalRead.NativeColorTuple(image, 3, 0)[0] > GpuLocalRead.NativeColorTuple(image, 2, 0)[0]);
    }

    /// <summary>Signed source overshoot is bounded without changing homogeneous scattering or transmission.</summary>
    [Fact]
    public void BoundarySourceBoundPreservesHomogeneousTransport()
    {
        // Arrange: Select signed source overshoot and a range of homogeneous transport depths.
        EnsureContextValid();
        // Act: Render bounded-source transport and compare scattering and transmission independently.
        var image = Render(PhotometricProbeOperation.BoundaryTransport);
        double[] depths = [0, .0001, .5, 10, 1, 1];
        double[] source = [.2, .3, .4];
        List<GpuScalarInterval> bounds = [];
        for (int column = 0; column < 6; column++)
        for (int channel = 0; channel < 3; channel++)
        {
            double transmission = Math.Exp(-depths[column]);
            bounds.Add(Near(column * 4 + channel, column == 5 ? 0 : source[channel] * (1 - transmission)));
            bounds.Add(Near((8 + column) * 4 + channel, transmission));
            bounds.Add(Exact(column * 4 + 3, 1));
        }
        bounds.Add(Exact(6 * 4 + 3, 0));
        bounds.Add(Exact(7 * 4 + 3, 0));
        // Assert: Both scattering and transmittance match homogeneous transport without changing alpha validity.
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
    }

    /// <summary>Frame helpers decode physical values once and reject unsupported temporal or generation inputs.</summary>
    [Fact]
    public void FrozenStorageHelpersKeepHistoryAndPhysicalDomainsSeparate()
    {
        // Arrange: Encode frozen storage scale, history scale, source bound, and generation identity in the publication block.
        EnsureContextValid();
        byte[] publication = new byte[32];
        System.Buffer.BlockCopy(new float[] { .25f, 4, 100, 1 }, 0, publication, 0, 16);
        System.Buffer.BlockCopy(new uint[] { 42, 7, 0, 0 }, 0, publication, 16, 16);
        // Act and assert: Render frozen-storage decodes and compare physical values separately from temporal/generation history.
        var image = Render(PhotometricProbeOperation.FrozenStorage, frozenStorage: publication);
        // Assert: Physical values decode once; extreme history scaling stays finite within the Float32 subnormal domain.
        ImageAssertions.Comparisons.CompareIntervals(image,
            [Exact(0, 8), Exact(4, 32), Exact(8, 8), Exact(12, 8), Exact(15, 1),
             Exact(19, 0), Exact(23, 0), Exact(27, 1), Exact(31, 0)]).AssertEqual();
        // Storage scale .25 with historyScale=3e38 yields about 1.7e-39 per RGB channel;
        // drivers may flush that ratio/output to zero or retain a bounded Float32 subnormal.
        float[] extremeHistory = GpuLocalRead.NativeColorTuple(image, 5, 1);
        float largestSubnormal = BitConverter.Int32BitsToSingle(0x007fffff);
        Assert.All(extremeHistory[..3], value =>
        {
            Assert.True(float.IsFinite(value));
            Assert.InRange(value, 0f, largestSubnormal);
        });
        Assert.True(extremeHistory[3] == 0f || extremeHistory[3] == 1f);
    }

    /// <summary>Visible disk storage respects glare headroom before multiplication and rejects unsupported source metadata.</summary>
    [Fact]
    public void VisibleDiskAndDirectionalAdmissionRespectDeclaredRanges()
    {
        // Arrange: Compute visible-disk limits for small/large sources and provide frozen storage metadata.
        EnsureContextValid();
        float[] limits =
        [
            AtmosphereSolarDiskStorage.MaximumScale(new(3e38f, 1.5e38f, 0), 1, 1),
            AtmosphereSolarDiskStorage.MaximumScale(new(3e38f, 1.5e38f, 0), 2, 1),
            0, 0, 0, 0,
            AtmosphereSolarDiskStorage.MaximumScale(new(1e-20f), 1, 1),
            AtmosphereSolarDiskStorage.MaximumScale(new(float.MaxValue, float.MaxValue * .5f, 0), 1, 1)
        ];
        Vector4 limits0 = new(limits[0], limits[1], limits[2], limits[3]);
        Vector4 limits1 = new(limits[4], limits[5], limits[6], limits[7]);
        byte[] publication = new byte[32];
        System.Buffer.BlockCopy(new float[] { 4, .25f, 100, 1 }, 0, publication, 0, 16);
        System.Buffer.BlockCopy(new uint[] { 42, 7, 0, 0 }, 0, publication, 16, 16);
        // Act and assert: Render visible-disk limits with frozen metadata and verify headroom and rejected ranges.
        var image = Render(PhotometricProbeOperation.VisibleDisk, frozenStorage: publication,
            limits0: limits0, limits1: limits1);
        List<GpuScalarInterval> bounds = [new(0, 0, 47999.9f, 48000), new(1, 0, 15999.9f, 16000),
            Exact(3, 1), Exact(7, 1), new(6, 0, 3.999e-20f, 4.001e-20f), Exact(27, 1),
            new(7, 0, 47999.9f, 48000), Exact(31, 1), Exact(8 * 4 + 3, 1), Exact((8 + 4) * 4 + 3, 1)];
        for (int column = 2; column <= 5; column++) bounds.Add(Exact(column * 4 + 3, 0));
        for (int column = 1; column <= 3; column++) bounds.Add(Exact((8 + column) * 4 + 3, 0));
        // Assert: Headroom bounds RGB before multiplication and unavailable metadata rejects disk publication.
        ImageAssertions.Comparisons.CompareIntervals(image, bounds).AssertEqual();
        // These three correlated tuples retain their original CPU double arithmetic.
        foreach (int column in new[] { 0, 1, 7 })
        {
            var tuple = GpuLocalRead.NativeColorTuple(image, column, 0);
            Close(tuple[0] * .5, tuple[1]);
        }
    }
    #endregion

    #region Private
    /// <summary>Prepares only the compact comparisons and correlated photometric checks used by this suite.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare");
        programs.Prepare("inspection");
        programs.Prepare("predicate-photometric");
    }

    /// <summary>Maps a flat scalar coordinate to its exact nominated GPU interval.</summary>
    private static GpuScalarInterval Exact(int component, double expected) => new(component / 4, component % 4, expected, expected);

    /// <summary>Retains the original independent double relative bound at one authored scalar coordinate.</summary>
    private static GpuScalarInterval Near(int component, double expected)
    {
        double tolerance = 2e-6 * Math.Max(1, Math.Abs(expected));
        return new(component / 4, component % 4, expected - tolerance, expected + tolerance);
    }

    /// <summary>Captures declared probe inputs and submits one 8x2 draw through the production RHI route.</summary>
    private GpuTexture Render(PhotometricProbeOperation operation, int selector = 0,
        LightingCalibration? calibration = null, byte[]? frozenStorage = null,
        Vector4 limits0 = default, Vector4 limits1 = default)
    {
        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<PhotometricLightingProbeProgram>();
        using var frame = new VgeFrameUniformBuffer();
        if (calibration.HasValue) frame.SetLightingCalibration(calibration.Value);
        using var atmosphere = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
        { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0,
            Usage = BufferUsage.DynamicDraw, Label = "Tests.PhotometricAtmosphereParameters" }).RequireResource();
        atmosphere.UploadData<Vector4>([
            new(0, 1, 0, .001f), new(1, .1f, 32, 24), new(32, 16, 128, 24), new(12, 0, 0, 1)
        ]);
        using var lighting = new TestFrameLightingPublication();
        using var customLighting = frozenStorage is null ? null
            : RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
            { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0,
                Usage = BufferUsage.DynamicDraw, Label = "Tests.FrozenFrameLighting" }).RequireResource();
        if (customLighting is not null)
        {
            customLighting.UploadData<byte>(frozenStorage!);
        }
        GpuShaderStorageBuffer lightingBuffer = customLighting ?? lighting.Buffer;
        program.Capture(operation, selector, limits0, limits1, frame, atmosphere, lightingBuffer);

        using var target = CreateRenderTarget(8, 2, TextureFormat.Rgba32f);
        var commands = RHICommandContext.Current;
        var pass = FramebufferTestSetup.DescribeAttachments(target,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var layout = new RHIVertexLayout([]);
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        // Act: publish typed resources and execute the full probe draw in one RHI pass.
        using var boundary = commands.EnterBoundary(new("Tests.PhotometricHelpers", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 3));
            commands.EndRenderPass();
        });
        return target[0];
    }

    /// <summary>Bounds arithmetic error without accepting nonfinite physical quantities.</summary>
    private static void Close(double expected, float actual)
    {
        Assert.True(float.IsFinite(actual));
        double tolerance = 2e-6 * Math.Max(1, Math.Abs(expected));
        Assert.InRange(actual, expected - tolerance, expected + tolerance);
    }
    #endregion
}
