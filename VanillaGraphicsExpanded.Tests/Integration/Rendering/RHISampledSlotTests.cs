using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks complete typed slot publication independently of ownership of an engine executable.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHISampledSlotTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>An invalid later input leaves all earlier supplied slots and the active selector untouched.</summary>
    [Fact]
    public void InvalidLaterTextureDoesNotPublishEarlierSlots()
    {
        // Arrange: bind one valid slot, select a nonzero unit, and retire a later candidate.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        Exception? testFailure = null;
        try
        {
            var context = RHICommandContext.Current;
            using var activeTexture = new ActiveTextureScope();
            using var original = context.CreateTexture2D(Description()).RequireResource();
            using var candidate = context.CreateTexture2D(Description()).RequireResource();
            var retired = context.CreateTexture2D(Description()).RequireResource();
            context.BindSampledTextures([new(0, original)]);
            GL.ActiveTexture(TextureUnit.Texture5);
            OpenGlStateCache.Current.Invalidate(EPipelineState.TextureBindings);
            retired.Dispose();
            // Act: submit a batch whose later slot has already been retired.
            Assert.ThrowsAny<InvalidOperationException>(() => context.BindSampledTextures([new(0, candidate), new(1, retired)]));
            // Assert: rejection preserves the original slot and caller-selected active unit.
            Assert.Equal((int)TextureUnit.Texture5, GL.GetInteger(GetPName.ActiveTexture));
            GL.ActiveTexture(TextureUnit.Texture0);
            Assert.Equal(original.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { Finish(testFailure); }
    }
    /// <summary>Empty inputs clear only their declared target, retain unrelated targets and clear the supplied sampler.</summary>
    [Fact]
    public void EmptySlotPreservesOtherTextureTargets()
    {
        // Arrange: bind 2D and 3D targets plus a sampler to the same declared slot.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        Exception? testFailure = null;
        try
        {
            var context = RHICommandContext.Current;
            using var two = context.CreateTexture2D(Description()).RequireResource();
            using var three = context.CreateTexture3D(new() { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba8,
                Width = 1, Height = 1, Depth = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var sampler = context.CreateSampler(new()).RequireResource();
            context.BindSampledTextures([new(0, three, Dimension: TextureDimension.Three)]);
            context.BindSampledTextures([new(0, two, sampler)]);
            // Act: publish an empty slot that clears only the declared 2D target and sampler.
            context.BindSampledTextures([new(0, default)]);
            GL.ActiveTexture(TextureUnit.Texture0);
            int absent = GL.GetInteger(GetPName.TextureBinding2D);
            int retained = GL.GetInteger(GetPName.TextureBinding3D);
            GL.GetInteger((GetIndexedPName)All.SamplerBinding, 0, out int absentSampler);
            // Assert: empty publication clears its declared target and sampler while preserving the 3D binding.
            Assert.Equal(0, absent); Assert.Equal(three.TextureId, retained); Assert.Equal(0, absentSampler);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { Finish(testFailure); }
    }
    /// <summary>An unrelated native error is not consumed or attributed to a successful logical uniform upload.</summary>
    [Fact]
    public void UniformWriteLeavesUnrelatedNativeErrorWithItsCaller()
    {
        // Arrange: create a uniform ring and queue one unrelated native GL error.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        Exception? testFailure = null;
        try
        {
            using var ring = new GpuUniformRingBuffer(pageSizeBytes: 65536, pageCount: 3, preferPersistent: false, coherent: false);
            ring.BeginFrame(0);
            GL.Viewport(0, 0, -1, 1);
            // Act: perform a valid logical upload while the unrelated GL error is pending.
            var allocation = ring.AllocateAndWrite(new byte[16]);
            // Assert: the allocation succeeds and the caller still observes its own error.
            Assert.True(allocation.IsValid);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            ring.EndFrame();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        catch (Exception error) { testFailure = error; throw; }
        finally { Finish(testFailure); }
    }
    #endregion
    #region Private
    /// <summary>Restores the caller's active texture unit even when a selector assertion fails.</summary>
    private sealed class ActiveTextureScope : IDisposable
    {
        #region Public API
        /// <summary>Creates a scope over the currently active texture unit.</summary>
        public ActiveTextureScope() { }

        /// <summary>Restores the captured selector and invalidates cached bindings changed by direct GL calls.</summary>
        public void Dispose()
        {
            GL.ActiveTexture((TextureUnit)previous);
            OpenGlStateCache.Current.InvalidateAll();
        }
        #endregion

        #region Private
        private readonly int previous = GL.GetInteger(GetPName.ActiveTexture);
        #endregion
    }

    /// <summary>Retires resources, reports unexpected errors after success, and isolates errors after failure.</summary>
    private void Finish(Exception? testFailure)
    {
        try
        {
            OpenGlStateCache.Current.InvalidateAll();
            fixture.CleanupGpuResources();
        }
        catch (Exception cleanupFailure)
        {
            DrainGlErrors(cleanupFailure);
            if (testFailure is null)
                throw;

            // Preserve the original assertion/action failure while retaining cleanup diagnostics on it.
            testFailure.Data["GPU resource cleanup failure"] = cleanupFailure;
            return;
        }

        if (testFailure is null)
            AssertNoGlErrors();
        else
            DrainGlErrors(testFailure);
    }

    /// <summary>Collects and reports any GL errors left by a successful test and resource cleanup.</summary>
    private static void AssertNoGlErrors()
    {
        var errors = new System.Collections.Generic.List<ErrorCode>();
        ErrorCode error;
        while ((error = GL.GetError()) != ErrorCode.NoError)
            errors.Add(error);
        Assert.Empty(errors);
    }

    /// <summary>Consumes every remaining error after a failed test so it cannot contaminate later cases.</summary>
    private static void DrainGlErrors(Exception? retainedFailure = null)
    {
        try
        {
            while (GL.GetError() != ErrorCode.NoError)
            {
            }
        }
        catch (Exception error)
        {
            if (retainedFailure is null)
                throw;

            retainedFailure.Data["GL error drain failure"] = error;
        }
    }

    /// <summary>Authors a minimal sampled image without any native format input.</summary>
    private static RHITextureDescription Description() => new() { Dimension = TextureDimension.Two,
        Format = TextureFormat.Rgba8, Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable };
    #endregion
}
