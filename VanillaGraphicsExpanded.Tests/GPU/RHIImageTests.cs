using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks semantic image operations against native content and preserved surrounding selectors.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHIImageTests(HeadlessGLFixture fixture)
{
    private static int samplerCalls, candidateSampler;
    private static readonly InvalidOperationException samplerFailure = new("Injected sampler configuration failure.");

    #region Public API
    /// <summary>Multisample copies retain their samples; client-pixel transfers remain unsupported for these images.</summary>
    [Theory]
    [InlineData(RHITextureDimension.TwoMultisample)] [InlineData(RHITextureDimension.TwoMultisampleArray)]
    public void MultisampleImageCopiesPreserveAllocation(RHITextureDimension dimension)
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var source = context.CreateTexture(Description(dimension, true)).RequireResource();
            using var destination = context.CreateTexture(Description(dimension, true)).RequireResource();
            var region = new RHITextureRegion(new(), 0, 0, 0, 4, 4, dimension == RHITextureDimension.TwoMultisampleArray ? 4 : 1);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            ulong revision = manager.GetResourceRegistration(destination)!.StorageRevision;
            context.CopyTexture(source, region, destination, region);
            Assert.Equal(revision, manager.GetResourceRegistration(destination)!.StorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var encoding = new RHIPixelEncoding(RHIPixelChannels.Rgba, RHIPixelType.UnsignedByte);
            Assert.Throws<ArgumentException>(() => context.UploadTexture(destination, region, encoding, new byte[256]));
        }
        finally { Finish(); }
    }

    /// <summary>All represented dimensions establish coherent facts through mutable and immutable allocation.</summary>
    [Theory]
    [InlineData(RHITextureDimension.One, false)] [InlineData(RHITextureDimension.One, true)]
    [InlineData(RHITextureDimension.Two, false)] [InlineData(RHITextureDimension.Two, true)]
    [InlineData(RHITextureDimension.Three, false)] [InlineData(RHITextureDimension.Three, true)]
    [InlineData(RHITextureDimension.OneArray, false)] [InlineData(RHITextureDimension.OneArray, true)]
    [InlineData(RHITextureDimension.TwoArray, false)] [InlineData(RHITextureDimension.TwoArray, true)]
    [InlineData(RHITextureDimension.Cube, false)] [InlineData(RHITextureDimension.Cube, true)]
    [InlineData(RHITextureDimension.CubeArray, false)] [InlineData(RHITextureDimension.CubeArray, true)]
    [InlineData(RHITextureDimension.Rectangle, false)] [InlineData(RHITextureDimension.Rectangle, true)]
    [InlineData(RHITextureDimension.TwoMultisample, false)] [InlineData(RHITextureDimension.TwoMultisample, true)]
    [InlineData(RHITextureDimension.TwoMultisampleArray, false)] [InlineData(RHITextureDimension.TwoMultisampleArray, true)]
    public void DimensionsPublishCompleteImages(RHITextureDimension dimension, bool immutable)
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            var request = Description(dimension, immutable);
            using var texture = context.CreateTexture(request).RequireResource();
            GpuTextureRef reference = texture;
            var facts = reference.RequireMetadata();
            Assert.Equal(immutable, facts.ImmutableStorage);
            Assert.Equal(0, facts.BaseLevel);
            Assert.Equal(dimension is RHITextureDimension.TwoMultisample or RHITextureDimension.TwoMultisampleArray ? 0 : 1000, facts.MaxLevel);
            int faces = dimension == RHITextureDimension.Cube ? 6 : 1;
            Assert.Equal(request.MipLevels * faces, facts.Levels.Length);
            Assert.All(facts.Levels, image => Assert.True(image.Allocated));
            foreach (var image in facts.Levels)
            {
                Assert.Equal(Math.Max(1, request.Width >> image.Level), image.Width);
                Assert.Equal(dimension == RHITextureDimension.One ? 1 : dimension == RHITextureDimension.OneArray ? request.Height : Math.Max(1, request.Height >> image.Level), image.Height);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Explicit pitches and offsets preserve transfer contents and caller pixel-buffer/layout state.</summary>
    [Theory]
    [InlineData(RHITextureDimension.Two)] [InlineData(RHITextureDimension.Three)] [InlineData(RHITextureDimension.OneArray)]
    public void PitchedBufferTransfersPreserveContentAndSelectors(RHITextureDimension dimension)
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var texture = context.CreateTexture(Description(dimension, true)).RequireResource();
            using var source = context.CreatePixelUnpackBuffer(Buffer(256, RHIBufferRole.PixelUnpack)).RequireResource();
            using var destination = context.CreatePixelPackBuffer(Buffer(256, RHIBufferRole.PixelPack)).RequireResource();
            byte[] data = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
            GL.NamedBufferSubData(source.BufferId, 0, data.Length, data);
            using var packBinding = StateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelPackBuffer, source.BufferId);
            using var unpackBinding = StateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelUnpackBuffer, destination.BufferId);
            using var packLayout = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelLayouts.PackScope(StateCache.Current, new(8));
            using var unpackLayout = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelLayouts.UnpackScope(StateCache.Current, new(8));
            StateCache.Current.InvalidateAll();
            var region = new RHITextureRegion(new(), 1, 0, 0, 2, dimension == RHITextureDimension.OneArray ? 1 : 2,
                dimension == RHITextureDimension.Two ? 1 : 2);
            var encoding = new RHIPixelEncoding(RHIPixelChannels.Rgba, RHIPixelType.UnsignedByte);
            context.TransferBufferToTexture(texture, new(source, 16, region, encoding, 16, 48));
            context.TransferTextureToBuffer(texture, new(destination, 32, region, encoding, 16, 48));
            byte[] result = new byte[256];
            GL.GetNamedBufferSubData(destination.BufferId, 0, result.Length, result);
            for (int z = 0; z < region.Depth; z++)
                for (int y = 0; y < region.Height; y++)
                    Assert.Equal(data.AsSpan(16 + z * 48 + y * 16, 8).ToArray(), result.AsSpan(32 + z * 48 + y * 16, 8).ToArray());
            Assert.Equal(source.BufferId, GL.GetInteger(GetPName.PixelPackBufferBinding));
            Assert.Equal(destination.BufferId, GL.GetInteger(GetPName.PixelUnpackBufferBinding));
            Assert.Equal(8, GL.GetInteger(GetPName.PackAlignment));
            Assert.Equal(8, GL.GetInteger(GetPName.UnpackAlignment));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Initial client data, clear and copy retain contents without changing canonical allocation revision.</summary>
    [Fact]
    public void InitialContentCopyAndClearUseViewRelativeCoordinates()
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            var description = Description(RHITextureDimension.TwoArray, true);
            var encoding = new RHIPixelEncoding(RHIPixelChannels.Rgba, RHIPixelType.UnsignedByte);
            var region = new RHITextureRegion(new(1, 1), 0, 0, 0, 2, 2);
            byte[] initial = Enumerable.Repeat((byte)17, 16).ToArray();
            using var source = context.CreateTexture(description, [new(region, encoding, initial)]).RequireResource();
            using var view = context.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = RHITextureDimension.Two,
                Format = RHIFormat.Rgba8, BaseMip = 1, MipCount = 1, BaseLayer = 1, LayerCount = 1 }).RequireResource();
            using var destination = context.CreateTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Width = 2, Height = 2,
                Format = RHIFormat.Rgba8, MipLevels = 1, Storage = RHIStorageMode.Immutable }).RequireResource();
            var selection = new RHITextureRegion(new(), 0, 0, 0, 2, 2);
            context.CopyTexture(view, selection, destination, selection);
            byte[] result = new byte[16];
            GL.GetTextureSubImage(destination.TextureId, 0, 0, 0, 0, 2, 2, 1, PixelFormat.Rgba, PixelType.UnsignedByte, result.Length, result);
            Assert.Equal(initial, result);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            ulong revision = manager.GetResourceRegistration(source)!.StorageRevision;
            context.ClearTexture(view, selection, encoding, new byte[] { 3, 4, 5, 6 });
            GL.GetTextureSubImage(source.TextureId, 1, 0, 0, 1, 2, 2, 1, PixelFormat.Rgba, PixelType.UnsignedByte, result.Length, result);
            Assert.Equal(Enumerable.Range(0, 16).Select(index => (byte)(3 + index % 4)), result);
            Assert.Equal(revision, manager.GetResourceRegistration(source)!.StorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Invalid intent and insufficient payloads reject before allocation or content mutation.</summary>
    [Fact]
    public void InvalidInputsRejectBeforeNativeMutation()
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            var description = new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba8, MipLevels = 1, Storage = RHIStorageMode.Mutable };
            Assert.Throws<ArgumentException>(() => context.CreateTexture(description));
            description.Width = 0; description.Height = 2;
            Assert.Throws<ArgumentOutOfRangeException>(() => context.CreateTexture(description));
            description.Width = 2;
            var region = new RHITextureRegion(new(), 0, 0, 0, 2, 2);
            var encoding = new RHIPixelEncoding(RHIPixelChannels.Rgba, RHIPixelType.UnsignedByte);
            Assert.Throws<ArgumentException>(() => context.CreateTexture(description, [new(region, encoding, new byte[15])]));
            using var texture = context.CreateTexture(description).RequireResource();
            Assert.Throws<ArgumentException>(() => context.UploadTexture(texture, region, encoding, new byte[16], rowPitch: 3));
            Assert.Throws<ArgumentException>(() => context.CopyTexture(texture, region, texture, region));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Sampler initialization applies supplied fields while omitted native defaults remain untouched.</summary>
    [Fact]
    public void SamplerIntentPreservesDefaultsAndExplicitFalse()
    {
        Start();
        try
        {
            using var sampler = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { MinFilter = RHISamplerFilter.Nearest,
                MagFilter = RHISamplerFilter.Linear, AddressS = RHISamplerAddress.MirroredRepeat, Compare = false, MinLod = 0 }).RequireResource();
            GL.GetSamplerParameter(sampler.SamplerId, SamplerParameterName.TextureMinFilter, out int min);
            GL.GetSamplerParameter(sampler.SamplerId, SamplerParameterName.TextureWrapT, out int wrap);
            GL.GetSamplerParameter(sampler.SamplerId, SamplerParameterName.TextureCompareMode, out int compare);
            Assert.Equal((int)TextureMinFilter.Nearest, min);
            Assert.Equal((int)TextureWrapMode.Repeat, wrap);
            Assert.Equal((int)TextureCompareMode.None, compare);
            GpuSamplerRef reference = sampler;
            Assert.True(reference.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>The approved one-value fallback operates only when cached per-format sample choices are absent.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MissingSampleChoicesReadOnlyAllocationSamples(bool renderbuffer)
    {
        Start();
        var target = renderbuffer ? OpenTK.Graphics.OpenGL.ImageTarget.Renderbuffer : OpenTK.Graphics.OpenGL.ImageTarget.Texture2DMultisample;
        const OpenTK.Graphics.OpenGL.SizedInternalFormat format = OpenTK.Graphics.OpenGL.SizedInternalFormat.Rgba8;
        var support = GpuSupport.GetInternalFormatCapabilities(target, format);
        var field = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "formatCapabilities");
        var cache = (System.Collections.Generic.Dictionary<(OpenTK.Graphics.OpenGL.ImageTarget, OpenTK.Graphics.OpenGL.SizedInternalFormat), InternalFormatCapabilities>)field.GetValue(null)!;
        cache[(target, format)] = support with { SampleCounts = System.Collections.Immutable.ImmutableArray<int>.Empty };
        try
        {
            if (renderbuffer)
            {
                using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Width = 4, Height = 4, Format = RHIFormat.Rgba8, Samples = 3 }).RequireResource();
                GL.GetNamedRenderbufferParameter(owner.RenderbufferId, RenderbufferParameterName.RenderbufferSamples, out int native);
                Assert.True(native >= 3);
                Assert.Equal(native, owner.Samples);
            }
            else
            {
                using var owner = RHICommandContext.Current.CreateTexture(Description(RHITextureDimension.TwoMultisample, true)).RequireResource();
                GL.GetTextureLevelParameter(owner.TextureId, 0, GetTextureParameter.TextureSamples, out int native);
                Assert.True(native >= 3);
                Assert.Equal(native, ((GpuTextureRef)owner).RequireImage(0).Samples);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache[(target, format)] = support; Finish(); }
    }

    /// <summary>A partially configured candidate is retired once and the original native exception propagates.</summary>
    [Fact]
    public void SamplerConfigurationFailureRetiresOnlyCandidate()
    {
        Start();
        var harmony = new HarmonyLib.Harmony("VGE.Tests.RHIImageSamplerFailure");
        samplerCalls = 0; candidateSampler = 0;
        var native = HarmonyLib.AccessTools.Method(typeof(GL), nameof(GL.SamplerParameter), [typeof(int), typeof(SamplerParameterName), typeof(int)]);
        harmony.Patch(native, prefix: new HarmonyLib.HarmonyMethod(typeof(RHIImageTests), nameof(RejectSecondSamplerParameter)));
        try
        {
            var failure = Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.CreateSampler(new RHISamplerDescription
                { MinFilter = RHISamplerFilter.Nearest, MagFilter = RHISamplerFilter.Linear, AddressS = RHISamplerAddress.ClampToEdge }));
            Assert.Same(samplerFailure, failure);
            Assert.Equal(2, samplerCalls);
            Assert.NotEqual(0, candidateSampler);
            GpuResourceManagerSystem.CurrentManager!.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(GL.IsSampler(candidateSampler));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); Finish(); }
    }

    /// <summary>Coverage sampling is rejected before allocation when unavailable and otherwise follows its named optional operation.</summary>
    [Fact]
    public void CoverageSamplingHonorsOptionalAdmission()
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            var description = new RHIRenderbufferDescription { Width = 4, Height = 4, Format = RHIFormat.Rgba8, Samples = 4, CoverageSamples = 4 };
            if (!context.Capabilities.CoverageSampling) Assert.Throws<NotSupportedException>(() => context.CreateRenderbuffer(description));
            else
            {
                using var owner = context.CreateRenderbuffer(description).RequireResource();
                GL.GetNamedRenderbufferParameter(owner.RenderbufferId, RenderbufferParameterName.RenderbufferSamples, out int samples);
                Assert.Equal(samples, owner.Samples);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion

    #region Private
    /// <summary>Interrupts the second setter before it can execute, preserving the first native effect.</summary>
    private static void RejectSecondSamplerParameter(int __0)
    {
        candidateSampler = __0;
        if (++samplerCalls == 2) throw samplerFailure;
    }

    /// <summary>Creates controlled dimensions, preserving array layers independently of mip geometry.</summary>
    private static RHITextureDescription Description(RHITextureDimension dimension, bool immutable)
    {
        bool multi = dimension is RHITextureDimension.TwoMultisample or RHITextureDimension.TwoMultisampleArray;
        var result = new RHITextureDescription { Dimension = dimension, Width = 4, Format = RHIFormat.Rgba8,
            MipLevels = multi || dimension == RHITextureDimension.Rectangle ? 1 : 2, Storage = immutable ? RHIStorageMode.Immutable : RHIStorageMode.Mutable };
        if (dimension != RHITextureDimension.One) result.Height = 4;
        if (dimension is RHITextureDimension.Three or RHITextureDimension.TwoArray or RHITextureDimension.TwoMultisampleArray) result.Depth = 4;
        if (dimension == RHITextureDimension.CubeArray) result.Depth = 12;
        if (multi) { result.Samples = 3; result.FixedSampleLocations = false; }
        return result;
    }
    /// <summary>Provides legal buffer allocation intent independent of its consuming binding role.</summary>
    private static RHIBufferDescription Buffer(int capacity, RHIBufferRole role) => new() { Role = role, Capacity = capacity, Mode = RHIStorageMode.Mutable, Usage = RHIBufferUsage.StaticDraw };
    /// <summary>Begins the original managed resource lifetime on the fixture context.</summary>
    private void Start() { fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); }
    /// <summary>Drains original retirements and discards test-owned selector knowledge.</summary>
    private void Finish() { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    #endregion
}
