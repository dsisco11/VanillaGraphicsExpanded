using System;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks complete typed slot publication independently of ownership of an engine executable.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHISampledSlotTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>An invalid later input leaves all earlier supplied slots and the active selector untouched.</summary>
    [Fact]
    public void InvalidLaterTextureDoesNotPublishEarlierSlots()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var original = context.CreateTexture2D(Description()).RequireResource();
            using var candidate = context.CreateTexture2D(Description()).RequireResource();
            var retired = context.CreateTexture2D(Description()).RequireResource();
            context.BindSampledTextures([new(0, original)]);
            GL.ActiveTexture(TextureUnit.Texture5);
            StateCache.Current.Invalidate(EPipelineState.TextureBindings);
            retired.Dispose();
            Assert.ThrowsAny<InvalidOperationException>(() => context.BindSampledTextures([new(0, candidate), new(1, retired)]));
            Assert.Equal((int)TextureUnit.Texture5, GL.GetInteger(GetPName.ActiveTexture));
            GL.ActiveTexture(TextureUnit.Texture0);
            Assert.Equal(original.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { StateCache.Current.InvalidateAll(); fixture.CleanupGpuResources(); }
    }
    /// <summary>Empty inputs clear only their declared target, retain unrelated targets and clear the supplied sampler.</summary>
    [Fact]
    public void EmptySlotPreservesOtherTextureTargets()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var two = context.CreateTexture2D(Description()).RequireResource();
            using var three = context.CreateTexture3D(new() { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba8,
                Width = 1, Height = 1, Depth = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var sampler = context.CreateSampler(new()).RequireResource();
            context.BindSampledTextures([new(0, three, Dimension: TextureDimension.Three)]);
            context.BindSampledTextures([new(0, two, sampler)]);
            context.BindSampledTextures([new(0, default)]);
            GL.ActiveTexture(TextureUnit.Texture0);
            int absent = GL.GetInteger(GetPName.TextureBinding2D);
            int retained = GL.GetInteger(GetPName.TextureBinding3D);
            GL.GetInteger((GetIndexedPName)All.SamplerBinding, 0, out int absentSampler);
            Assert.Equal(0, absent); Assert.Equal(three.TextureId, retained); Assert.Equal(0, absentSampler);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { StateCache.Current.InvalidateAll(); fixture.CleanupGpuResources(); }
    }
    /// <summary>An unrelated native error is not consumed or attributed to a successful logical uniform upload.</summary>
    [Fact]
    public void UniformWriteLeavesUnrelatedNativeErrorWithItsCaller()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var ring = new GpuUniformRingBuffer(pageSizeBytes: 65536, pageCount: 3, preferPersistent: false, coherent: false);
            ring.BeginFrame(0);
            GL.Viewport(0, 0, -1, 1);
            var allocation = ring.AllocateAndWrite(new byte[16]);
            Assert.True(allocation.IsValid);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            ring.EndFrame();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { StateCache.Current.InvalidateAll(); fixture.CleanupGpuResources(); }
    }
    #endregion
    #region Private
    /// <summary>Authors a minimal sampled image without any native format input.</summary>
    private static RHITextureDescription Description() => new() { Dimension = TextureDimension.Two,
        Format = TextureFormat.Rgba8, Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable };
    #endregion
}
