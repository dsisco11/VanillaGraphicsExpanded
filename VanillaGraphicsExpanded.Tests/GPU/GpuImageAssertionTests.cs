using ErrorCode = OpenTK.Graphics.OpenGL4.ErrorCode;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies the compact GPU oracle against authored data, deliberate failures and hostile native state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuImageAssertionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Production executable ownership
    /// <summary>The production numeric block preserves both whole-buffer and ranged caller bindings.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ProductionCheckerPreservesUniformSelectors(bool range)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1);
        Upload(image, [1f], PixelComponentType.Float);
        int selected = GL.GenBuffer(), generic = GL.GenBuffer();
        try
        {
            GL.BindBuffer(BufferTarget.UniformBuffer, selected);
            GL.BufferData(BufferTarget.UniformBuffer, 256, IntPtr.Zero, BufferUsageHint.StaticDraw);
            if (range) GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 28, selected, IntPtr.Zero, 16);
            else GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 28, selected);
            GL.BindBuffer(BufferTarget.UniformBuffer, generic);
            using var check = new GpuImageAssertions();
            check.CompareConstant(image, 1).AssertEqual();
            Assert.Equal(generic, GL.GetInteger(GetPName.UniformBufferBinding));
            GL.GetInteger((GetIndexedPName)All.UniformBufferBinding, 28, out int restored);
            Assert.Equal(selected, restored);
            var cache = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGlStateCache.Current;
            Assert.True(cache.TryGetCachedIndexedBuffer(OpenTK.Graphics.OpenGL.BufferRangeTarget.UniformBuffer,
                28, out int cached, out nint offset, out nint size));
            Assert.Equal(selected, cached); Assert.Equal((nint)0, offset);
            Assert.Equal(range ? (nint)16 : 0, size);
        }
        finally
        {
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 28, 0);
            GL.BindBuffer(BufferTarget.UniformBuffer, 0);
            GL.DeleteBuffer(selected); GL.DeleteBuffer(generic);
        }
    }

    /// <summary>Independent consumers reuse one production executable while inputs, results and cleanup remain isolated.</summary>
    [Fact]
    public void ProductionOwnerSurvivesConsumersAndPublishesFreshInputs()
    {
        EnsureContextValid();
        var owner = fixture.AssertionProgram(0, 0);
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
        using (var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 4, 2))
        using (var first = new GpuImageAssertions())
        {
            Upload(image, Enumerable.Repeat(.25f, 32).ToArray(), PixelComponentType.Float);
            first.CompareConstant(image, .25f).AssertEqual();
        }
        // Per-test resources drain without retiring the fixture manager or its checker ownership.
        fixture.CleanupGpuResources();
        fixture.EnsureContextValid();
        Assert.True(GL.IsProgram(program));
        Assert.True(owner.IsValid);
        using var replacement = Create(TextureDimension.Two, TextureFormat.Rgba32f, 4, 2);
        using (var second = new GpuImageAssertions())
        {
            Upload(replacement, Enumerable.Repeat(.25f, 32).ToArray(), PixelComponentType.Float);
            second.CompareConstant(replacement, .25f).AssertEqual();
            Upload(replacement, Enumerable.Repeat(.75f, 32).ToArray(), PixelComponentType.Float);
            second.CompareConstant(replacement, .75f).AssertEqual();
            Assert.Equal(32u, second.CompareConstant(replacement, .25f).Mismatches);
            Assert.Throws<ArgumentOutOfRangeException>(() => second.Predicate(replacement, (GpuAssertionPredicate)int.MaxValue));
        }
        Assert.Same(owner, fixture.AssertionProgram(0, 0));
        Assert.Equal(program, owner.ProgramId);
        Assert.Equal(0, creations);
        native.RequireSuccessfulObservation();
    }
    #endregion

    #region Numeric predicates
    /// <summary>Stencil comparison covers odd rows and the final tail texel without changing original depth sampling or payload.</summary>
    [Fact]
    public void PackedStencilDomainDetectsFinalTexelAndPreservesDepthAspect()
    {
        EnsureContextValid();
        using var texture=new DepthStencilTexture(65,3);
        using var target=DynamicGpuFramebuffer.CreateDepthOnly(texture)!;
        using var bindings=VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGlStateCache.Current.BindFramebufferScope();
        target.Bind();GL.Disable(EnableCap.ScissorTest);GL.StencilMask(0xff);GL.DepthMask(true);
        GL.ClearDepth(.5);GL.ClearStencil(0x36);GL.Clear(ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);
        using var assertions=new GpuImageAssertions();
        assertions.CompareStencil(texture,0x36).AssertEqual();
        // Modify exactly the final texel, independently of the compute checker.
        GL.Enable(EnableCap.ScissorTest);GL.Scissor(64,2,1,1);GL.ClearStencil(0x59);GL.Clear(ClearBufferMask.StencilBufferBit);
        GL.Disable(EnableCap.ScissorTest);
        Assert.Equal(1u,assertions.CompareStencil(texture,0x36).Mismatches);
        assertions.Inspect(texture,.499,.501).AssertFiniteRange();
        GL.GetTextureParameter(texture.TextureId,(GetTextureParameter)TextureParameterName.DepthStencilTextureMode,out int mode);
        Assert.Equal((int)PixelFormat.DepthComponent,mode);
    }

    /// <summary>A local float tuple preserves explicit layer selection and IEEE payloads without a copied enclosing image.</summary>
    [Fact]
    public void LocalFloatTuplePreservesLayerAndPayload()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.TwoArray,TextureFormat.Rgba32f,2,1,2);
        float[] values=[1,2,3,4, 5,6,7,8, 9,10,11,12, float.NaN,float.Epsilon,-0f,float.PositiveInfinity];
        RHICommandContext.Current.UploadTexture(image,new(new(0),0,0,0,2,1,2),
            RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.Float),MemoryMarshal.AsBytes(values.AsSpan()));
        Assert.Equal(Array.ConvertAll(values[12..],BitConverter.SingleToUInt32Bits),
            Array.ConvertAll(GpuLocalRead.Tuple(image,1,0,1),BitConverter.SingleToUInt32Bits));
    }

    /// <summary>All UNORM8 byte values and the final component survive GPU reconstruction over odd rows and a partial workgroup.</summary>
    [Fact]
    public void NormalizedByteDomainPreservesAllValuesAndFinalComponent()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.Rgba8,65,3);
        byte[] values=Enumerable.Range(0,65*3*4).Select(index=>(byte)(index%256)).ToArray();
        RHICommandContext.Current.UploadTexture(image,new(new(0),0,0,0,65,3),
            RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.UnsignedByte),values);
        using var assertions=new GpuImageAssertions();
        assertions.CompareRgba8(image,values).AssertEqual();
        values[^1]^=1;
        Assert.Equal(1u,assertions.CompareRgba8(image,values).Mismatches);
    }

    /// <summary>Sparse authored positions retain the final texel and layer identity, and cannot silently skip an empty domain.</summary>
    [Fact]
    public void SparsePredicatesRetainEveryNominatedPoint()
    {
        EnsureContextValid();
        var values=Enumerable.Repeat(1f,65*3*2).ToArray();values[^1]=3;
        using var image=Create(TextureDimension.TwoArray,TextureFormat.R32f,65,3,2);
        RHICommandContext.Current.UploadTexture(image,new(new(0),0,0,0,65,3,2),
            RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.Float),MemoryMarshal.AsBytes(values.AsSpan()));
        using var assertions=new GpuImageAssertions();
        (int X,int Y,int Layer)[] positions=[(0,0,0),(64,2,1)];
        var proof=assertions.PredicateAt(image,positions, GpuAssertionPredicate.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint);
        Assert.Equal(2u,proof.MatchingTexels);Assert.Equal(1u,proof.Mismatches);
        Assert.Throws<ArgumentException>(()=>assertions.PredicateAt(image,[], GpuAssertionPredicate.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint_2));
    }

    /// <summary>Local byte tuples preserve normalized metadata bytes and the explicitly selected array layer.</summary>
    [Fact]
    public void LocalRgba8TuplePreservesOriginalBytes()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.TwoArray,TextureFormat.Rgba8,2,1,2);
        byte[] values=[0,1,127,255, 17,33,65,129, 255,128,127,0, 1,127,128,254];
        RHICommandContext.Current.UploadTexture(image,new(new(0),0,0,0,2,1,2),
            RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.UnsignedByte),values);
        Assert.Equal(values[..4],GpuLocalRead.Rgba8(image,0,0));
        Assert.Equal(values[12..],GpuLocalRead.Rgba8(image,1,0,1));
    }

    /// <summary>Ordered float accumulation preserves cancellation, per-texel expression rounding and the final odd-domain texel.</summary>
    [Fact]
    public void OrderedFloatReductionRetainsOriginalArithmetic()
    {
        EnsureContextValid();
        var values=new float[65*3*4];
        values[0]=16777216;values[4]=1;values[8]=-16777216;values[^4]=3;
        values[^3]=.25f;values[^2]=.125f;
        using var image=Create(TextureDimension.Two,TextureFormat.Rgba32f,65,3);
        Upload(image,values,PixelComponentType.Float);
        using var assertions=new GpuImageAssertions();
        float red=0,rgb=0;
        for(int i=0;i<values.Length;i+=4){red+=values[i];rgb+=(values[i]+values[i+1])+values[i+2];}
        Assert.Equal(red,assertions.OrderedFloatSum(image, GpuImageProjection.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic));
        Assert.Equal(rgb,assertions.OrderedFloatSum(image, GpuImageProjection.GpuImageAssertionTests_OrderedFloatReductionRetainsOriginalArithmetic_2));
    }
    /// <summary>Exact comparison preserves numeric zero, NaN/infinity and float subnormals without an arithmetic flush.</summary>
    [Fact]
    public void ExactAndTolerantFloatContractsRemainIndependent()
    {
        EnsureContextValid();
        float[] values = [0, -0f, float.Epsilon, -float.Epsilon, float.MaxValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity];
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 8, 1);
        Upload(image, values, PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        var equal = assertions.Compare(image, values); equal.AssertEqual(); Assert.Equal(3u, equal.NonFinite);
        float[] wrong = values.ToArray(); wrong[2] = 0;
        var mismatch = assertions.Compare(image, wrong); Assert.Equal(1u, mismatch.Mismatches);
        Assert.Contains("(2,0,0), channel=0", mismatch.ToString());
        assertions.Compare(image, wrong, tolerance: (double)float.Epsilon).AssertEqual();
        wrong[6] = float.NegativeInfinity;
        Assert.Equal(2u, assertions.Compare(image, wrong).Mismatches);
        using var finite = Create(TextureDimension.Two, TextureFormat.R32f, 4, 1);
        Upload(finite, new float[] { -2, 0, 1, 3 }, PixelComponentType.Float);
        var range = assertions.Inspect(finite, -1, 2);
        Assert.Equal(2u, range.OutsideRange); Assert.Equal(2u, range.Positive);
        Assert.Equal(-2d, range.Minimum); Assert.Equal(3d, range.Maximum);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(range.AssertFiniteRange);
        // Constant observations keep the same exact-value rules, including selected-channel and tail failures.
        Assert.Equal(3u, assertions.CompareConstant(finite, 0).Mismatches);
        using var constant = Create(TextureDimension.Two, TextureFormat.R32f, 65, 3);
        Upload(constant, Enumerable.Repeat(-2f, 195).ToArray(), PixelComponentType.Float);
        assertions.CompareConstant(constant, -2).AssertEqual();
        Assert.Equal(195u, assertions.CompareConstant(constant, 2).Mismatches);
        Assert.Equal(195u, assertions.CompareConstant(constant, -2).ConstantMatches);
        Assert.Equal(195u, assertions.Inspect(constant).Nonzero);
        Assert.Equal(195u, assertions.Inspect(constant, -2, -2).InsideRange);
        var strided = assertions.Inspect(finite, pixelStride: 2);
        Assert.Equal(2u, strided.Components); Assert.Equal(-2d, strided.Minimum); Assert.Equal(1d, strided.Maximum);
        assertions.CompareDouble(finite, [-2d,0d,1d,3d], 0).AssertEqual();
        Assert.Equal(1u, assertions.CompareDouble(finite, [-2d,1e-10,1d,3d], 1e-11).Mismatches);
        assertions.CompareIntervals(finite, [-2d,0d,1d,3d], [-2d,0d,1d,3d]).AssertEqual();
        Assert.Equal(1u, assertions.CompareIntervals(finite,
            [double.NegativeInfinity,1e-10,double.NegativeInfinity,double.NegativeInfinity],
            [double.PositiveInfinity,1,double.PositiveInfinity,double.PositiveInfinity]).Mismatches);
        Assert.Equal(6d, assertions.Statistics(finite, absolute: true).Sum(0));
        Assert.Equal(1u, assertions.CompareConstant(finite, 0, tolerance: 2).Mismatches);
    }

    /// <summary>Integer predicates retain the full 32-bit domain, rather than round-tripping through float.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void IntegerValuesPreserveAllBits(bool signed)
    {
        EnsureContextValid();
        uint[] bits = signed ? [0, 0x80000000, 0x7fffffff, uint.MaxValue] : [0, uint.MaxValue, 16777217, 0x80000000];
        using var image = Create(TextureDimension.Two, signed ? TextureFormat.R32i : TextureFormat.R32ui, 4, 1);
        RHICommandContext.Current.UploadTexture(image, new(new(0,0),0,0,0,4,1),
            new(PixelChannelLayout.RedInteger, signed ? PixelComponentType.Int : PixelComponentType.UnsignedInt), MemoryMarshal.AsBytes(bits.AsSpan()));
        using var assertions = new GpuImageAssertions();
        assertions.CompareBits(image, bits).AssertEqual();
        bits[2] ^= 1;
        Assert.Equal(1u, assertions.CompareBits(image, bits).Mismatches);
    }

    /// <summary>Rank validation rejects duplicates, malformed values and an unchanged temporal slice.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void RankAndSliceNegativesAreObservable(int mutation)
    {
        EnsureContextValid();
        float[] data = [.125f,.375f,.625f,.875f,.375f,.625f,.875f,.125f];
        if (mutation == 1) data[7] = data[6];
        if (mutation == 2) data[5] = float.NaN;
        if (mutation == 3) Array.Copy(data, 0, data, 4, 4);
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 2, 4);
        Upload(image, data, PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        var result = assertions.CheckRanks(image, 2, 2);
        if (mutation == 0) result.AssertRanks(2);
        else Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => result.AssertRanks(2));
    }
    #endregion

    #region Aggregate predicates
    /// <summary>Generic sums, moments, ratios and coverage retain independently calculated values over an odd complete domain.</summary>
    [Fact]
    public void StatisticsAndCoveredChannelsPreserveTheirAuthoredPredicates()
    {
        EnsureContextValid();
        const int count = 129 * 67;
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 129, 67);
        var values = Enumerable.Range(0, count).SelectMany(i => new[] { i * .25f, i * .125f, i * .0625f, 1f }).ToArray();
        Upload(image, values, PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        var result = assertions.Statistics(image);
        result.AssertFiniteRange();
        for (int c = 0; c < 4; c++)
        {
            var channel = values.Where((_, index) => index % 4 == c).Select(v => (double)v).ToArray();
            Assert.Equal(channel.Sum(), result.Sum(c));
            Assert.Equal(channel.Sum(v => v * v), result.SumSquares(c));
        }
        assertions.CompareChannelRatios(image, new(0, .5f, .25f, 0), 0).AssertEqual();
        Assert.Equal((uint)(count - 1), assertions.CompareChannelRatios(image, new(0, .25f, .25f, 0), 0).Mismatches);
        using var coverage = Create(TextureDimension.Two, TextureFormat.Rgba32f, 3, 1);
        Upload(coverage, new float[] {0,0,0,0, .9f,0,0,1, .95f,0,0,.5f}, PixelComponentType.Float);
        assertions.InspectCoverage(coverage, .88f, 1).AssertFiniteRange();
        assertions.CompareSelected(coverage, new Dictionary<int,System.Numerics.Vector3> { [1] = new(.9f,0,0) }, 0).AssertEqual();
        Upload(coverage, new float[] {.1f,0,0,0, .87f,0,0,1, .95f,0,0,.5f}, PixelComponentType.Float);
        Assert.Equal(2u, assertions.InspectCoverage(coverage, .88f, 1).OutsideRange);
        Assert.Equal(1u, assertions.CompareSelected(coverage,
            new Dictionary<int,System.Numerics.Vector3> { [1] = new(.9f,0,0) }, 0).Mismatches);
        Assert.Equal(2u, assertions.CountMatching(coverage,
            new(MathF.BitIncrement(.01f),float.NegativeInfinity,float.NegativeInfinity,float.Epsilon),
            new(float.PositiveInfinity), channelMask: 9).MatchingTexels);
        Assert.Equal(1u, assertions.CountMatching(coverage, new(.9f),
            new(float.PositiveInfinity), channelMask: 7, requireAll: false).MatchingTexels);
        // In one periodic row the independent second differences are 10,2,2,-14.
        using var field = Create(TextureDimension.Two, TextureFormat.R32f, 4, 1);
        Upload(field, new float[] {0,1,4,9}, PixelComponentType.Float);
        var equation = assertions.PeriodicLaplacianResidual(field, [1,0,0,2]);
        equation.AssertFiniteRange(); Assert.Equal(-3d, equation.Sum(0)); Assert.Equal(345d, equation.SumSquares(0));
        assertions.Compare(field, [0,1,4,9]).AssertEqual();
        assertions.CompareRelation(field, field, 1, 0, 0, channelMask: 1).AssertEqual();
        Assert.Equal(3u, assertions.CompareRelation(field, field, 2, 0, 0, channelMask: 1).Mismatches);
        Assert.Equal(2u, assertions.CountExcess(field, field, field, .5f, 0, 1).MatchingTexels);
        double[] weights=[.125,.25+Math.ScaleB(1d,-40),.5,1.5];
        Assert.Equal(15.75+Math.ScaleB(1d,-40),assertions.WeightedStatistics(field,weights,channelMask:1).Sum(0));
        Assert.Equal(31.5+Math.ScaleB(1d,-39),assertions.WeightedStatistics(field,weights,
            channelMask:1,secondaryWeights:[2,2,2,2]).Sum(0));
        Assert.Throws<ArgumentException>(()=>assertions.WeightedStatistics(field,[1]));
        var floatProducts=assertions.Statistics(coverage,channelMask:1,squareInFloat:true);
        Assert.Equal(new[]{.1f,.87f,.95f}.Sum(value=>(double)(value*value)),floatProducts.SumSquares(0));
    }

    /// <summary>Authored predicates cover every texel, correlate four inputs and independently count conditional witnesses.</summary>
    [Fact]
    public void AuthoredPredicatesRetainCorrespondingImagesAndWitnesses()
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 65, 3);
        using var reference = Create(TextureDimension.Two, TextureFormat.Rg32f, 65, 3);
        float[] values = Enumerable.Range(0,195).Select(index => (float)index).ToArray();
        Upload(image, values, PixelComponentType.Float);
        Upload(reference, values.SelectMany(value => new[] { value * 2, 1f }).ToArray(), PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();

        var result = assertions.Predicate(image, GpuAssertionPredicate.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses, reference, image, image);
        result.AssertEqual(); Assert.Equal(98u,result.MatchingTexels);
        // A fault in the final tail texel must be reported, even though earlier workgroups satisfy the contract.
        values[^1] = -1; Upload(image, values, PixelComponentType.Float);
        result = assertions.Predicate(image, GpuAssertionPredicate.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses, reference, image, image);
        Assert.Equal(1u,result.Mismatches); Assert.Equal(98u,result.MatchingTexels);
        using var mismatched = Create(TextureDimension.Two, TextureFormat.R32f, 1, 1);
        Assert.Throws<ArgumentException>(() => assertions.Predicate(image, GpuAssertionPredicate.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses, mismatched));
        Upload(mismatched,new float[]{7},PixelComponentType.Float);
        result=assertions.Predicate(image, GpuAssertionPredicate.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_2,mismatched,mappedSources:true);
        result.AssertEqual();Assert.Equal(195u,result.MatchingTexels);
        Assert.Equal(0,result.FirstWitnessIndex);
        var tail=assertions.Predicate(image, GpuAssertionPredicate.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_3);
        Assert.Equal(194,tail.FirstWitnessIndex);
        Assert.Throws<InvalidOperationException>(()=>{_=assertions.Predicate(image, GpuAssertionPredicate.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint_2).FirstWitnessIndex;});
        using var flags=Create(TextureDimension.Two,TextureFormat.Rg32f,2,1);
        using var previousFlags=Create(TextureDimension.Two,TextureFormat.Rg32f,2,1);
        Upload(flags,new[]{0f,BitConverter.UInt32BitsToSingle(1),float.NaN,-0f},PixelComponentType.Float);
        Upload(previousFlags,new[]{0f,BitConverter.UInt32BitsToSingle(2),float.NaN,0f},PixelComponentType.Float);
        result=assertions.Predicate(flags, GpuAssertionPredicate.GpuImageAssertionTests_AuthoredPredicatesRetainCorrespondingImagesAndWitnesses_4,previousFlags);
        Assert.Equal(1u,result.Mismatches);
        assertions.CompareFloating(image,values,Enumerable.Repeat(0f,values.Length).ToArray()).AssertEqual();
        float[] wanted=(float[])values.Clone();wanted[^1]=0;
        Assert.Equal(1u,assertions.CompareFloating(image,wanted,new float[wanted.Length]).Mismatches);
    }

    /// <summary>GPU-authored history copies retain complete odd domains and their own snapshots across later source mutation.</summary>
    [Fact]
    public void HistoryProjectionsRetainCompleteIndependentImages()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.R32f,65,3);
        float[] values=Enumerable.Range(0,195).Select(index=>(float)index+.25f).ToArray();
        Upload(image,values,PixelComponentType.Float);
        using var projection=new GpuImageTransform();using var assertions=new GpuImageAssertions();
        using var transformed=projection.Apply(image, GpuImageProjection.GpuImageAssertionTests_HistoryProjectionsRetainCompleteIndependentImages,new(2,0,0,0));
        float[] expected=values.SelectMany((value,index)=>new[]{value+2,(float)(index%65),(float)(index/65),1f}).ToArray();
        assertions.Compare(transformed,expected).AssertEqual();
        Upload(image,new float[195],PixelComponentType.Float);
        assertions.Compare(transformed,expected).AssertEqual();
        using var integer=Create(TextureDimension.Two,TextureFormat.R16ui,65,3);
        uint[] words=Enumerable.Range(0,195).Select(index=>(uint)index).ToArray();words[^1]=65535;
        RHICommandContext.Current.UploadTexture(integer,new(new(),0,0,0,65,3),
            RHICommandContext.Current.GetTextureEncoding(integer,PixelComponentType.UnsignedInt),MemoryMarshal.AsBytes(words.AsSpan()));
        using var converted=projection.Apply(integer, GpuImageProjection.AoMinimumChildProductionTests_ValidateMasks);
        assertions.Compare(converted,words.SelectMany(value=>new[]{(float)value,0f,0f,1f}).ToArray()).AssertEqual();
    }

    /// <summary>Maximum selection and two-dimensional export retain original array, volume and cube layer identities.</summary>
    [Theory]
    [InlineData(TextureDimension.TwoArray)] [InlineData(TextureDimension.Three)] [InlineData(TextureDimension.Cube)]
    public void LocalMaximumSelectionRetainsEveryLayer(TextureDimension dimension)
    {
        EnsureContextValid();int layers=dimension==TextureDimension.Cube?6:3;
        using var image=Create(dimension,TextureFormat.Rgba32f,2,2,layers);
        for(int layer=0;layer<layers;layer++)
            Upload(image,Enumerable.Range(0,4).SelectMany(_=>new[]{0f,(float)layer+1,0f,1f}).ToArray(),PixelComponentType.Float,layer);
        using var assertions=new GpuImageAssertions();
        var maximum=assertions.Brightest(image);
        Assert.Equal(new System.Numerics.Vector3(0,layers,0),maximum.Color);
        Assert.Equal(layers*.7152f,maximum.Luminance);
        Assert.Equal((0,0,0,1f),assertions.Extremum(image,1,false));
        var region=new RHITextureRegion(new(0,dimension==TextureDimension.Three?0:layers-1),0,0,
            dimension==TextureDimension.Three?layers-1:0,2,2);
        using var snapshot=new GpuImageSnapshot(image,region);using var plane=snapshot.ToPlane();
        assertions.Compare(plane,Enumerable.Range(0,4).SelectMany(_=>new[]{0f,(float)layers,0f,1f}).ToArray()).AssertEqual();
    }

    /// <summary>Neither incomplete partial sums nor a missing final reduction may report a successful aggregate.</summary>
    [Theory]
    [InlineData(2, false)] [InlineData(2, true)] [InlineData(3, false)]
    public void StatisticsRequireBothCompleteReductionStages(int suppressed, bool partial)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 65, 3);
        Upload(image, Enumerable.Repeat(2f, 195).ToArray(), PixelComponentType.Float);
        using var assertions = new GpuImageAssertions(); using var native = new NativeDriverCapture();
        var original = Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        int calls = 0;
        native.Add("glDispatchCompute", new Dispatch((x,y,z) =>
        {
            calls++;
            if (calls != suppressed) original(x,y,z);
            else if (partial) original(x-1,y,z);
        }));
        native.Install();
        var failure = Assert.Throws<InvalidOperationException>(() => assertions.Statistics(image));
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
        EnsureContextValid();
        int layers = dimension == TextureDimension.Cube ? 6 : dimension == TextureDimension.Two ? 1 : 3;
        using var image = Create(dimension, TextureFormat.R32f, 4, 4, layers, 2);
        for (int layer = 0; layer < layers; layer++)
            Upload(image, Enumerable.Range(0, 16).Select(index => (float)(index + layer * 16)).ToArray(), PixelComponentType.Float, layer);
        var selection = new RHITextureRegion(new(0, layers - 1), 1, 1, 0, 2, 2);
        float[] expected = [5+(layers-1)*16,6+(layers-1)*16,9+(layers-1)*16,10+(layers-1)*16];
        using var assertions = new GpuImageAssertions();
        assertions.Compare(image, Enumerable.Range(0,16*layers).Select(value=>(float)value).ToArray()).AssertEqual();
        assertions.Compare(image, expected, selection: selection).AssertEqual();
        using var previous = new GpuImageSnapshot(image, selection);
        assertions.CompareHistory(image, previous, selection).AssertEqual();
        RHICommandContext.Current.UploadTexture(image, new(new(0,layers-1),2,2,0,1,1),
            new(PixelChannelLayout.Red,PixelComponentType.Float),MemoryMarshal.AsBytes(new float[] {99}.AsSpan()));
        Assert.Equal(1u, assertions.CompareHistory(image, previous, selection).Mismatches);
        // A selected nonbase mip has its own extent and contents; no base-image resampling is permitted.
        Upload(image, new float[] {.1f,.2f,.3f,.4f}, PixelComponentType.Float, 0, 1, 2, 2);
        assertions.Compare(image, new float[] {.1f,.2f,.3f,.4f}, selection:new(new(1,0),0,0,0,2,2)).AssertEqual();
    }

    /// <summary>Depth is sampled as depth, while channel masks exclude only explicitly unasserted color components.</summary>
    [Fact]
    public void DepthAndChannelSelectionsPreserveTheirAspects()
    {
        EnsureContextValid();
        using var depth = Create(TextureDimension.Two, TextureFormat.DepthComponent32f, 3, 1);
        Upload(depth, new float[] {.1f,.5f,.9f}, PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        assertions.Compare(depth, new float[] {.1f,.5f,.9f}).AssertEqual();
        using var rgba = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(rgba, new float[] {1,2,3,4}, PixelComponentType.Float);
        assertions.Compare(rgba, new float[] {1,99,3,99}, channelMask:5).AssertEqual();
        Assert.Equal(2u, assertions.Compare(rgba, new float[] {1,99,3,99}).Mismatches);
        Assert.Equal(2u, assertions.CountFlags(rgba, 0x00800000, 15).FlagMatches);
        Assert.Equal(1u, assertions.Compare(rgba, new float[] {1.1f,2.1f,3.1f,4},
            tolerance: .15f, channelMask: 7, vectorDistance: true).Mismatches);
        assertions.Compare(rgba, new float[] {1.1f,2.1f,3.1f,4},
            tolerance: .18f, channelMask: 7, vectorDistance: true).AssertEqual();
        Upload(rgba, new float[] {0,2,0,4}, PixelComponentType.Float);
        using var partial = new GpuImageSnapshot(rgba);
        Upload(rgba, new float[] {99,2,99,4}, PixelComponentType.Float);
        assertions.CompareHistory(rgba, partial, retainNonzero: true).AssertEqual();
        Assert.Equal(2u, assertions.CompareHistory(rgba, partial).Mismatches);
        Upload(rgba, new float[] {0,3,0,4}, PixelComponentType.Float);
        Assert.Equal(1u, assertions.CompareHistory(rgba, partial, retainNonzero: true).Mismatches);
        Upload(rgba, new float[] {BitConverter.UInt32BitsToSingle(0x80000000),2,0,4}, PixelComponentType.Float);
        assertions.CompareHistory(rgba, partial).AssertEqual();
        Assert.Equal(1u, assertions.CompareHistory(rgba, partial, bitwise: true).Mismatches);
        Assert.Equal(1u,assertions.Compare(rgba,[0,2,0,4],bitwise:true).Mismatches);
        using var encoded=Create(TextureDimension.Two,TextureFormat.Srgb8Alpha8,1,1);
        Upload(encoded,new float[] {.25f,.5f,.75f,1},PixelComponentType.Float);
        assertions.Compare(encoded,new float[] {64f/255,128f/255,191f/255,1},tolerance:1e-7).AssertEqual();
        using var packed=Create(TextureDimension.Two,TextureFormat.Depth24Stencil8,2,1);
        uint[] depthStencil=[0x000000ff,0xffffff12];
        RHICommandContext.Current.UploadTexture(packed,new(new(0,0),0,0,0,2,1),
            new(PixelChannelLayout.DepthStencil,PixelComponentType.UnsignedInt248),MemoryMarshal.AsBytes(depthStencil.AsSpan()));
        assertions.Compare(packed,new float[] {0,1}).AssertEqual();
    }

    /// <summary>Layer construction retains authored nearest resampling, exact copied values and zero expansion.</summary>
    [Fact]
    public void LayerCopiesAndConversionsPreserveOriginalContracts()
    {
        EnsureContextValid();
        using var color = Create(TextureDimension.Two, TextureFormat.Rgba32f, 3, 2);
        float[] authored = Enumerable.Range(0,24).Select(index => index*.125f).ToArray();
        Upload(color, authored, PixelComponentType.Float);
        using var scalar = Create(TextureDimension.Two, TextureFormat.R32f, 2, 1);
        Upload(scalar, new float[] {.25f,.75f}, PixelComponentType.Float);
        using var layered = LayeredTestTexture.Create(color, scalar, null);
        float[] expected = new float[3*2*3*4]; Array.Copy(authored, expected, authored.Length);
        for (int y=0;y<2;y++)for(int x=0;x<3;x++) expected[(6+y*3+x)*4] = x*2/3==0 ? .25f : .75f;
        using var assertions = new GpuImageAssertions();
        assertions.Compare(layered, expected).AssertEqual();
        using var terms = Create(TextureDimension.TwoArray, TextureFormat.Rgba32f, 2, 1, 3);
        Upload(terms, new float[] {1e20f,1e20f,1e20f,1e20f,1,1,1,1}, PixelComponentType.Float, 0);
        Upload(terms, new float[] {-1e20f,-1e20f,-1e20f,-1e20f,2,2,2,2}, PixelComponentType.Float, 1);
        Upload(terms, new float[] {3,3,3,3,3,3,3,3}, PixelComponentType.Float, 2);
        using var sum = Create(TextureDimension.Two, TextureFormat.Rgba32f, 2, 1);
        Upload(sum, new float[] {0,0,0,0,6,6,6,6}, PixelComponentType.Float);
        assertions.Compare(sum, new float[8], additiveLayers: terms).AssertEqual();
        Assert.Equal(8u, assertions.Compare(sum, Enumerable.Repeat(1f,8).ToArray(), additiveLayers: terms).Mismatches);
    }
    #endregion

    #region Execution failures
    /// <summary>Incomplete native dispatch cannot turn zeroed counters into a passing assertion.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SuppressedNativeDispatchFailsTheCompletionSentinel(bool partial)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 129, 1);
        Upload(image, Enumerable.Range(0,129).Select(value=>(float)value).ToArray(), PixelComponentType.Float);
        using var assertions = new GpuImageAssertions();
        using var native = new NativeDriverCapture();
        int calls=0;
        var original=Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        native.Add("glDispatchCompute",new Dispatch((x,y,z)=>{calls++;if(partial)original(x-1,y,z);})); native.Install();
        var error=Assert.Throws<InvalidOperationException>(() => assertions.Inspect(image));
        if(partial)Assert.Contains("texels=128/129",error.Message);
        Assert.Equal(1,calls); native.RequireSuccessfulObservation();
    }

    /// <summary>The independent histogram-validation dispatch must complete even when the first rank pass succeeds.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RankValidationRequiresItsOwnCompleteDispatch(bool partial)
    {
        EnsureContextValid();
        const int side=16,ranks=side*side;
        float[] values=Enumerable.Range(0,ranks*2).Select(index=>((index+index/ranks)%ranks+.5f)/ranks).ToArray();
        using var image=Create(TextureDimension.Two,TextureFormat.R32f,side,side*2);
        Upload(image,values,PixelComponentType.Float);
        using var assertions=new GpuImageAssertions();using var native=new NativeDriverCapture();
        var original=Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        int calls=0;
        native.Add("glDispatchCompute",new Dispatch((x,y,z)=>{calls++;if(calls==1)original(x,y,z);else if(partial)original(x-1,y,z);}));
        native.Install();
        var failure=Assert.Throws<InvalidOperationException>(()=>assertions.CheckRanks(image,side,2));
        Assert.Contains("GPU rank validation did not complete",failure.Message);
        if(partial)Assert.Contains("ranks=448/512",failure.Message);
        Assert.Equal(2,calls);native.RequireSuccessfulObservation();
    }

    /// <summary>Partial compilation failure releases the original allocation and never publishes an executable.</summary>
    [Fact]
    public void MalformedKernelRetiresPartialConstruction()
    {
        EnsureContextValid();
        bool createdService=RHIResourceManagerSystem.CurrentManager is null;
        var service=RHIResourceManagerSystem.EnsureCurrentContext();
        try
        {
        int partial=0;
        Assert.Throws<IOException>(()=>new NativeComputeProgram("unused",shader=>{partial=shader;throw new IOException("injected construction fault");}));
        Assert.NotEqual(0,partial);Assert.False(GL.IsShader(partial));
        Assert.Throws<InvalidOperationException>(() => new NativeComputeProgram("#version 450\nnot a shader"));
        // A fresh valid owner remains independently usable after the failed construction.
        using var valid = new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        Assert.True(GL.IsProgram(valid.Id));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally {if(createdService){RHIResourceManagerSystem.Shutdown();service.Dispose();}}
    }

    #endregion

    #region Selector restoration
    /// <summary>Unbound, base and ranged SSBO slots, generic binding, program, images and pack layout survive checks.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void HostileSelectorsRemainUnchanged(bool failedDispatch)
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.Two, TextureFormat.R32f, 2, 1);
        Upload(image,new float[] {1,2},PixelComponentType.Float);
        using var arraySentinel=Create(TextureDimension.TwoArray,TextureFormat.R32f,1,1);
        using var sentinel = new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        int buffer=GL.GenBuffer();
        try
        {
            GL.GetInteger(GetPName.ShaderStorageBufferOffsetAlignment,out int alignment);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer,buffer); GL.BufferData(BufferTarget.ShaderStorageBuffer,alignment*4,IntPtr.Zero,BufferUsageHint.DynamicDraw);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,0,buffer);
            GL.BindBufferRange(BufferRangeTarget.ShaderStorageBuffer,1,buffer,(IntPtr)alignment,alignment);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,2,0); GL.BindBuffer(BufferTarget.ShaderStorageBuffer,buffer);
            GL.BindBuffer(BufferTarget.PixelPackBuffer,buffer); GL.PixelStore(PixelStoreParameter.PackRowLength,7); GL.PixelStore(PixelStoreParameter.PackSkipPixels,1);
            GL.UseProgram(sentinel.Id); GL.ActiveTexture(TextureUnit.Texture5);
            GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2DArray,arraySentinel.TextureId);GL.ActiveTexture(TextureUnit.Texture5);
            GL.ActiveTexture(TextureUnit.Texture3);GL.BindTexture(TextureTarget.Texture2DArray,arraySentinel.TextureId);GL.ActiveTexture(TextureUnit.Texture4);GL.BindTexture(TextureTarget.Texture2DArray,arraySentinel.TextureId);GL.ActiveTexture(TextureUnit.Texture5);
            GL.BindImageTexture(0,image.TextureId,0,false,0,TextureAccess.ReadOnly,SizedInternalFormat.R32f);
            int sampler=GL.GenSampler();GL.BindSampler(0,sampler);GL.BindSampler(3,sampler);GL.BindSampler(4,sampler);
            try
            {
            using var native=failedDispatch?new NativeDriverCapture():null;
            if(native is not null){native.Add("glDispatchCompute",new Dispatch((_,_,_)=>{}));native.Install();}
            using var assertions=new GpuImageAssertions();
            if(failedDispatch)Assert.Throws<InvalidOperationException>(()=>assertions.Compare(image,new float[] {1,2}));
            else assertions.Compare(image,new float[] {1,2}).AssertEqual();
            Assert.Equal(sentinel.Id,GL.GetInteger(GetPName.CurrentProgram)); Assert.Equal((int)TextureUnit.Texture5,GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(buffer,GL.GetInteger(GetPName.ShaderStorageBufferBinding)); Assert.Equal(buffer,GL.GetInteger(GetPName.PixelPackBufferBinding));
            Assert.Equal(7,GL.GetInteger(GetPName.PackRowLength)); Assert.Equal(1,GL.GetInteger(GetPName.PackSkipPixels));
            GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding,0,out int first); Assert.Equal(buffer,first);
            GL.GetInteger64(GetIndexedPName.ShaderStorageBufferStart,1,out long offset); Assert.Equal(alignment,offset);
            GL.GetInteger64(GetIndexedPName.ShaderStorageBufferSize,1,out long size); Assert.Equal(alignment,size);
            GL.GetInteger(GetIndexedPName.ShaderStorageBufferBinding,2,out int empty); Assert.Equal(0,empty);
            GL.GetInteger((GetIndexedPName)All.ImageBindingName,0,out int imageBinding);Assert.Equal(image.TextureId,imageBinding);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess,0,out int access);Assert.Equal((int)TextureAccess.ReadOnly,access);
            GL.GetInteger((GetIndexedPName)All.SamplerBinding,0,out int samplerBinding);Assert.Equal(sampler,samplerBinding);
            GL.ActiveTexture(TextureUnit.Texture0);Assert.Equal(arraySentinel.TextureId,GL.GetInteger(GetPName.TextureBinding2DArray));GL.ActiveTexture(TextureUnit.Texture5);
            GL.ActiveTexture(TextureUnit.Texture3);Assert.Equal(arraySentinel.TextureId,GL.GetInteger(GetPName.TextureBinding2DArray));GL.ActiveTexture(TextureUnit.Texture5);
            GL.GetInteger((GetIndexedPName)All.SamplerBinding,3,out int fourthSampler);Assert.Equal(sampler,fourthSampler);
            GL.ActiveTexture(TextureUnit.Texture4);Assert.Equal(arraySentinel.TextureId,GL.GetInteger(GetPName.TextureBinding2DArray));GL.ActiveTexture(TextureUnit.Texture5);
            GL.GetInteger((GetIndexedPName)All.SamplerBinding,4,out int fifthSampler);Assert.Equal(sampler,fifthSampler);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
            }
            finally {GL.BindSampler(0,0);GL.BindSampler(3,0);GL.BindSampler(4,0);GL.DeleteSampler(sampler);GL.BindImageTexture(0,0,0,false,0,TextureAccess.ReadOnly,SizedInternalFormat.R32f);}
        }
        finally
        {
            GL.UseProgram(0);GL.ActiveTexture(TextureUnit.Texture4);GL.BindTexture(TextureTarget.Texture2DArray,0);GL.ActiveTexture(TextureUnit.Texture3);GL.BindTexture(TextureTarget.Texture2DArray,0);GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2DArray,0);GL.BindBuffer(BufferTarget.PixelPackBuffer,0);
            GL.PixelStore(PixelStoreParameter.PackRowLength,0); GL.PixelStore(PixelStoreParameter.PackSkipPixels,0);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,0,0);GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,1,0);GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer,2,0);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer,0);GL.DeleteBuffer(buffer);
        }
    }

    #endregion

    #region Context ownership
    /// <summary>A foreign physical namespace cannot use or retire a live checker, and returning restores its use.</summary>
    [Fact]
    public unsafe void ForeignContextCannotAccessOrRetireAnOwner()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.R32f,1,1);Upload(image,new float[] {1},PixelComponentType.Float);
        using var assertions=new GpuImageAssertions();assertions.Inspect(image).AssertFiniteRange();
        GLFW.DefaultWindowHints();GLFW.WindowHint(WindowHintInt.ContextVersionMajor,4);GLFW.WindowHint(WindowHintInt.ContextVersionMinor,5);
        GLFW.WindowHint(WindowHintBool.Visible,false);
        Window* window=GLFW.CreateWindow(16,16,"Assertion context qualification",null,null);
        Assert.True(window!=null);object owner=new();
        try
        {
            GLFW.MakeContextCurrent(window);RenderContextRegistry.RegisterCurrent(owner,static _=>true);
            int foreign=GL.CreateProgram();
            try
            {
                Assert.Throws<InvalidOperationException>(()=>assertions.Inspect(image));
                Assert.Throws<InvalidOperationException>(assertions.Dispose);
                Assert.True(GL.IsProgram(foreign));
            }
            finally {GL.DeleteProgram(foreign);}
        }
        finally {RenderContextRegistry.Retire(owner);GLFW.DestroyWindow(window);fixture.MakeCurrent();}
        assertions.Inspect(image).AssertFiniteRange();
    }

    /// <summary>A constructor interruption defers its partial shader to the original service, never deleting foreign names.</summary>
    [Fact]
    public unsafe void InterruptedConstructionRetiresOnReturnToOrigin()
    {
        EnsureContextValid();
        bool createdService=RHIResourceManagerSystem.CurrentManager is null;
        var service=RHIResourceManagerSystem.EnsureCurrentContext();
        GLFW.DefaultWindowHints();GLFW.WindowHint(WindowHintInt.ContextVersionMajor,4);GLFW.WindowHint(WindowHintInt.ContextVersionMinor,5);
        GLFW.WindowHint(WindowHintBool.Visible,false);
        Window* window=GLFW.CreateWindow(16,16,"Partial construction qualification",null,null);
        Assert.True(window!=null);object foreignOwner=new();int foreignShader=0,partial=0;
        try
        {
            GLFW.MakeContextCurrent(window);RenderContextRegistry.RegisterCurrent(foreignOwner,static _=>true);
            foreignShader=GL.CreateShader(ShaderType.ComputeShader);fixture.MakeCurrent();
            var expected=new IOException("construction interrupted");
            Assert.Same(expected,Assert.Throws<IOException>(()=>new NativeComputeProgram("unused",shader=>
            {
                partial=shader;GLFW.MakeContextCurrent(window);throw expected;
            })));
            Assert.True(GL.IsShader(foreignShader));
            fixture.MakeCurrent();Assert.True(GL.IsShader(partial));
            service.OnRenderFrame(0,RHIResourceManager.Stage);Assert.False(GL.IsShader(partial));
            GLFW.MakeContextCurrent(window);Assert.True(GL.IsShader(foreignShader));
        }
        finally
        {
            GLFW.MakeContextCurrent(window);if(foreignShader!=0)GL.DeleteShader(foreignShader);
            RenderContextRegistry.Retire(foreignOwner);GLFW.DestroyWindow(window);fixture.MakeCurrent();
            if(createdService){RHIResourceManagerSystem.Shutdown();service.Dispose();}
        }
    }

    /// <summary>Retiring a real native namespace cannot delete recycled program names in the surviving context.</summary>
    [Fact]
    public unsafe void RetiredContextDropsNativeNamesWithoutForeignDeletion()
    {
        EnsureContextValid();
        GLFW.DefaultWindowHints();GLFW.WindowHint(WindowHintInt.ContextVersionMajor,4);GLFW.WindowHint(WindowHintInt.ContextVersionMinor,5);
        GLFW.WindowHint(WindowHintBool.Visible,false);
        Window* window=GLFW.CreateWindow(16,16,"Assertion retirement qualification",null,null);
        Assert.True(window!=null);object owner=new();NativeComputeProgram? retired=null;
        try
        {
            GLFW.MakeContextCurrent(window);RenderContextRegistry.RegisterCurrent(owner,static _=>true);
            retired=new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        }
        finally {RenderContextRegistry.Retire(owner);GLFW.DestroyWindow(window);fixture.MakeCurrent();}
        using var surviving=new NativeComputeProgram("#version 450\nlayout(local_size_x=1)in;void main(){}");
        retired!.Dispose();Assert.True(GL.IsProgram(surviving.Id));retired.Dispose();
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
}
