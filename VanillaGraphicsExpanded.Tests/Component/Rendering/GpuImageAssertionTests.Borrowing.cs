using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Separates synchronous shared-storage interpretation from retained temporal history.</summary>
public sealed partial class GpuImageAssertionTests
{
    #region Public API
    #region Authored predicate domains
    /// <summary>Rectangle comparisons preserve exterior sentinels and detect a changed interior texel.</summary>
    [Fact]
    public void AuthoredMaterialRectangleSeparatesExteriorAndInterior()
    {
        // Arrange: author matching interior/exterior values, then nominate one interior and one excluded sentinel.
        EnsureContextValid();
        ImageAssertions.Programs.Prepare("predicate-material");
        using var actual = Create(TextureDimension.Two, TextureFormat.Rgba32f, 64, 64);
        using var reference = Create(TextureDimension.Two, TextureFormat.Rgba32f, 64, 64);
        float[] values = new float[64 * 64 * 4];
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            bool inside = x >= 16 && x < 48 && y >= 16 && y < 48;
            int offset = (y * 64 + x) * 4;
            values[offset] = values[offset + 1] = values[offset + 3] = inside ? 1 : .5f;
            values[offset + 2] = 1;
        }
        Upload(actual, values, PixelComponentType.Float);
        Upload(reference, Enumerable.Repeat(1f, values.Length).ToArray(), PixelComponentType.Float);
        var policy = MaterialPredicates.MaterialAtlasBakeSubmissionTests_IndependentAtlasEntriesPreserveStateAndRectangles_3;
        // Act: run the predicate for the valid rectangle, then after each targeted mutation.
        ImageAssertions.Predicates.Predicate(actual, policy, reference).AssertEqual();
        values[(20 * 64 + 19) * 4] = 2;
        Upload(actual, values, PixelComponentType.Float);
        var changed = ImageAssertions.Predicates.Predicate(actual, policy, reference);
        Assert.Equal(1u, changed.Mismatches);
        Assert.Contains("first=(19,20,0)", changed.ToString());
        values[(20 * 64 + 19) * 4] = 1;
        values[16 * 64 * 4] = 2;
        Upload(actual, values, PixelComponentType.Float);
        var exterior = ImageAssertions.Predicates.Predicate(actual, policy, reference);
        // Assert: each readback reports only its changed coordinate, including the excluded exterior sentinel.
        Assert.Equal(1u, exterior.Mismatches);
        Assert.Contains("first=(0,16,0)", exterior.ToString());
    }

    /// <summary>Conditional cloud witnesses exclude boundaries and report only a deliberately changed interior sample.</summary>
    [Fact]
    public void AuthoredCloudSelectionPreservesExcludedBoundaryAndInteriorWitnesses()
    {
        // Arrange: populate fresh interior witnesses and an intentionally bad excluded boundary sample.
        EnsureContextValid();
        ImageAssertions.Programs.Prepare("predicate-cloud");
        using var diagnostics = Create(TextureDimension.Two, TextureFormat.Rgba32f, 8, 8);
        using var age = Create(TextureDimension.Two, TextureFormat.Rgba32f, 8, 8);
        using var expected = Create(TextureDimension.Two, TextureFormat.Rgba32f, 8, 8);
        float[] samples = new float[64 * 4], ages = new float[64 * 4], reference = new float[64 * 4];
        for (int pixel = 0; pixel < 64; pixel++)
        {
            samples[pixel * 4 + 1] = 2;
            ages[pixel * 4] = reference[pixel * 4] = 2;
        }
        ages[0] = 99;
        Upload(diagnostics, samples, PixelComponentType.Float);
        Upload(age, ages, PixelComponentType.Float);
        Upload(expected, reference, PixelComponentType.Float);
        var policy = CloudPredicates.CloudTemporalFreshnessTests_ResampledFreshnessUsesPhysicalWeightsAndExpiresWithoutObservations_2;
        // Act: compare before and after changing one sample that survives the cloud policy selection.
        var valid = ImageAssertions.Predicates.Predicate(diagnostics, policy, age, expected);
        valid.AssertEqual();
        Assert.Equal(36u, valid.MatchingTexels);
        ages[(3 * 8 + 2) * 4] = 99;
        Upload(age, ages, PixelComponentType.Float);
        var changed = ImageAssertions.Predicates.Predicate(diagnostics, policy, age, expected);
        // Assert: the altered interior point is the sole mismatch and remains visible in diagnostics.
        Assert.Equal(1u, changed.Mismatches);
        Assert.Contains("first=(2,3,0)", changed.ToString());
    }

    /// <summary>Minimum-child checking retains mapped source dimensions and detects one deliberately wrong mask.</summary>
    [Fact]
    public void AuthoredMinimumChildMaskUsesParentAndChildSamples()
    {
        // Arrange: map a 2x2 parent domain onto 4x4 child samples and author a valid minimum mask.
        EnsureContextValid();
        ImageAssertions.Programs.Prepare("predicate-ao");
        using var masks = Create(TextureDimension.Two, TextureFormat.R32f, 2, 2);
        using var parents = Create(TextureDimension.Two, TextureFormat.R32f, 2, 2);
        using var children = Create(TextureDimension.Two, TextureFormat.R32f, 4, 4);
        Upload(masks, [27f, 27f, 27f, 27f], PixelComponentType.Float);
        Upload(parents, [1f, 1f, 1f, 1f], PixelComponentType.Float);
        Upload(children, Enumerable.Repeat(1f, 16).ToArray(), PixelComponentType.Float);
        var policy = AoPredicates.AoMinimumChildProductionTests_ValidateMasks_2;
        // Act: evaluate the mapped source set, then change one mask lane and read it again.
        ImageAssertions.Predicates.Predicate(masks, policy, parents, children,
            parameters: new(4, 4, 2, 2), mappedSources: true).AssertEqual();
        Upload(masks, [27f, 27f, 26f, 27f], PixelComponentType.Float);
        var changed = ImageAssertions.Predicates.Predicate(masks, policy, parents, children,
            parameters: new(4, 4, 2, 2), mappedSources: true);
        // Assert: the second result identifies the one invalid mask coordinate.
        Assert.Equal(1u, changed.Mismatches);
        Assert.Contains("first=(0,1,0)", changed.ToString());
    }

    #endregion

    #region Context and storage borrowing
    /// <summary>Explicit registration restoration replaces retired preparations without adopting a foreign epoch.</summary>
    [Fact]
    public void ObservationPreparationFollowsExplicitFixtureRegistrationRestoration()
    {
        // Arrange: retain the current observation owner so retirement cannot be hidden by later fixture setup.
        EnsureContextValid();
        var previous = ImageAssertions;
        var original = VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Current();
        // Act: retire fixture registration and withdraw its current observation owner.
        VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Retire(fixture);
        try
        {
            // Assert: the old preparation is retired and the unregistered fixture cannot provide observations.
            Assert.True(previous.Programs.IsRetired);
            Assert.Throws<InvalidOperationException>(() => fixture.Observations);
        }
        finally { fixture.RestoreContextRegistration(); }
        // Act: request observations after restoring registration under a new context epoch.
        EnsureContextValid();
        // Assert: old preparations remain retired while the restored fixture can observe a new image.
        Assert.NotEqual(original, VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.Current());
        Assert.NotSame(previous, ImageAssertions);
        Assert.Throws<ObjectDisposedException>(() => previous.Programs.RequireContext());
        using var image = Create(TextureDimension.Two, TextureFormat.Rgba32f, 1, 1);
        Upload(image, [2, 3, 4, 5], PixelComponentType.Float);
        ImageAssertions.Comparisons.CompareVector(image, new(2, 3, 4, 5)).AssertEqual();
    }

    /// <summary>Immediate observations preserve raw stored channels despite hostile source sampling state.</summary>
    [Theory]
    [InlineData(TextureDimension.Two)] [InlineData(TextureDimension.TwoArray)]
    public void ImmediateObservationSharesStorageWithoutSourceSelectorMutation(TextureDimension dimension)
    {
        // Arrange: set hostile source sampling state and capture independent history before mutation.
        EnsureContextValid();
        using var image = Create(dimension, TextureFormat.Rgba32f, 2, 1);
        Upload(image, [2, 3, 4, 5, 2, 3, 4, 5], PixelComponentType.Float);
        GL.TextureParameter(image.TextureId, TextureParameterName.TextureSwizzleR, 0);
        using var history = new GpuImageSnapshot(image);
        using (var input = new GpuObservationImage(image))
        {
            // Act: read shared storage before and after changing source contents, then compare retained history.
            Assert.False(input.CopiesStorage);
            Assert.NotEqual(image.TextureId, input.Texture.TextureId);
            ImageAssertions.Comparisons.Compare(image, [2, 3, 4, 5, 2, 3, 4, 5]).AssertEqual();
            // A shared interpretation sees the original allocation's later content, while explicitly
            // retained history remains independent. No checker execution overlaps this mutation.
            Upload(image, [7, 3, 4, 5, 7, 3, 4, 5], PixelComponentType.Float);
            Assert.Equal(7f, GpuLocalRead.NativeColorTuple(input.Texture, 0, 0)[0]);
            Assert.True(ImageAssertions.Comparisons.CompareHistory(image, history).Mismatches > 0);
        }
        Assert.True(image.IsValid);
        // Assert: observation preserved native selector state and left the source image usable.
        GL.GetTextureParameter(image.TextureId, GetTextureParameter.TextureSwizzleR, out int swizzle);
        Assert.Equal(0, swizzle);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Spatial crops retain their exact conversion, while full selected mips/layers need no image copy.</summary>
    [Fact]
    public void ObservationInterpretationsRetainSelectedDomainsAndRejectInvalidRanges()
    {
        // Arrange: author distinguishable values in two layers of a nonbase mip.
        EnsureContextValid();
        using var image = Create(TextureDimension.TwoArray, TextureFormat.R32f, 4, 4, 3, 2);
        Upload(image, [5, 5, 5, 5], PixelComponentType.Float, layer: 1, mip: 1, width: 2, height: 2);
        Upload(image, [6, 6, 6, 6], PixelComponentType.Float, layer: 2, mip: 1, width: 2, height: 2);
        using (var mip = new GpuObservationImage(image, new(new(1, 1), 0, 0, 0, 2, 2, 2)))
        {
            // Act: observe the complete mip/layer range without copying, reading both selected endpoints directly.
            Assert.False(mip.CopiesStorage);
            Assert.Equal((2, 2, 2), (mip.Texture.Width, mip.Texture.Height, mip.Texture.Depth));
            Assert.Equal(5f, GpuLocalRead.NativeColorTuple(mip.Texture, 0, 0, 0)[0]);
            Assert.Equal(6f, GpuLocalRead.NativeColorTuple(mip.Texture, 1, 1, 1)[0]);
        }
        using (var crop = new GpuObservationImage(image, new(new(0, 1), 1, 1, 0, 2, 2)))
        {
            // Assert: the spatial crop materializes only requested dimensions; invalid ranges are rejected.
            Assert.True(crop.CopiesStorage);
            Assert.Equal((2, 2, 1), (crop.Texture.Width, crop.Texture.Height, crop.Texture.Depth));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuObservationImage(image, new(new(0, -1), 0, 0, 0, 4, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuObservationImage(image, new(new(2), 0, 0, 0, 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuObservationImage(image, new(new(0), 0, 0, 0, 4, 4, 4)));
    }
    #endregion
    #endregion
}
