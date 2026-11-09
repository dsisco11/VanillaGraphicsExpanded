using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies native format support and demand-driven capability cache behavior.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class InternalFormatCapabilitiesTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Each target and format has independent native support and multisample data.</summary>
    [Theory]
    [InlineData(ImageTarget.Texture2D, SizedInternalFormat.Rgba8)]
    [InlineData(ImageTarget.Renderbuffer, SizedInternalFormat.Rgba8)]
    [InlineData(ImageTarget.Texture2DMultisample, SizedInternalFormat.Rgba16f)]
    public void FormatSupportMatchesNative(ImageTarget target, SizedInternalFormat format)
    {
        // Arrange: Query one target/format pair on the current driver and leave other keys independently addressable.
        // Act: Ask the capability cache for the selected target and format.
        // Assert: Reported support and multisample values agree with native format queries.
        fixture.MakeCurrent();
        var support = GpuSupport.GetInternalFormatCapabilities(target == ImageTarget.Renderbuffer ? TextureDimension.Two : OpenGLImageValues.Dimension((TextureTarget)target), OpenGLImageValues.Format((PixelInternalFormat)format), target == ImageTarget.Renderbuffer ? GpuResourceKind.Renderbuffer : GpuResourceKind.Texture);
        GL.GetInternalformat(target, format, InternalFormatParameter.InternalformatSupported, 1, out int supported);
        GL.GetInternalformat(target, format, InternalFormatParameter.FramebufferRenderable, 1, out int renderable);
        Assert.Equal(supported != 0, support.Supported);
        Assert.Equal(renderable, support.FramebufferRenderableSupport);
        if (target != ImageTarget.Texture2D)
        {
            GL.GetInternalformat(target, format, InternalFormatParameter.NumSampleCounts, 1, out int count);
            int[] samples = new int[count];
            if (count > 0) GL.GetInternalformat(target, format, InternalFormatParameter.Samples, count, samples);
            Assert.Equal(samples, support.SampleCounts.ToArray());
        }
        else Assert.Empty(support.SampleCounts);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Cache keys include both dimensions and warm lookups preserve pending native errors.</summary>
    [Fact]
    public void WarmReadsReuseOnlyMatchingTargetAndFormat()
    {
        // Arrange: Seed a native error and warm the cache for one target/format key.
        // Act: Repeat the matching lookup and query a different key.
        // Assert: Warm access preserves the pending error and does not alias distinct keys.
        fixture.MakeCurrent();
        var texture = GpuSupport.GetInternalFormatCapabilities(TextureDimension.Two, TextureFormat.Rgba8);
        var renderbuffer = GpuSupport.GetInternalFormatCapabilities(TextureDimension.Two, TextureFormat.Rgba8, GpuResourceKind.Renderbuffer);
        var floating = GpuSupport.GetInternalFormatCapabilities(TextureDimension.Two, TextureFormat.Rgba16f);
        Assert.NotSame(texture, renderbuffer);
        Assert.NotSame(texture, floating);
        GL.GetInteger((GetPName)(-1));
        Assert.Same(texture, GpuSupport.GetInternalFormatCapabilities(TextureDimension.Two, TextureFormat.Rgba8));
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Forced support initialization clears cached format values without mutating retained data.</summary>
    [Fact]
    public void ForcedInitializationRequeriesFormats()
    {
        // Arrange: Retain a previously published support result before forcing initialization.
        // Act: Force a fresh capability initialization.
        // Assert: Cached format values are re-queried without mutating retained data.
        fixture.MakeCurrent();
        var retained = GpuSupport.GetInternalFormatCapabilities(TextureDimension.Two, TextureFormat.Rgba8, GpuResourceKind.Renderbuffer);
        var samples = retained.SampleCounts.ToArray();
        GpuSupport.Initialize(force: true);
        var current = GpuSupport.GetInternalFormatCapabilities(TextureDimension.Two, TextureFormat.Rgba8, GpuResourceKind.Renderbuffer);
        Assert.NotSame(retained, current);
        Assert.Equal(samples, current.SampleCounts.ToArray());
        Assert.Equal(samples, retained.SampleCounts.ToArray());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An invalid native query must fail on every attempt instead of caching unsupported data.</summary>
    [Fact]
    public void FailedQueriesAreNotPublished()
    {
        // Arrange: Select an invalid native query whose failure can be observed on each attempt.
        // Act: Repeat the failing support query.
        // Assert: Failure is not published as a cached unsupported result.
        fixture.MakeCurrent();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.Throws<InvalidOperationException>(() => GpuSupport.GetInternalFormatCapabilities((TextureDimension)(-1), TextureFormat.Rgba8));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
    }
    #endregion
}
