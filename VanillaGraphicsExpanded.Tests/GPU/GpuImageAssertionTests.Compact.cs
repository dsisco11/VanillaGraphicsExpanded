using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies compact references and sparse completion independently of unused image extent.</summary>
public sealed partial class GpuImageAssertionTests
{
    #region Public API
    /// <summary>Point predicates borrow every supported original source dimension and retain duplicates and last-layer negatives.</summary>
    [Theory]
    [InlineData(TextureDimension.Two)]
    [InlineData(TextureDimension.TwoArray)]
    [InlineData(TextureDimension.Three)]
    public void SparsePointsBorrowOriginalSources(TextureDimension dimension)
    {
        EnsureContextValid();
        int depth = dimension == TextureDimension.Two ? 1 : 2;
        using var image = Create(dimension, TextureFormat.R32f, 65, 3, depth);
        for (int layer = 0; layer < depth; layer++) Upload(image, Enumerable.Repeat(1f, 195).ToArray(), PixelComponentType.Float, layer);
        var points = new (int X, int Y, int Layer)[] { (0,0,0), (64,2,depth-1), (64,2,depth-1) };
        long bytes = ImageAssertions.Predicates.UploadedCoordinateBytes;
        long copied = ImageAssertions.Predicates.CopiedPointTexels;
        var success = ImageAssertions.Predicates.PredicateAt(image, points,
            ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint);
        success.AssertEqual();
        Assert.Equal(3u, success.VisitedTexels); Assert.Equal(3u, success.MatchingTexels);
        Assert.Equal(36, ImageAssertions.Predicates.UploadedCoordinateBytes - bytes);
        Assert.Equal(copied,ImageAssertions.Predicates.CopiedPointTexels);
        RHICommandContext.Current.UploadTexture(image, new(new(0,depth-1),64,2,0,1,1),
            new(PixelChannelLayout.Red,PixelComponentType.Float), MemoryMarshal.AsBytes(new float[] {3}.AsSpan()));
        var failure = ImageAssertions.Predicates.PredicateAt(image, points,
            ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint);
        Assert.Equal(2u, failure.Mismatches); Assert.Contains($"(64,2,{depth-1})", failure.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageAssertions.Predicates.PredicateAt(image, [(65,0,0)],
            ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint));
    }

    /// <summary>Sparse interpretation retains stored sRGB bytes and default depth without changing the subject aspect.</summary>
    [Fact]
    public void SparseInterpretationsCopyOnlyNominatedTexels()
    {
        EnsureContextValid();
        using var srgb=Create(TextureDimension.Two,TextureFormat.Srgb8Alpha8,256,3);
        byte[] bytes=Enumerable.Range(0,256*3).SelectMany(index=>new byte[]{(byte)(index%256),0,0,255}).ToArray();
        RHICommandContext.Current.UploadTexture(srgb,new(new(0),0,0,0,256,3),
            new(PixelChannelLayout.Rgba,PixelComponentType.UnsignedByte),bytes);
        var points=Enumerable.Range(0,256).Select(x=>(x,2,0)).ToArray();
        long copied=ImageAssertions.Predicates.CopiedPointTexels;
        var proof=ImageAssertions.Predicates.PredicateAt(srgb,points,ObservationPredicates.StoredRedByte);
        proof.AssertEqual();Assert.Equal(256u,proof.MatchingTexels);
        Assert.Equal(256,ImageAssertions.Predicates.CopiedPointTexels-copied);
        using var depth=Create(TextureDimension.Two,TextureFormat.Depth24Stencil8,2,1);
        uint[] packed=[0xffffff07,0xffffff09];
        RHICommandContext.Current.UploadTexture(depth,new(new(0),0,0,0,2,1),
            new(PixelChannelLayout.DepthStencil,PixelComponentType.UnsignedInt248),MemoryMarshal.AsBytes(packed.AsSpan()));
        GL.TextureParameter(depth.TextureId,TextureParameterName.DepthStencilTextureMode,(int)PixelFormat.StencilIndex);
        var depthProof=ImageAssertions.Predicates.PredicateAt(depth,[(0,0,0),(1,0,0)],
            ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint);
        depthProof.AssertEqual();
        GL.GetTextureParameter(depth.TextureId,(GetTextureParameter)TextureParameterName.DepthStencilTextureMode,out int aspect);
        Assert.Equal((int)PixelFormat.StencilIndex,aspect);
        Assert.Equal(258,ImageAssertions.Predicates.CopiedPointTexels-copied);
    }

    /// <summary>Region overlaps keep both guarantees and compact vectors retain selected nonbase mips and layers.</summary>
    [Fact]
    public void CompactRegionsAndMipSelectionsRetainTheirDomains()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.TwoArray,TextureFormat.Rgba32f,4,4,2,2);
        Upload(image,Enumerable.Repeat(new[]{1f,2f,3f,4f},4).SelectMany(value=>value).ToArray(),PixelComponentType.Float,1,1,2,2);
        var mip=new RHITextureRegion(new(1,1),0,0,0,2,2);
        var result=ImageAssertions.Comparisons.CompareVector(image,new(1,2,3,4),mip);
        result.AssertEqual();Assert.Equal(16u,result.ComparedComponents);
        Assert.Equal(4u,ImageAssertions.Comparisons.CompareVector(image,new(1,2,99,4),mip,4).Mismatches);
        using var regions=Create(TextureDimension.Two,TextureFormat.Rgba32f,65,3);
        Upload(regions,Enumerable.Repeat(new[]{1f,2f,3f,4f},195).SelectMany(value=>value).ToArray(),PixelComponentType.Float);
        GpuVectorRegion[] expectations=[new(new(new(0),0,0,0,65,3),new(1,2,3,4)),
            new(new(new(0),64,2,0,1,1),new(1,99,3,99),5)];
        var matching=ImageAssertions.Comparisons.CompareRegions(regions,expectations);
        matching.AssertEqual();Assert.Equal(782u,matching.ComparedComponents);
        expectations[1]=expectations[1] with {Expected=new(1,99,99,99)};
        var failure=ImageAssertions.Comparisons.CompareRegions(regions,expectations);
        Assert.Equal(1u,failure.Mismatches);Assert.Contains("(64,2,0), channel=2",failure.ToString());
        // RGB vector distance always retains all three authored components even with a narrower inspection mask.
        ImageAssertions.Comparisons.Compare(regions,
            Enumerable.Repeat(new[]{1f,2f,3f,4f},195).SelectMany(value=>value).ToArray(),
            vectorDistance:true,channelMask:1).AssertEqual();
        Assert.Equal(195u,ImageAssertions.Comparisons.Compare(regions,
            Enumerable.Repeat(new[]{1f,2f,99f,4f},195).SelectMany(value=>value).ToArray(),
            vectorDistance:true,channelMask:1).Mismatches);
    }

    /// <summary>First and last cube faces preserve nominated coordinates without copying enclosing faces.</summary>
    [Fact]
    public void SparseCubePointsRetainFaceIdentity()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.Cube,TextureFormat.R32f,2,2);
        for(int face=0;face<6;face++)Upload(image,[1,1,1,face==5?3:1],PixelComponentType.Float,face);
        long copied=ImageAssertions.Predicates.CopiedPointTexels;
        var proof=ImageAssertions.Predicates.PredicateAt(image,[(0,0,0),(1,1,5)],
            ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint);
        Assert.Equal(1u,proof.Mismatches);Assert.Equal(2u,proof.VisitedTexels);
        Assert.Contains("(1,1,5)",proof.ToString());
        Assert.Equal(2,ImageAssertions.Predicates.CopiedPointTexels-copied);
    }

    /// <summary>Records retain exact selected totals, scalar interval intersections and source-coordinate diagnostics.</summary>
    [Fact]
    public void SparseReferencesRetainCoordinatesAndIntersectingBounds()
    {
        EnsureContextValid();
        using var image = Create(TextureDimension.TwoArray, TextureFormat.Rgba32f, 65, 3, 2);
        for (int layer = 0; layer < 2; layer++) Upload(image, Enumerable.Repeat(new[] {1f,2f,3f,4f},195).SelectMany(value=>value).ToArray(), PixelComponentType.Float,layer);
        var expected = new Dictionary<int,Vector3> { [0]=new(1,2,3), [389]=new(1,2,3) };
        long bytes = ImageAssertions.Comparisons.UploadedExpectationBytes;
        var pass = ImageAssertions.Comparisons.CompareSelected(image,expected,0);
        pass.AssertEqual(); Assert.Equal(2u,pass.VisitedTexels); Assert.Equal(6u,pass.ComparedComponents);
        Assert.Equal(32,ImageAssertions.Comparisons.UploadedExpectationBytes-bytes);
        expected[389]=new(1,2,99);
        var failure=ImageAssertions.Comparisons.CompareSelected(image,expected,0);
        Assert.Equal(1u,failure.Mismatches);Assert.Contains("(64,2,1), channel=2",failure.ToString());
        GpuScalarInterval[] intervals=[new(389,2,2.5,3.5),new(389,2,3,3),new(0,0,1,1)];
        var intersection=ImageAssertions.Comparisons.CompareIntervals(image,intervals);
        intersection.AssertEqual();Assert.Equal(3u,intersection.ComparedComponents);
        intervals[1]=new(389,2,Math.BitIncrement(3d),4);
        Assert.Equal(1u,ImageAssertions.Comparisons.CompareIntervals(image,intervals).Mismatches);
        Assert.Throws<ArgumentOutOfRangeException>(()=>ImageAssertions.Comparisons.CompareSelected(image,new Dictionary<int,Vector3>{[390]=Vector3.Zero},0));
        Assert.Throws<ArgumentException>(()=>ImageAssertions.Comparisons.CompareSelected(image,new Dictionary<int,Vector3>(),0));
    }

    /// <summary>Suppression and truncated sparse dispatch cannot certify nominated tail records.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SparseDispatchRequiresEveryRecord(bool partial)
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.Rgba32f,65,3);
        Upload(image,new float[65*3*4],PixelComponentType.Float);
        var expected=Enumerable.Range(0,65).ToDictionary(index=>index,_=>Vector3.Zero);
        using var native=new NativeDriverCapture();
        var original=Marshal.GetDelegateForFunctionPointer<Dispatch>(native.Original("glDispatchCompute"));
        native.Add("glDispatchCompute",new Dispatch((x,y,z)=>{if(partial)original(x-1,y,z);}));native.Install();
        Assert.Throws<InvalidOperationException>(()=>ImageAssertions.Comparisons.CompareSelected(image,expected,0));
        Assert.Throws<InvalidOperationException>(()=>ImageAssertions.Predicates.PredicateAt(image,
            Enumerable.Range(0,65).Select(x=>(x,0,0)).ToArray(),
            ObservationPredicates.GpuImageAssertionTests_SparsePredicatesRetainEveryNominatedPoint));
        native.RequireSuccessfulObservation();
    }

    /// <summary>Vector/selected-channel references retain exact masks, mip interpretation and independent double bounds.</summary>
    [Fact]
    public void CompactExpectationsPreserveMasksAndReportConstructionCosts()
    {
        EnsureContextValid();
        using var image=Create(TextureDimension.Two,TextureFormat.Rgba32f,65,3);
        Upload(image,Enumerable.Repeat(new[]{1f,2f,3f,4f},195).SelectMany(value=>value).ToArray(),PixelComponentType.Float);
        var comparisons=ImageAssertions.Comparisons;
        long bytes=comparisons.UploadedExpectationBytes;
        comparisons.CompareVector(image,new(1,99,3,99),channelMask:5).AssertEqual();
        Assert.Equal(bytes,comparisons.UploadedExpectationBytes);
        Assert.Equal(390u,comparisons.CompareVector(image,new(1,99,3,99)).Mismatches);
        var watch=Stopwatch.StartNew();
        double[] lower=Enumerable.Repeat(2d,195).ToArray(),upper=Enumerable.Repeat(2d,195).ToArray();
        watch.Stop();var construction=watch.Elapsed;
        watch.Restart();
        var result=comparisons.CompareComponentIntervals(image,lower,upper,2);
        watch.Stop();result.AssertEqual();Assert.Equal(195u,result.ComparedComponents);
        Assert.Equal(195*16,comparisons.UploadedExpectationBytes-bytes);
        upper[^1]=Math.BitDecrement(2d);lower[^1]=1;
        Assert.Equal(1u,comparisons.CompareComponentIntervals(image,lower,upper,2).Mismatches);
        output.WriteLine($"Compact bounds: CPU authored arrays={195*16} bytes, construction={construction.TotalMilliseconds:F3}ms; scalar upload={195*16} bytes; full operation={watch.Elapsed.TotalMilliseconds:F3}ms. Previous padded RGBA double bands={195*64} bytes. No whole-suite inference.");
    }
    #endregion
}
