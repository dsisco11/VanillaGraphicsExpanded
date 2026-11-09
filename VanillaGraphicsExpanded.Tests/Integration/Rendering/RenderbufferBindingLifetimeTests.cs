using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies renderbuffer cache queries, nested binding scopes, and retirement of bound storage.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RenderbufferBindingLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Unknown bindings are resolved and nested scopes restore each preceding owner exactly.</summary>
    [Fact]
    public void UnknownBindingAndNestedScopesRestoreNativeOwners()
    {
        // Arrange: Start with unknown renderbuffer bindings and bind two nested owners.
        // Act: Query and unwind the nested binding scopes.
        // Assert: Each scope restores the exact native owner that preceded it.
        EnsureContextValid();
        using var first = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        using var second = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        using var third = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (int)first.ResourceId);
        var cache = OpenGlStateCache.Current; cache.Invalidate(EPipelineState.RenderbufferBinding);
        Assert.Equal((int)first.ResourceId, cache.GetCurrentRenderbuffer());
        using (OpenGlStateCache.Current.BindRenderbufferScope(second.RenderbufferId))
        {
            Assert.Equal((int)second.ResourceId, cache.GetCurrentRenderbuffer());
            var failure = new InvalidOperationException("nested scope");
            Assert.Same(failure, Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var nested = OpenGlStateCache.Current.BindRenderbufferScope(third.RenderbufferId);
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
        // Arrange: Bind two renderbuffers to independent targets and retain their cached knowledge.
        // Act: Delete the renderbuffer bound to one target.
        // Assert: Only the affected binding becomes zero and the next live bind is not suppressed.
        EnsureContextValid();
        using var retired = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        using var live = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        var cache = OpenGlStateCache.Current;
        OpenGLImageBindings.BindRenderbuffer(cache, (int)retired.ResourceId);
        retired.Dispose();
        Assert.Equal(0, cache.GetCurrentRenderbuffer());
        Assert.Equal(0, GL.GetInteger(GetPName.RenderbufferBinding));
        OpenGLImageBindings.BindRenderbuffer(cache, (int)live.ResourceId);
        Assert.Equal((int)live.ResourceId, GL.GetInteger(GetPName.RenderbufferBinding));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An unrelated pending error does not invalidate a valid binding query or get consumed by cache recovery.</summary>
    [Fact]
    public void UnrelatedNativeErrorSurvivesBindingRecovery()
    {
        // Arrange: Leave an unrelated native error pending while a valid binding query is needed.
        // Act: Recover binding knowledge through the cache.
        // Assert: The query succeeds without consuming the caller's error.
        EnsureContextValid();
        using var bound = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        var cache = OpenGlStateCache.Current;
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (int)bound.ResourceId);
        cache.Invalidate(EPipelineState.RenderbufferBinding);
        GL.LineWidth(-1); // Preserve this unrelated error while querying the actual renderbuffer selection.
        Assert.Equal((int)bound.ResourceId, cache.GetCurrentRenderbuffer());
        Assert.Equal((int)bound.ResourceId, cache.GetCurrentRenderbuffer());
        Assert.Equal((int)bound.ResourceId, GL.GetInteger(GetPName.RenderbufferBinding));
        Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
