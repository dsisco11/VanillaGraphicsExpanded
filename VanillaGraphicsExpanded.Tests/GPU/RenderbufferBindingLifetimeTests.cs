using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies renderbuffer cache queries, nested binding scopes, and retirement of bound storage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RenderbufferBindingLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Unknown bindings are resolved and nested scopes restore each preceding owner exactly.</summary>
    [Fact]
    public void UnknownBindingAndNestedScopesRestoreNativeOwners()
    {
        EnsureContextValid();
        using var first = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
        using var second = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
        using var third = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (int)first.ResourceId);
        var cache = StateCache.Current; cache.Invalidate(EPipelineState.RenderbufferBinding);
        Assert.Equal((int)first.ResourceId, cache.GetCurrentRenderbuffer());
        using (second.BindScope())
        {
            Assert.Equal((int)second.ResourceId, cache.GetCurrentRenderbuffer());
            var failure = new InvalidOperationException("nested scope");
            Assert.Same(failure, Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var nested = third.BindScope();
                Assert.Equal((int)third.ResourceId, GL.GetInteger(GetPName.RenderbufferBinding));
                throw failure;
            })));
            Assert.Equal((int)second.ResourceId, GL.GetInteger(GetPName.RenderbufferBinding));
        }
        Assert.Equal((int)first.ResourceId, GL.GetInteger(GetPName.RenderbufferBinding));
        Assert.Equal((int)first.ResourceId, cache.GetCurrentRenderbuffer());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Deleting a bound renderbuffer records native zero and never suppresses the next live binding.</summary>
    [Fact]
    public void BoundRetirementUpdatesOnlyTheAffectedBinding()
    {
        EnsureContextValid();
        using var retired = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
        using var live = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
        var cache = StateCache.Current;
        cache.BindRenderbuffer((int)retired.ResourceId);
        retired.Dispose();
        Assert.Equal(0, cache.GetCurrentRenderbuffer());
        Assert.Equal(0, GL.GetInteger(GetPName.RenderbufferBinding));
        cache.BindRenderbuffer((int)live.ResourceId);
        Assert.Equal((int)live.ResourceId, GL.GetInteger(GetPName.RenderbufferBinding));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A failed unknown-state query never publishes a fabricated binding and a clean retry resolves the real owner.</summary>
    [Fact]
    public void FailedUnknownQueryDoesNotPublishBinding()
    {
        EnsureContextValid();
        using var bound = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 2, 2);
        var cache = StateCache.Current;
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (int)bound.ResourceId);
        cache.Invalidate(EPipelineState.RenderbufferBinding);
        GL.LineWidth(-1); // A deliberate native failure must be reported before any value is admitted as known.
        Assert.Throws<InvalidOperationException>(() => cache.GetCurrentRenderbuffer());
        Assert.Equal((int)bound.ResourceId, cache.GetCurrentRenderbuffer());
        Assert.Equal((int)bound.ResourceId, GL.GetInteger(GetPName.RenderbufferBinding));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
