using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies native format support and demand-driven capability cache behavior.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
        fixture.MakeCurrent();
        var support = GpuSupport.GetInternalFormatCapabilities(target, format);
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
        fixture.MakeCurrent();
        var texture = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture2D, SizedInternalFormat.Rgba8);
        var renderbuffer = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Renderbuffer, SizedInternalFormat.Rgba8);
        var floating = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture2D, SizedInternalFormat.Rgba16f);
        Assert.NotSame(texture, renderbuffer);
        Assert.NotSame(texture, floating);
        GL.GetInteger((GetPName)(-1));
        Assert.Same(texture, GpuSupport.GetInternalFormatCapabilities(ImageTarget.Texture2D, SizedInternalFormat.Rgba8));
        Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Forced support initialization clears cached format values without mutating retained data.</summary>
    [Fact]
    public void ForcedInitializationRequeriesFormats()
    {
        fixture.MakeCurrent();
        var retained = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Renderbuffer, SizedInternalFormat.Rgba8);
        var samples = retained.SampleCounts.ToArray();
        GpuSupport.Initialize(force: true);
        var current = GpuSupport.GetInternalFormatCapabilities(ImageTarget.Renderbuffer, SizedInternalFormat.Rgba8);
        Assert.NotSame(retained, current);
        Assert.Equal(samples, current.SampleCounts.ToArray());
        Assert.Equal(samples, retained.SampleCounts.ToArray());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An invalid native query must fail on every attempt instead of caching unsupported data.</summary>
    [Fact]
    public void FailedQueriesAreNotPublished()
    {
        fixture.MakeCurrent();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.Throws<InvalidOperationException>(() => GpuSupport.GetInternalFormatCapabilities((ImageTarget)(-1), SizedInternalFormat.Rgba8));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
    }
    #endregion
}
