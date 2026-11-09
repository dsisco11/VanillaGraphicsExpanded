using ErrorCode = OpenTK.Graphics.OpenGL4.ErrorCode;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Qualifies the compact GPU oracle against authored data, deliberate failures and hostile native state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed partial class GpuImageAssertionTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{


    #region Public API
    #region Production executable ownership
    /// <summary>Independent consumers reuse one production executable while inputs, results and cleanup remain isolated.</summary>
    [Fact]
    public void ProductionOwnerSurvivesConsumersAndPublishesFreshInputs()
    {
        // Arrange: retain the fixture-owned executable identity and observe unexpected program recreation.
        EnsureContextValid();
        var owner = fixture.Observations.Programs.Get("compare");
        int program = owner.ProgramId;
        using var native = new NativeDriverCapture();
        int creations = 0;
        var original = Marshal.GetDelegateForFunctionPointer<CreateProgramNative>(native.Original("glCreateProgram"));
        native.Add("glCreateProgram", new CreateProgramNative(() =>
        {
            native.Observe(() => creations++);
            return original();
        }));
        native.Install();
        // Act: consume one image, drain test resources, then publish distinct inputs through the surviving owner.
        using (var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 4, 2))
        { var first = ImageAssertions;
            Upload(image, Enumerable.Repeat(.25f, 32).ToArray(), PixelComponentType.Float);
            first.Comparisons.CompareConstant(image, .25f).AssertEqual();
        }
        // Per-test resources drain without retiring the fixture manager or its checker ownership.
        fixture.CleanupGpuResources();
        fixture.EnsureContextValid();
        Assert.True(GL.IsProgram(program));
        Assert.True(owner.IsValid);
        using var replacement = Create(TextureDimension.Two, TextureFormat.Rgba32f, 4, 2);
        { var second = ImageAssertions;
            Upload(replacement, Enumerable.Repeat(.25f, 32).ToArray(), PixelComponentType.Float);
            second.Comparisons.CompareConstant(replacement, .25f).AssertEqual();
            Upload(replacement, Enumerable.Repeat(.75f, 32).ToArray(), PixelComponentType.Float);
            second.Comparisons.CompareConstant(replacement, .75f).AssertEqual();
            Assert.Equal(32u, second.Comparisons.CompareConstant(replacement, .25f).Mismatches);
            Assert.Throws<ArgumentOutOfRangeException>(() => second.Predicates.Predicate(replacement, default(GpuAssertionPredicate)));
        }
        Assert.Same(owner, fixture.Observations.Programs.Get("compare"));
        // Assert: owner/program identity survives consumer churn and no replacement executable was created.
        Assert.Equal(program, owner.ProgramId);
        Assert.Equal(0, creations);
        native.RequireSuccessfulObservation();
    }
    #endregion

    /// <summary>Explicit preparation retains owners, allocates only needed result storage and rejects unknown domain choices.</summary>
    [Fact]
    public void ExplicitPreparationAndResultStorageRemainBounded()
    {
        // Arrange: start with an empty explicit owner and prepare only operation families under test.
        EnsureContextValid();
        using var owners = new GpuObservationPrograms();
        var observations = new GpuObservations(owners);
        Assert.Equal(0, owners.PreparedCount);
        Assert.Throws<InvalidOperationException>(() => owners.Get("compare"));
        // Act: prepare the comparison owner twice to verify repeated setup is idempotent.
        owners.Prepare("compare"); owners.Prepare("compare");
        // Assert: only one program is retained and its reserved image/sampler slots stay unused.
        Assert.Equal(1, owners.PreparedCount);
        Assert.DoesNotContain(1, owners.Get("compare").ImageUnits);
        Assert.DoesNotContain(3, owners.Get("compare").SamplerUnits);
        Assert.DoesNotContain(4, owners.Get("compare").SamplerUnits);
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1);
        Upload(image, [1f], PixelComponentType.Float);
        // Act: run bounded comparison/statistics operations and request unavailable domains.
        observations.Comparisons.CompareConstant(image, 1).AssertEqual();
        // Assert: one comparison result image is reused only within its operation family.
        Assert.Equal(1u, observations.Comparisons.CompareConstant(image, 2).Mismatches);
        Assert.Equal(2, owners.Dispatch.AllocatedImages);
        owners.Prepare("inspection"); owners.Prepare("statistics");
        Assert.Equal(3, owners.PreparedCount);
        Assert.Equal(1, observations.Statistics.Statistics(image).SumSquares(0));
        Assert.Equal(4, owners.Dispatch.AllocatedImages);
        var predicate = ObservationPredicates.GpuImageAssertionsSelection_Extremum with { Choice = int.MaxValue };
        var projection = ObservationProjections.GpuImageAssertionsSelection_Brightest with { Choice = int.MaxValue };
        // Assert: invalid domains are rejected without allocating additional result images.
        Assert.Throws<ArgumentOutOfRangeException>(() => observations.Predicates.Predicate(image, predicate));
        Assert.Throws<ArgumentOutOfRangeException>(() => observations.Projections.Apply(image, projection));
        Assert.Equal(4, owners.Dispatch.AllocatedImages);
        output.WriteLine($"Observation ownership: prepared={owners.PreparedCount}, preparationMs={owners.PreparationElapsed.TotalMilliseconds:F3}, comparisonResultImages=1/operation, statisticsResultAndScratchImages=2/operation.");
    }

    /// <summary>Context validation prepares no observers and explicit manager retirement invalidates original preparations.</summary>
    [Fact]
    public void ContextValidationAndManagerRetirementDoNotReuseWithdrawnPreparations()
    {
        // Arrange: prepare an observation owner, then retire the resource services that own its GPU state.
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        fixture.EnsureContextValid();
        var original = fixture.Observations;
        Assert.Equal(0, original.Programs.PreparedCount);
        // Act: prepare and use one owner, retire the service, then validate the replacement fixture generation.
        original.Programs.Prepare("inspection");
        Assert.Equal(1, original.Programs.PreparedCount);
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(image, [0, 0, 0, 0], PixelComponentType.Float);
        original.Statistics.InspectRgbPeak(image).AssertFiniteRange();
        fixture.RetireResourceServices();
        Assert.Throws<ObjectDisposedException>(() => original.Programs.Get("inspection"));
        Assert.Throws<ObjectDisposedException>(() => original.Statistics.InspectRgbPeak(image));
        fixture.EnsureContextValid();
        // Assert: the original owner is retired and context validation creates a fresh empty observation set.
        Assert.NotSame(original, fixture.Observations);
        Assert.Equal(0, fixture.Observations.Programs.PreparedCount);
    }

    /// <summary>One GPU pass validates every represented channel while preserving RGB-only absolute peak semantics.</summary>
    [Fact]
    public void RgbPeakInspectionCombinesFullChannelFinitenessAndFreshMagnitude()
    {
        EnsureContextValid();
        // Arrange: alpha exceeds RGB and a negative red value owns the expected magnitude.
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 67, 1);
        float[] authored = new float[67 * 4];
        authored[0] = -11; authored[1] = -3; authored[2] = -2; authored[3] = 100;
        authored[4] = 4; authored[5] = 5; authored[6] = -6; authored[7] = -200;
        authored[^4] = -13; authored[^3] = 1; authored[^2] = 2; authored[^1] = 300;
        Upload(image, authored, PixelComponentType.Float);

        // Act: inspect the complete represented image through the existing GPU observation owner.
        var first = ImageAssertions.Statistics.InspectRgbPeak(image);

        // Assert: the full-domain extrema include alpha, while the reported peak excludes it.
        Assert.Equal(0u, first.NonFinite);
        Assert.Equal(67u * 4, first.Components);
        Assert.Equal(67u, first.VisitedTexels);
        Assert.Equal(-200d, first.Minimum);
        Assert.Equal(300d, first.Maximum);
        Assert.Equal(13f, first.RgbPeakMagnitude);

        // Same-context writes must be observed freshly even when storage identity is unchanged.
        Array.Clear(authored);
        authored[^4] = -4; authored[^3] = -5; authored[^2] = -6; authored[^1] = 7;
        Upload(image, authored, PixelComponentType.Float);
        var replaced = ImageAssertions.Statistics.InspectRgbPeak(image);
        Assert.Equal(6f, replaced.RgbPeakMagnitude);
        Array.Clear(authored);
        authored[1] = -0f;
        Upload(image, authored, PixelComponentType.Float);
        Assert.Equal(0f, ImageAssertions.Statistics.InspectRgbPeak(image).RgbPeakMagnitude);

        // Ordered positive float bits preserve subnormal magnitude, signed zero and the finite limit.
        Array.Clear(authored);
        authored[0] = float.Epsilon; authored[1] = -float.Epsilon; authored[2] = -0f;
        authored[^1] = float.MaxValue;
        Upload(image, authored, PixelComponentType.Float);
        Assert.Equal(float.Epsilon, ImageAssertions.Statistics.InspectRgbPeak(image).RgbPeakMagnitude);
        authored[0] = -float.MaxValue; authored[1] = float.MaxValue; authored[2] = -0f;
        authored[^1] = 0;
        Upload(image, authored, PixelComponentType.Float);
        Assert.Equal(float.MaxValue, ImageAssertions.Statistics.InspectRgbPeak(image).RgbPeakMagnitude);

        // Alpha remains part of finite validation and retains its original component diagnostics.
        Array.Clear(authored);
        authored[3] = float.NaN;
        Upload(image, authored, PixelComponentType.Float);
        var invalidAlpha = ImageAssertions.Statistics.InspectRgbPeak(image);
        Assert.Equal(1u, invalidAlpha.NonFinite);
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(invalidAlpha.AssertFiniteRange);
        Assert.Contains("channel=3", failure.Message);
        Assert.Contains("nonfinite=1", failure.Message);
    }

    /// <summary>Alpha infinities remain invalid even though alpha never contributes to the RGB peak.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void RgbPeakInspectionRejectsInfiniteAlpha(int sign)
    {
        EnsureContextValid();
        // Arrange: alpha is nonfinite while every RGB value remains finite.
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(image, [1, 2, 3, sign > 0 ? float.PositiveInfinity : float.NegativeInfinity], PixelComponentType.Float);

        // Act: inspect all four represented channels with the RGB peak operation.
        var result = ImageAssertions.Statistics.InspectRgbPeak(image);

        // Assert: alpha's original component location is retained in the failure.
        Assert.Equal(1u, result.NonFinite);
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(result.AssertFiniteRange);
        Assert.Contains("channel=3", failure.Message);
    }

    /// <summary>RGB NaN and signed infinities fail the same full-domain finite assertion as other represented channels.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RgbPeakInspectionRejectsEveryRgbNonFiniteClass(int channel)
    {
        EnsureContextValid();
        // Arrange: choose a distinct IEEE nonfinite value in the nominated RGB lane.
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        float[] values = [1, 2, 3, 4];
        values[channel] = channel switch
        {
            0 => float.NaN,
            1 => float.PositiveInfinity,
            _ => float.NegativeInfinity
        };
        Upload(image, values, PixelComponentType.Float);

        // Act: collect the compact GPU result, then require the existing finite-range behavior.
        var result = ImageAssertions.Statistics.InspectRgbPeak(image);

        // Assert: failures retain the original image coordinate and RGB channel.
        Assert.Equal(1u, result.NonFinite);
        var failure = Assert.ThrowsAny<Xunit.Sdk.XunitException>(result.AssertFiniteRange);
        Assert.Contains($"channel={channel}", failure.Message);
        Assert.Contains("nonfinite=1", failure.Message);
    }

    #region Numeric predicates
    /// <summary>Stencil comparison covers odd rows and the final tail texel without changing original depth sampling or payload.</summary>
    [Fact]
    public void PackedStencilDomainDetectsFinalTexelAndPreservesDepthAspect()
    {
        // Arrange: author packed depth/stencil values whose final stencil texel differs from its predecessor.
        EnsureContextValid();
        bool scissorEnabled = GL.IsEnabled(EnableCap.ScissorTest);
        int[] scissorBox = new int[4]; GL.GetInteger(GetPName.ScissorBox, scissorBox);
        bool depthWrite = GL.GetBoolean(GetPName.DepthWritemask);
        int stencilWriteMask = GL.GetInteger(GetPName.StencilWritemask);
        int stencilBackWriteMask = GL.GetInteger(GetPName.StencilBackWritemask);
        double clearDepth = GL.GetDouble(GetPName.DepthClearValue);
        int clearStencil = GL.GetInteger(GetPName.StencilClearValue);
        try
        {
            using var texture = new DepthStencilTexture(65, 3);
            using var target = DynamicGpuFramebuffer.CreateDepthOnly(texture)!;
            using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
            target.Bind(); GL.Disable(EnableCap.ScissorTest); GL.StencilMask(0xff); GL.DepthMask(true);
            GL.ClearDepth(.5); GL.ClearStencil(0x36); GL.Clear(ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            var assertions = ImageAssertions;
            assertions.Comparisons.CompareStencil(texture, 0x36).AssertEqual();
            // Act: scissor a clear to the final texel, then read the complete stencil domain again.
            GL.Enable(EnableCap.ScissorTest); GL.Scissor(64, 2, 1, 1); GL.ClearStencil(0x59); GL.Clear(ClearBufferMask.StencilBufferBit);
            GL.Disable(EnableCap.ScissorTest);
            // Assert: only the final stencil value changes; depth inspection and its native aspect stay intact.
            Assert.Equal(1u, assertions.Comparisons.CompareStencil(texture, 0x36).Mismatches);
            assertions.Statistics.Inspect(texture, .499, .501).AssertFiniteRange();
            GL.GetTextureParameter(texture.TextureId, (GetTextureParameter)TextureParameterName.DepthStencilTextureMode, out int mode);
            Assert.Equal((int)PixelFormat.DepthComponent, mode);
        }
        finally
        {
            GL.Scissor(scissorBox[0], scissorBox[1], scissorBox[2], scissorBox[3]);
            GL.StencilMaskSeparate(StencilFace.Front, (uint)stencilWriteMask);
            GL.StencilMaskSeparate(StencilFace.Back, (uint)stencilBackWriteMask);
            GL.DepthMask(depthWrite);
            GL.ClearDepth(clearDepth);
            GL.ClearStencil(clearStencil);
            if (scissorEnabled) GL.Enable(EnableCap.ScissorTest);
            else GL.Disable(EnableCap.ScissorTest);
        }
    }

    /// <summary>All UNORM8 byte values and the final component survive GPU reconstruction over odd rows and a partial workgroup.</summary>
    [Fact]
    public void NormalizedByteDomainPreservesAllValuesAndFinalComponent()
    {
        // Arrange: author distinct normalized bytes through the final component of a two-layer image.
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.Rgba8,65,3);
        byte[] values=Enumerable.Range(0,65*3*4).Select(index=>(byte)(index%256)).ToArray();
        RHICommandContext.Current.UploadTexture(image,new(new(0),0,0,0,65,3),
            RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.UnsignedByte),values);
        var assertions = ImageAssertions;
        // Act: compare the complete byte image, then alter only the final component and read it again.
        assertions.Comparisons.CompareRgba8(image,values).AssertEqual();
        values[^1]^=1;
        // Assert: the final component is included in the comparison domain.
        Assert.Equal(1u,assertions.Comparisons.CompareRgba8(image,values).Mismatches);
    }

    /// <summary>Sparse authored positions retain the final texel and layer identity, and cannot silently skip an empty domain.</summary>
    [Fact]
    public void SparsePredicatesRetainEveryNominatedPoint()
    {
        // Arrange: nominate corners and an interior point across array layers, retaining duplicate selections.
        EnsureContextValid();
        var values=Enumerable.Repeat(1f,65*3*2).ToArray();values[^1]=3;
        using var image=Create(TextureDimension.TwoArray,TextureFormat.R32f,65,3,2);
        RHICommandContext.Current.UploadTexture(image,new(new(0),0,0,0,65,3,2),
            RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.Float),MemoryMarshal.AsBytes(values.AsSpan()));
        var assertions = ImageAssertions;
        (int X,int Y,int Layer)[] positions=[(0,0,0),(64,2,1)];
        // Act: evaluate both nominated points against the production predicate.
        var proof=assertions.Predicates.PredicateAt(image,positions, ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint);
        // Assert: both selected points are visited, the changed final texel is detected, and an empty domain is rejected.
        Assert.Equal(2u,proof.MatchingTexels);Assert.Equal(1u,proof.Mismatches);
        Assert.Throws<ArgumentException>(()=>assertions.Predicates.PredicateAt(image,[], ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint_2));
    }

    /// <summary>Ordered float accumulation preserves cancellation, per-texel expression rounding and the final odd-domain texel.</summary>
    [Fact]
    public void OrderedFloatReductionRetainsOriginalArithmetic()
    {
        // Arrange: author values whose ordered accumulation differs from reordered or higher-precision sums.
        EnsureContextValid();
        var values=new float[65*3*4];
        values[0]=16777216;values[4]=1;values[8]=-16777216;values[^4]=3;
        values[^3]=.25f;values[^2]=.125f;
        using var image=Create(TextureDimension.Two,TextureFormat.Rgba32f,65,3);
        Upload(image,values,PixelComponentType.Float);
        var assertions = ImageAssertions;
        float red=0,rgb=0;
        for(int i=0;i<values.Length;i+=4){red+=values[i];rgb+=(values[i]+values[i+1])+values[i+2];}
        // Act: read each ordered scalar reduction using its independent original arithmetic expression.
        // Assert: both returned sums match the CPU sequence, including cancellation and the final tail texel.
        Assert.Equal(red,assertions.Statistics.OrderedFloatSum(image, ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic));
        Assert.Equal(rgb,assertions.Statistics.OrderedFloatSum(image, ObservationProjections.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2));
    }
    /// <summary>Exact comparison preserves numeric zero, NaN/infinity and float subnormals without an arithmetic flush.</summary>
    [Fact]
    public void ExactAndTolerantFloatContractsRemainIndependent()
    {
        // Arrange: retain one authored image while each comparison mode receives distinct bounds.
        EnsureContextValid();
        float[] values = [0, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity];
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 8, 1);
        Upload(image, values, PixelComponentType.Float);
        var assertions = ImageAssertions;
        // Act: read exact bits and then evaluate the authored tolerance, range, and constant-value cases.
        var equal = assertions.Comparisons.Compare(image, values);
        // Assert: the exact path visits every component and reports all nonfinite values.
        equal.AssertEqual(); Assert.Equal(8u, equal.ComparedComponents); Assert.Equal(3u, assertions.Statistics.Inspect(image).NonFinite);
        float[] wrong = values.ToArray(); wrong[2] = 0;
        var mismatch = assertions.Comparisons.Compare(image, wrong);
        // Assert: exact mismatch diagnostics include the changed component and its expected/observed values.
        Assert.Equal(1u, mismatch.Mismatches);
        Assert.Contains("(2,0,0), channel=0", mismatch.ToString());
        Assert.Contains("actual=", mismatch.ToString()); Assert.Contains("expected=", mismatch.ToString());
        Assert.Contains("0x00000001", mismatch.ToString());
        assertions.Comparisons.Compare(image, wrong, tolerance: (double)float.Epsilon).AssertEqual();
        wrong[6] = float.NegativeInfinity;
        Assert.Equal(2u, assertions.Comparisons.Compare(image, wrong).Mismatches);
        using var finite = Create(TextureDimension.Two, TextureFormat.R32f, 4, 1);
        Upload(finite, new float[] { -2, 0, 1, 3 }, PixelComponentType.Float);
        // Act: exercise range, constant, strided, double, interval, and tolerant observations independently.
        var range = assertions.Statistics.Inspect(finite, -1, 2);
        Assert.Equal(2u, range.OutsideRange); Assert.Equal(2u, range.Positive);
        Assert.Equal(-2d, range.Minimum); Assert.Equal(3d, range.Maximum);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(range.AssertFiniteRange);
        // Constant observations keep the same exact-value rules, including selected-channel and tail failures.
        Assert.Equal(3u, assertions.Comparisons.CompareConstant(finite, 0).Mismatches);
        using var constant = Create(TextureDimension.Two, TextureFormat.R32f, 65, 3);
        Upload(constant, Enumerable.Repeat(-2f, 195).ToArray(), PixelComponentType.Float);
        assertions.Comparisons.CompareConstant(constant, -2).AssertEqual();
        Assert.Equal(195u, assertions.Comparisons.CompareConstant(constant, 2).Mismatches);
        Assert.Equal(195u, assertions.Comparisons.CompareConstant(constant, -2).ConstantMatches);
        Assert.Equal(195u, assertions.Statistics.Inspect(constant).Nonzero);
        Assert.Equal(195u, assertions.Statistics.Inspect(constant, -2, -2).InsideRange);
        var strided = assertions.Statistics.Inspect(finite, pixelStride: 2);
        Assert.Equal(2u, strided.Components); Assert.Equal(-2d, strided.Minimum); Assert.Equal(1d, strided.Maximum);
        assertions.Comparisons.CompareDouble(finite, [-2d,0d,1d,3d], 0).AssertEqual();
        Assert.Equal(1u, assertions.Comparisons.CompareDouble(finite, [-2d,1e-10,1d,3d], 1e-11).Mismatches);
        assertions.Comparisons.CompareIntervals(finite, [-2d,0d,1d,3d], [-2d,0d,1d,3d]).AssertEqual();
        Assert.Equal(1u, assertions.Comparisons.CompareIntervals(finite,
            [double.NegativeInfinity,1e-10,double.NegativeInfinity,double.NegativeInfinity],
            [double.PositiveInfinity,1,double.PositiveInfinity,double.PositiveInfinity]).Mismatches);
        Assert.Equal(6d, assertions.Statistics.Statistics(finite, absolute: true).Sum(0));
        Assert.Equal(1u, assertions.Comparisons.CompareConstant(finite, 0, tolerance: 2).Mismatches);
    }

    /// <summary>Integer predicates retain the full 32-bit domain, rather than round-tripping through float.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IntegerValuesPreserveAllBits(bool signed)
    {
        // Arrange: include signed extrema and nontrivial unsigned high bits in the authored integer image.
        EnsureContextValid();
        uint[] bits = signed ? [0, 0x80000000, 0x7fffffff, uint.MaxValue] : [0, uint.MaxValue, 16777217, 0x80000000];
        using var image = Create(TextureDimension.Two, signed ? TextureFormat.R32i : TextureFormat.R32ui, 4, 1);
        RHICommandContext.Current.UploadTexture(image, new(new(0,0),0,0,0,4,1),
            new(PixelChannelLayout.RedInteger, signed ? PixelComponentType.Int : PixelComponentType.UnsignedInt), MemoryMarshal.AsBytes(bits.AsSpan()));
        var assertions = ImageAssertions;
        // Act: compare exact integer bit patterns, then flip one bit in the authored image.
        assertions.Comparisons.CompareBits(image, bits).AssertEqual();
        bits[2] ^= 1;
        // Assert: no float conversion hides the one-bit mutation.
        Assert.Equal(1u, assertions.Comparisons.CompareBits(image, bits).Mismatches);
    }

    /// <summary>Rank validation rejects duplicates, malformed values and an unchanged temporal slice.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void RankAndSliceNegativesAreObservable(int mutation)
    {
        // Arrange: keep neighboring texels valid and mutate only the selected rank or slice witness.
        EnsureContextValid();
        float[] data = [.125f,.375f,.625f,.875f,.375f,.625f,.875f,.125f];
        if (mutation == 1) data[7] = data[6];
        if (mutation == 2) data[5] = float.NaN;
        if (mutation == 3) Array.Copy(data, 0, data, 4, 4);
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 2, 4);
        Upload(image, data, PixelComponentType.Float);
        var assertions = ImageAssertions;
        // Act: validate rank/slice data after applying the row's single selected mutation.
        var result = assertions.Statistics.CheckRanks(image, 2, 2);
        // Assert: valid data passes while each malformed, duplicate or stale slice fails the same contract.
        if (mutation == 0) result.AssertRanks(2);
        else Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => result.AssertRanks(2));
    }
    #endregion

    #region Aggregate predicates
    /// <summary>Generic sums, moments, ratios and coverage retain independently calculated values over an odd complete domain.</summary>
    [Fact]
    public void StatisticsAndCoveredChannelsPreserveTheirAuthoredPredicates()
    {
        // Arrange: author nonuniform channels so full-image statistics and covered-channel predicates differ.
        EnsureContextValid();
        const int count = 129 * 67;
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 129, 67);
        var values = Enumerable.Range(0, count).SelectMany(i => new[] { i * .25f, i * .125f, i * .0625f, 1f }).ToArray();
        Upload(image, values, PixelComponentType.Float);
        var assertions = ImageAssertions;
        // Act: compute complete moments and coverage observations over both authored domains.
        var result = assertions.Statistics.Statistics(image);
        result.AssertFiniteRange();
        for (int c = 0; c < 4; c++)
        {
            var channel = values.Where((_, index) => index % 4 == c).Select(v => (double)v).ToArray();
            Assert.Equal(channel.Sum(), result.Sum(c));
            Assert.Equal(channel.Sum(v => v * v), result.SumSquares(c));
        }
        assertions.Comparisons.CompareChannelRatios(image, new(0, .5f, .25f, 0), 0).AssertEqual();
        Assert.Equal((uint)(count - 1), assertions.Comparisons.CompareChannelRatios(image, new(0, .25f, .25f, 0), 0).Mismatches);
        using var coverage = Create(TextureDimension.Two, TextureFormat.Rgba32f, 3, 1);
        Upload(coverage, new float[] {0,0,0,0, .9f,0,0,1, .95f,0,0,.5f}, PixelComponentType.Float);
        assertions.Statistics.InspectCoverage(coverage, .88f, 1).AssertFiniteRange();
        assertions.Comparisons.CompareSelected(coverage, new Dictionary<int,System.Numerics.Vector3> { [1] = new(.9f,0,0) }, 0).AssertEqual();
        Upload(coverage, new float[] {.1f,0,0,0, .87f,0,0,1, .95f,0,0,.5f}, PixelComponentType.Float);
        Assert.Equal(2u, assertions.Statistics.InspectCoverage(coverage, .88f, 1).OutsideRange);
        Assert.Equal(1u, assertions.Comparisons.CompareSelected(coverage,
            new Dictionary<int,System.Numerics.Vector3> { [1] = new(.9f,0,0) }, 0).Mismatches);
        Assert.Equal(2u, assertions.Statistics.CountMatching(coverage,
            new(MathF.BitIncrement(.01f),float.NegativeInfinity,float.NegativeInfinity,float.Epsilon),
            new(float.PositiveInfinity), channelMask: 9).MatchingTexels);
        Assert.Equal(1u, assertions.Statistics.CountMatching(coverage, new(.9f),
            new(float.PositiveInfinity), channelMask: 7, requireAll: false).MatchingTexels);
        // In one periodic row the independent second differences are 10,2,2,-14.
        using var field = Create(TextureDimension.Two, TextureFormat.R32f, 4, 1);
        Upload(field, new float[] {0,1,4,9}, PixelComponentType.Float);
        // Assert: independent channel, stencil and weighted references match their distinct reduction contracts.
        var equation = assertions.Statistics.PeriodicLaplacianResidual(field, [1,0,0,2]);
        equation.AssertFiniteRange(); Assert.Equal(-3d, equation.Sum(0)); Assert.Equal(345d, equation.SumSquares(0));
        assertions.Comparisons.Compare(field, [0,1,4,9]).AssertEqual();
        assertions.Comparisons.CompareRelation(field, field, 1, 0, 0, channelMask: 1).AssertEqual();
        Assert.Equal(3u, assertions.Comparisons.CompareRelation(field, field, 2, 0, 0, channelMask: 1).Mismatches);
        Assert.Equal(2u, assertions.Statistics.CountExcess(field, field, field, .5f, 0, 1).MatchingTexels);
        double[] weights=[.125,.25+Math.ScaleB(1d,-40),.5,1.5];
        Assert.Equal(15.75+Math.ScaleB(1d,-40),assertions.Statistics.WeightedStatistics(field,weights,channelMask:1).Sum(0));
        Assert.Equal(31.5+Math.ScaleB(1d,-39),assertions.Statistics.WeightedStatistics(field,weights,
            channelMask:1,secondaryWeights:[2,2,2,2]).Sum(0));
        Assert.Throws<ArgumentException>(()=>assertions.Statistics.WeightedStatistics(field,[1]));
        var floatProducts=assertions.Statistics.Statistics(coverage,channelMask:1,squareInFloat:true);
        Assert.Equal(new[]{.1f,.87f,.95f}.Sum(value=>(double)(value*value)),floatProducts.SumSquares(0));
    }

    /// <summary>Authored predicates cover every texel, correlate four inputs and independently count conditional witnesses.</summary>
    [Fact]
    public void AuthoredPredicatesRetainCorrespondingImagesAndWitnesses()
    {
        // Arrange: author matching and deliberately divergent inputs for each corresponding-image predicate.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 65, 3);
        using var reference = Create(TextureDimension.Two, TextureFormat.Rg32f, 65, 3);
        float[] values = Enumerable.Range(0,195).Select(index => (float)index).ToArray();
        Upload(image, values, PixelComponentType.Float);
        Upload(reference, values.SelectMany(value => new[] { value * 2, 1f }).ToArray(), PixelComponentType.Float);
        var assertions = ImageAssertions;

        // Act: evaluate corresponding-source predicates before and after mutations of their selected sources.
        var result = assertions.Predicates.Predicate(image, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses, reference, image, image);
        result.AssertEqual(); Assert.Equal(98u,result.MatchingTexels);
        // A fault in the final tail texel must be reported, even though earlier workgroups satisfy the contract.
        values[^1] = -1; Upload(image, values, PixelComponentType.Float);
        result = assertions.Predicates.Predicate(image, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses, reference, image, image);
        Assert.Equal(1u,result.Mismatches); Assert.Equal(98u,result.MatchingTexels);
        using var mismatched = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1);
        Assert.Throws<ArgumentException>(() => assertions.Predicates.Predicate(image, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses, mismatched));
        Upload(mismatched,new float[]{7},PixelComponentType.Float);
        result=assertions.Predicates.Predicate(image, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_2,mismatched,mappedSources:true);
        result.AssertEqual();Assert.Equal(195u,result.MatchingTexels);
        Assert.Equal(0,result.FirstWitnessIndex);
        var tail=assertions.Predicates.Predicate(image, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_3);
        Assert.Equal(194,tail.FirstWitnessIndex);
        Assert.Throws<InvalidOperationException>(()=>{_=assertions.Predicates.Predicate(image, ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint_2).FirstWitnessIndex;});
        using var flags=Create(TextureDimension.Two,TextureFormat.Rg32f,2,1);
        using var previousFlags=Create(TextureDimension.Two,TextureFormat.Rg32f,2,1);
        Upload(flags,new[]{0f,BitConverter.UInt32BitsToSingle(1),float.NaN,-0f},PixelComponentType.Float);
        Upload(previousFlags,new[]{0f,BitConverter.UInt32BitsToSingle(2),float.NaN,0f},PixelComponentType.Float);
        result=assertions.Predicates.Predicate(flags, ObservationPredicates.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4,previousFlags);
        Assert.Equal(1u,result.Mismatches);
        var floating = assertions.Comparisons.CompareFloating(image,values,Enumerable.Repeat(0f,values.Length).ToArray());
        // Assert: selected correspondence, witness location, and floating comparison results retain the authored domain.
        floating.AssertEqual(); Assert.Equal((uint)values.Length, floating.ComparedComponents);
        float[] wanted=(float[])values.Clone();wanted[^1]=0;
        Assert.Equal(1u,assertions.Comparisons.CompareFloating(image,wanted,new float[wanted.Length]).Mismatches);
    }

    /// <summary>GPU-authored history copies retain complete odd domains and their own snapshots across later source mutation.</summary>
    [Fact]
    public void HistoryProjectionsRetainCompleteIndependentImages()
    {
        // Arrange: snapshot the source before mutation so history projections can be checked independently.
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.R32f,65,3);
        float[] values=Enumerable.Range(0,195).Select(index=>(float)index+.25f).ToArray();
        Upload(image,values,PixelComponentType.Float);
        var projection = ImageAssertions.Projections;var assertions = ImageAssertions;
        // Act: capture transformed history, mutate the source, and convert integer samples independently.
        using var transformed=projection.Apply(image, ObservationProjections.GpuImageAssertionTests_HistoryProjectionsRetainCompleteIndependentImages,new(2,0,0,0));
        float[] expected=values.SelectMany((value,index)=>new[]{value+2,(float)(index%65),(float)(index/65),1f}).ToArray();
        assertions.Comparisons.Compare(transformed,expected).AssertEqual();
        Upload(image,new float[195],PixelComponentType.Float);
        assertions.Comparisons.Compare(transformed,expected).AssertEqual();
        using var integer=Create(TextureDimension.Two,TextureFormat.R16ui,65,3);
        uint[] words=Enumerable.Range(0,195).Select(index=>(uint)index).ToArray();words[^1]=65535;
        RHICommandContext.Current.UploadTexture(integer,new(new(),0,0,0,65,3),
            RHICommandContext.Current.GetTextureEncoding(integer,PixelComponentType.UnsignedInt),MemoryMarshal.AsBytes(words.AsSpan()));
        using var converted=projection.Apply(integer, AoProjections.AoMinimumChildProductionTests_ValidateMasks);
        // Assert: transformed snapshots preserve their authored values after the source changes.
        assertions.Comparisons.Compare(converted,words.SelectMany(value=>new[]{(float)value,0f,0f,1f}).ToArray()).AssertEqual();
    }

    /// <summary>Maximum selection and two-dimensional export retain original array, volume and cube layer identities.</summary>
    [Theory]
    [InlineData(TextureDimension.TwoArray)] [InlineData(TextureDimension.Three)] [InlineData(TextureDimension.Cube)]
    public void LocalMaximumSelectionRetainsEveryLayer(TextureDimension dimension)
    {
        // Arrange: author a distinct brightest texel in every layer before selecting the complete domain.
        EnsureContextValid();int layers=dimension==TextureDimension.Cube?6:3;
        using var image=Create(dimension,TextureFormat.Rgba32f,2,2,layers);
        for(int layer=0;layer<layers;layer++)
            Upload(image,Enumerable.Range(0,4).SelectMany(_=>new[]{0f,(float)layer+1,0f,1f}).ToArray(),PixelComponentType.Float,layer);
        var assertions = ImageAssertions;
        // Act: select the maximum over all layers and export the chosen region to a two-dimensional plane.
        var maximum=assertions.Selection.Brightest(image);
        // Assert: selection value and exported plane retain the winning layer's original identity.
        Assert.Equal(new System.Numerics.Vector3(0,layers,0),maximum.Color);
        Assert.Equal(layers*.7152f,maximum.Luminance);
        Assert.Equal((0,0,0,1f),assertions.Selection.Extremum(image,1,false));
        var region=new RHITextureRegion(new(0,dimension==TextureDimension.Three?0:layers-1),0,0,
            dimension==TextureDimension.Three?layers-1:0,2,2);
        using var snapshot=new GpuImageSnapshot(image,region);using var plane=snapshot.ToPlane();
        assertions.Comparisons.CompareVector(plane,new(0,layers,0,1)).AssertEqual();
    }

    /// <summary>Neither incomplete partial sums nor a missing final reduction may report a successful aggregate.</summary>
    [Theory]
    [InlineData(2, false)] [InlineData(2, true)] [InlineData(3, false)]
    public void StatisticsRequireBothCompleteReductionStages(int suppressed, bool partial)
    {
        // Arrange: intercept the staged reduction and suppress or truncate exactly one dispatch.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 65, 3);
        Upload(image, Enumerable.Repeat(2f, 195).ToArray(), PixelComponentType.Float);
        var assertions = ImageAssertions; using var native = new NativeDriverCapture();
        var original = Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        int calls = 0;
        native.Add("glDispatchCompute", new Dispatch((x,y,z) =>
        {
            calls++;
            if (calls != suppressed) original(x,y,z);
            else if (partial) original(x-1,y,z);
        }));
        native.Install();
        // Act: request a two-stage reduction after one dispatch is suppressed or shortened.
        var failure = Assert.Throws<InvalidOperationException>(() => assertions.Statistics.Statistics(image));
        // Assert: the missing stage cannot produce a passing aggregate.
        Assert.Contains("GPU statistics did not complete", failure.Message);
        Assert.Equal(3, calls); native.RequireSuccessfulObservation();
    }
    #endregion

    #region Domains and history
    /// <summary>Every texel in each selected mip, region, volume layer or cube face reaches the comparison.</summary>
    [Theory]
    [InlineData(TextureDimension.Two)] [InlineData(TextureDimension.TwoArray)]
    [InlineData(TextureDimension.Three)] [InlineData(TextureDimension.Cube)]
    public void SelectionsAndHistoryRetainTheCompleteDomain(TextureDimension dimension)
    {
        // Arrange: author distinguishable values across every layer and mip in the selected source dimension.
        EnsureContextValid();
        int layers = dimension == TextureDimension.Cube ? 6 : dimension == TextureDimension.Two ? 1 : 3;
        using var image = Create(dimension, TextureFormat.R32f, 4, 4, layers, 2);
        for (int layer = 0; layer < layers; layer++)
            Upload(image, Enumerable.Range(0, 16).Select(index => (float)(index + layer * 16)).ToArray(), PixelComponentType.Float, layer);
        var selection = new RHITextureRegion(new(0, layers - 1), 1, 1, 0, 2, 2);
        float[] expected = [5+(layers-1)*16,6+(layers-1)*16,9+(layers-1)*16,10+(layers-1)*16];
        var assertions = ImageAssertions;
        // Act: compare full images and selected regions, retain a history snapshot, then mutate its source.
        assertions.Comparisons.Compare(image, Enumerable.Range(0,16*layers).Select(value=>(float)value).ToArray()).AssertEqual();
        assertions.Comparisons.Compare(image, expected, selection: selection).AssertEqual();
        using var previous = new GpuImageSnapshot(image, selection);
        assertions.Comparisons.CompareHistory(image, previous, selection).AssertEqual();
        RHICommandContext.Current.UploadTexture(image, new(new(0,layers-1),2,2,0,1,1),
            new(PixelChannelLayout.Red,PixelComponentType.Float),MemoryMarshal.AsBytes(new float[] {99}.AsSpan()));
        // Assert: only the changed selected coordinate differs from the retained snapshot.
        Assert.Equal(1u, assertions.Comparisons.CompareHistory(image, previous, selection).Mismatches);
        // A selected nonbase mip has its own extent and contents; no base-image resampling is permitted.
        Upload(image, new float[] {.1f,.2f,.3f,.4f}, PixelComponentType.Float, 0, 1, 2, 2);
        assertions.Comparisons.Compare(image, new float[] {.1f,.2f,.3f,.4f}, selection:new(new(1,0),0,0,0,2,2)).AssertEqual();
    }

    /// <summary>Depth is sampled as depth, while channel masks exclude only explicitly unasserted color components.</summary>
    [Fact]
    public void DepthAndChannelSelectionsPreserveTheirAspects()
    {
        EnsureContextValid();
        // Arrange depth and color images with known payloads before comparing their distinct readback aspects.
        using var depth = Create(TextureDimension.Two, TextureFormat.DepthComponent32f, 3, 1);
        Upload(depth, new float[] {.1f,.5f,.9f}, PixelComponentType.Float);
        var assertions = ImageAssertions;
        // Act: compare depth as its own aspect, then apply channel masks and history to color images.
        assertions.Comparisons.Compare(depth, new float[] {.1f,.5f,.9f}).AssertEqual();
        // Assert: depth sampling remains independent of the following color-channel checks.
        using var rgba = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(rgba, new float[] {1,2,3,4}, PixelComponentType.Float);
        assertions.Comparisons.Compare(rgba, new float[] {1,99,3,99}, channelMask:5).AssertEqual();
        Assert.Equal(2u, assertions.Comparisons.Compare(rgba, new float[] {1,99,3,99}).Mismatches);
        Assert.Equal(2u, assertions.Statistics.CountFlags(rgba, 0x00800000, 15).FlagMatches);
        Assert.Equal(1u, assertions.Comparisons.Compare(rgba, new float[] {1.1f,2.1f,3.1f,4},
            tolerance: .15f, channelMask: 7, vectorDistance: true).Mismatches);
        assertions.Comparisons.Compare(rgba, new float[] {1.1f,2.1f,3.1f,4},
            tolerance: .18f, channelMask: 7, vectorDistance: true).AssertEqual();
        Upload(rgba, new float[] {0,2,0,4}, PixelComponentType.Float);
        using var partial = new GpuImageSnapshot(rgba);
        Upload(rgba, new float[] {99,2,99,4}, PixelComponentType.Float);
        var retained = assertions.Comparisons.CompareHistory(rgba, partial, retainNonzero: true); retained.AssertEqual();
        Assert.Equal(2u, retained.ComparedComponents);
        Assert.Equal(2u, assertions.Comparisons.CompareHistory(rgba, partial).Mismatches);
        Upload(rgba, new float[] {0,3,0,4}, PixelComponentType.Float);
        Assert.Equal(1u, assertions.Comparisons.CompareHistory(rgba, partial, retainNonzero: true).Mismatches);
        Upload(rgba, new float[] {BitConverter.UInt32BitsToSingle(0x80000000),2,0,4}, PixelComponentType.Float);
        assertions.Comparisons.CompareHistory(rgba, partial).AssertEqual();
        Assert.Equal(1u, assertions.Comparisons.CompareHistory(rgba, partial, bitwise: true).Mismatches);
        var bitwise = assertions.Comparisons.Compare(rgba,[0,2,0,4],bitwise:true);
        Assert.Equal(1u, bitwise.Mismatches); Assert.Equal(4u, bitwise.ComparedComponents);
        using var encoded=Create(TextureDimension.Two,TextureFormat.Srgb8Alpha8,1,1);
        Upload(encoded,new float[] {.25f,.5f,.75f,1},PixelComponentType.Float);
        assertions.Comparisons.Compare(encoded,new float[] {64f/255,128f/255,191f/255,1},tolerance:1e-7).AssertEqual();
        using var packed=Create(TextureDimension.Two,TextureFormat.Depth24Stencil8,2,1);
        uint[] depthStencil=[0x000000ff,0xffffff12];
        RHICommandContext.Current.UploadTexture(packed,new(new(0,0),0,0,0,2,1),
            new(PixelChannelLayout.DepthStencil,PixelComponentType.UnsignedInt248),MemoryMarshal.AsBytes(depthStencil.AsSpan()));
        // Assert: packed depth/stencil retains its depth interpretation after channel and history checks.
        assertions.Comparisons.Compare(packed,new float[] {0,1}).AssertEqual();
    }

    /// <summary>Layer construction retains authored nearest resampling, exact copied values and zero expansion.</summary>
    [Fact]
    public void LayerCopiesAndConversionsPreserveOriginalContracts()
    {
        // Arrange: prepare typed source layers and conversion inputs with distinct expected payloads.
        EnsureContextValid();
        using var color = Create(TextureDimension.Two, TextureFormat.Rgba32f, 3, 2);
        float[] authored = Enumerable.Range(0,24).Select(index => index*.125f).ToArray();
        Upload(color, authored, PixelComponentType.Float);
        using var scalar = Create(TextureDimension.Two, TextureFormat.R32f, 2, 1);
        Upload(scalar, new float[] {.25f,.75f}, PixelComponentType.Float);
        using var layered = LayeredTestTexture.Create(ImageAssertions.Programs, color, scalar, null);
        float[] expected = new float[3*2*3*4]; Array.Copy(authored, expected, authored.Length);
        for (int y=0;y<2;y++)for(int x=0;x<3;x++) expected[(6+y*3+x)*4] = x*2/3==0 ? .25f : .75f;
        var assertions = ImageAssertions;
        // Act: compare resampled/copied layers and additive layer sums against separately authored results.
        assertions.Comparisons.Compare(layered, expected).AssertEqual();
        using var terms = Create(TextureDimension.TwoArray, TextureFormat.Rgba32f, 2, 1, 3);
        Upload(terms, new float[] {1e20f,1e20f,1e20f,1e20f,1,1,1,1}, PixelComponentType.Float, 0);
        Upload(terms, new float[] {-1e20f,-1e20f,-1e20f,-1e20f,2,2,2,2}, PixelComponentType.Float, 1);
        Upload(terms, new float[] {3,3,3,3,3,3,3,3}, PixelComponentType.Float, 2);
        using var sum = Create(TextureDimension.Two, TextureFormat.Rgba32f, 2, 1);
        Upload(sum, new float[] {0,0,0,0,6,6,6,6}, PixelComponentType.Float);
        assertions.Comparisons.Compare(sum, new float[8], additiveLayers: terms).AssertEqual();
        // Assert: layer addition preserves order-sensitive arithmetic instead of matching a substituted sum.
        Assert.Equal(8u, assertions.Comparisons.Compare(sum, Enumerable.Repeat(1f,8).ToArray(), additiveLayers: terms).Mismatches);
    }
    #endregion

    #region Execution failures
    /// <summary>Incomplete native dispatch cannot turn zeroed counters into a passing assertion.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SuppressedNativeDispatchFailsTheCompletionSentinel(bool partial)
    {
        // Arrange: intercept compute dispatch while retaining the original driver call for full mode.
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 129, 1);
        Upload(image, Enumerable.Range(0,129).Select(value=>(float)value).ToArray(), PixelComponentType.Float);
        var assertions = ImageAssertions;
        using var native = new NativeDriverCapture();
        int calls=0;
        var original=Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        native.Add("glDispatchCompute",new Dispatch((x,y,z)=>{calls++;if(partial)original(x-1,y,z);})); native.Install();
        // Act: request a complete statistics observation after the intercepted dispatch is suppressed or shortened.
        var error=Assert.Throws<InvalidOperationException>(() => assertions.Statistics.Inspect(image));
        // Assert: completion diagnostics identify the missing workgroup instead of accepting reset output.
        if(partial)Assert.Contains("texels=128/129",error.Message);
        Assert.Equal(1,calls); native.RequireSuccessfulObservation();
    }

    /// <summary>The independent histogram-validation dispatch must complete even when the first rank pass succeeds.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RankValidationRequiresItsOwnCompleteDispatch(bool partial)
    {
        // Arrange: intercept rank validation and optionally omit its final workgroup.
        EnsureContextValid();
        const int side=16,ranks=side*side;
        float[] values=Enumerable.Range(0,ranks*2).Select(index=>((index+index/ranks)%ranks+.5f)/ranks).ToArray();
        using var image=Create(TextureDimension.Two,TextureFormat.R32f,side,side*2);
        Upload(image,values,PixelComponentType.Float);
        var assertions = ImageAssertions;using var native=new NativeDriverCapture();
        var original=Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        int calls=0;
        native.Add("glDispatchCompute",new Dispatch((x,y,z)=>{calls++;if(calls==1)original(x,y,z);else if(partial)original(x-1,y,z);}));
        native.Install();
        // Act: execute rank validation after its first pass succeeds, optionally truncating the second pass.
        var failure=Assert.Throws<InvalidOperationException>(()=>assertions.Statistics.CheckRanks(image,side,2));
        // Assert: the independent histogram pass cannot silently accept incomplete validation data.
        Assert.Contains("GPU rank validation did not complete",failure.Message);
        if(partial)Assert.Contains("ranks=448/512",failure.Message);
        Assert.Equal(2,calls);native.RequireSuccessfulObservation();
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Creates a fresh native subject for each numeric/domain contract.</summary>
    private static GpuTexture Create(TextureDimension dimension,TextureFormat format,int width,int height,int depth=1,int levels=1)
    {
        return RHICommandContext.Current.CreateTexture(new RHITextureDescription {Dimension=dimension,Format=format,
            Width=width,Height=height,Depth=dimension==TextureDimension.Cube?1:depth,MipLevels=levels,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest}).RequireResource();
    }

    /// <summary>Uploads authored values through the production transfer boundary, with no readback-derived expectation.</summary>
    private static void Upload(GpuTexture image,float[] values,PixelComponentType type,int layer=0,int mip=0,int? width=null,int? height=null)
    {
        var encoding=RHICommandContext.Current.GetTextureEncoding(image,type);
        RHICommandContext.Current.UploadTexture(image,new(new(mip,layer),0,0,0,width??image.Width,height??image.Height),encoding,MemoryMarshal.AsBytes(values.AsSpan()));
    }

    /// <summary>Matches the native dispatch ABI for deliberate no-execution fault injection.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void Dispatch(uint x,uint y,uint z);
    /// <summary>Matches native program creation for observing unexpected per-consumer executable construction.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint CreateProgramNative();
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("compare", 1, 0);
        programs.Prepare("compare", 2, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("inspection", 1, 0);
        programs.Prepare("inspection", 2, 0);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("predicate-observation", 0, 0);
        programs.Prepare("predicate-observation-points1");
        programs.Prepare("predicate-observation-points2");
        programs.Prepare("predicate-observation-points3");
        programs.Prepare("predicate-observation-points4");
        programs.Prepare("projection-ao", 0, 0);
        programs.Prepare("projection-ao", 2, 0);
        programs.Prepare("projection-observation", 0, 0);
        programs.Prepare("ranks", 0, 0);
        programs.Prepare("statistics", 0, 0);
        programs.Prepare("statistics-poisson", 0, 0);
        programs.Prepare("statistics", 0, 2);
        programs.Prepare("statistics", 0, 3);
    }
    #endregion
}
