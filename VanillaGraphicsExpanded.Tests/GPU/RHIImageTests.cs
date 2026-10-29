using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
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

    #region Public API
    /// <summary>Multisample copies retain their samples; client-pixel transfers remain unsupported for these images.</summary>
    [Theory]
    [InlineData(TextureDimension.TwoMultisample)] [InlineData(TextureDimension.TwoMultisampleArray)]
    public void MultisampleImageCopiesPreserveAllocation(TextureDimension dimension)
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var source = context.CreateTexture(Description(dimension, true)).RequireResource();
            using var destination = context.CreateTexture(Description(dimension, true)).RequireResource();
            var region = new RHITextureRegion(new(), 0, 0, 0, 4, 4, dimension == TextureDimension.TwoMultisampleArray ? 4 : 1);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            ulong revision = manager.GetResourceRegistration(destination)!.StorageRevision;
            context.CopyTexture(source, region, destination, region);
            Assert.Equal(revision, manager.GetResourceRegistration(destination)!.StorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var encoding = new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte);
            Assert.Throws<ArgumentException>(() => context.UploadTexture(destination, region, encoding, new byte[256]));
        }
        finally { Finish(); }
    }

    /// <summary>All represented dimensions establish coherent facts through mutable and immutable allocation.</summary>
    [Theory]
    [InlineData(TextureDimension.One, false)] [InlineData(TextureDimension.One, true)]
    [InlineData(TextureDimension.Two, false)] [InlineData(TextureDimension.Two, true)]
    [InlineData(TextureDimension.Three, false)] [InlineData(TextureDimension.Three, true)]
    [InlineData(TextureDimension.OneArray, false)] [InlineData(TextureDimension.OneArray, true)]
    [InlineData(TextureDimension.TwoArray, false)] [InlineData(TextureDimension.TwoArray, true)]
    [InlineData(TextureDimension.Cube, false)] [InlineData(TextureDimension.Cube, true)]
    [InlineData(TextureDimension.CubeArray, false)] [InlineData(TextureDimension.CubeArray, true)]
    [InlineData(TextureDimension.Rectangle, false)] [InlineData(TextureDimension.Rectangle, true)]
    [InlineData(TextureDimension.TwoMultisample, false)] [InlineData(TextureDimension.TwoMultisample, true)]
    [InlineData(TextureDimension.TwoMultisampleArray, false)] [InlineData(TextureDimension.TwoMultisampleArray, true)]
    public void DimensionsPublishCompleteImages(TextureDimension dimension, bool immutable)
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
            Assert.Equal(dimension is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray ? 0 : 1000, facts.MaxLevel);
            int faces = dimension == TextureDimension.Cube ? 6 : 1;
            Assert.Equal(request.MipLevels * faces, facts.Levels.Length);
            Assert.All(facts.Levels, image => Assert.True(image.Allocated));
            foreach (var image in facts.Levels)
            {
                Assert.Equal(Math.Max(1, request.Width >> image.Level), image.Width);
                Assert.Equal(dimension == TextureDimension.One ? 1 : dimension == TextureDimension.OneArray ? request.Height : Math.Max(1, request.Height >> image.Level), image.Height);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Explicit pitches and offsets preserve transfer contents and caller pixel-buffer/layout state.</summary>
    [Theory]
    [InlineData(TextureDimension.Two)] [InlineData(TextureDimension.Three)] [InlineData(TextureDimension.OneArray)]
    public void PitchedBufferTransfersPreserveContentAndSelectors(TextureDimension dimension)
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var texture = context.CreateTexture(Description(dimension, true)).RequireResource();
            using var source = context.CreatePixelUnpackBuffer(Buffer(256, BufferRole.PixelUnpack)).RequireResource();
            using var destination = context.CreatePixelPackBuffer(Buffer(256, BufferRole.PixelPack)).RequireResource();
            byte[] data = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
            GL.NamedBufferSubData(source.BufferId, 0, data.Length, data);
            using var packBinding = OpenGlStateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelPackBuffer, source.BufferId);
            using var unpackBinding = OpenGlStateCache.Current.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.PixelUnpackBuffer, destination.BufferId);
            using var packLayout = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(8));
            using var unpackLayout = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelLayouts.UnpackScope(OpenGlStateCache.Current, new(8));
            OpenGlStateCache.Current.InvalidateAll();
            var region = new RHITextureRegion(new(), 1, 0, 0, 2, dimension == TextureDimension.OneArray ? 1 : 2,
                dimension == TextureDimension.Two ? 1 : 2);
            var encoding = new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte);
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
            var description = Description(TextureDimension.TwoArray, true);
            var encoding = new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte);
            var region = new RHITextureRegion(new(1, 1), 0, 0, 0, 2, 2);
            byte[] initial = Enumerable.Repeat((byte)17, 16).ToArray();
            using var source = context.CreateTexture(description, [new(region, encoding, initial)]).RequireResource();
            using var view = context.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba8, BaseMip = 1, MipCount = 1, BaseLayer = 1, LayerCount = 1 }).RequireResource();
            using var destination = context.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2,
                Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Immutable }).RequireResource();
            var selection = new RHITextureRegion(new(), 0, 0, 0, 2, 2);
            context.CopyTexture(view, selection, destination, selection);
            byte[] result = new byte[16];
            GL.GetTextureSubImage(destination.TextureId, 0, 0, 0, 0, 2, 2, 1, PixelFormat.Rgba, PixelType.UnsignedByte, result.Length, result);
            Assert.Equal(initial, result);
            var manager = RHIResourceManagerSystem.CurrentManager!;
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
            var description = new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Mutable };
            Assert.Throws<ArgumentException>(() => context.CreateTexture(description));
            description.Width = 0; description.Height = 2;
            Assert.Throws<ArgumentOutOfRangeException>(() => context.CreateTexture(description));
            description.Width = 2;
            var region = new RHITextureRegion(new(), 0, 0, 0, 2, 2);
            var encoding = new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte);
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
            using var sampler = RHICommandContext.Current.CreateSampler(new RHISamplerDescription { MinFilter = SamplerFilter.Nearest,
                MagFilter = SamplerFilter.Linear, AddressS = SamplerAddressMode.MirroredRepeat, Compare = false, MinLod = 0 }).RequireResource();
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
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void SampleChoicesAvoidReadbackExceptOneValueFallback(bool renderbuffer, bool cached)
    {
        Start();
        var target = renderbuffer ? TextureDimension.Two : TextureDimension.TwoMultisample;
        var kind = renderbuffer ? GpuResourceKind.Renderbuffer : GpuResourceKind.Texture;
        const TextureFormat format = TextureFormat.Rgba8;
        var support = GpuSupport.GetInternalFormatCapabilities(target, format, kind);
        var field = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "formatCapabilities");
        var cache = (System.Collections.Generic.Dictionary<(GpuResourceKind, TextureDimension, TextureFormat), InternalFormatCapabilities>)field.GetValue(null)!;
        Assert.False(support.SampleCounts.IsDefaultOrEmpty);
        if (!cached) cache[(kind, target, format)] = support with { SampleCounts = System.Collections.Immutable.ImmutableArray<int>.Empty };
        try
        {
            using var capture = new ImageDescriptorCapture();
            if (renderbuffer)
            {
                using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Width = 4, Height = 4, Format = TextureFormat.Rgba8, Samples = 3 }).RequireResource();
                Assert.Equal(cached ? 0 : 1, capture.SampleReads);
                Assert.Equal(capture.SampleReads, capture.Reads);
                GL.GetNamedRenderbufferParameter(owner.RenderbufferId, RenderbufferParameterName.RenderbufferSamples, out int native);
                Assert.True(native >= 3);
                Assert.Equal(native, owner.Samples);
            }
            else
            {
                using var owner = RHICommandContext.Current.CreateTexture(Description(TextureDimension.TwoMultisample, true)).RequireResource();
                Assert.Equal(cached ? 0 : 1, capture.SampleReads);
                Assert.Equal(capture.SampleReads, capture.Reads);
                GL.GetTextureLevelParameter(owner.TextureId, 0, GetTextureParameter.TextureSamples, out int native);
                Assert.True(native >= 3);
                Assert.Equal(native, ((GpuTextureRef)owner).RequireImage(0).Samples);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { cache[(kind, target, format)] = support; Finish(); }
    }

    /// <summary>Native-boundary interruption retires only its partially configured candidate.</summary>
    [Fact]
    public unsafe void SamplerConfigurationFailureRetiresOnlyCandidate()
    {
        Start();
        try
        {
            using var survivor = RHICommandContext.Current.CreateSampler(new()).RequireResource();
            using var native = new NativeDriverCapture();
            nint original = native.Original("glSamplerParameteri");
            int calls = 0, candidate = 0;
            native.Add("glSamplerParameteri", new SamplerIntegerSetter((id, parameter, value) =>
            {
                ((delegate* unmanaged[Stdcall]<uint, int, int, void>)original)(id, parameter, value);
                candidate = unchecked((int)id);
                if (++calls == 2) native.Observe(() =>
                    OpenGlStateCache.Current.InterruptNativeResourceOperation(GpuResourceKind.Sampler, candidate));
            }));
            native.Install();
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.CreateSampler(new RHISamplerDescription
                { MinFilter = SamplerFilter.Nearest, MagFilter = SamplerFilter.Linear, AddressS = SamplerAddressMode.ClampToEdge }));
            Assert.Equal(2, calls); Assert.NotEqual(0, candidate);
            RHIResourceManagerSystem.CurrentManager!.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.False(GL.IsSampler(candidate)); Assert.True(GL.IsSampler(survivor.SamplerId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    /// <summary>Coverage sampling is rejected before allocation when unavailable and otherwise follows its named optional operation.</summary>
    [Fact]
    public void CoverageSamplingHonorsOptionalAdmission()
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            var description = new RHIRenderbufferDescription { Width = 4, Height = 4, Format = TextureFormat.Rgba8, Samples = 4, CoverageSamples = 4 };
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
    /// <summary>Matches native integer sampler configuration without throwing across unmanaged dispatch.</summary>
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate void SamplerIntegerSetter(uint name, int parameter, int value);
    /// <summary>Creates controlled dimensions, preserving array layers independently of mip geometry.</summary>
    private static RHITextureDescription Description(TextureDimension dimension, bool immutable)
    {
        bool multi = dimension is TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray;
        var result = new RHITextureDescription { Dimension = dimension, Width = 4, Format = TextureFormat.Rgba8,
            MipLevels = multi || dimension == TextureDimension.Rectangle ? 1 : 2, Storage = immutable ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable };
        if (dimension != TextureDimension.One) result.Height = 4;
        if (dimension is TextureDimension.Three or TextureDimension.TwoArray or TextureDimension.TwoMultisampleArray) result.Depth = 4;
        if (dimension == TextureDimension.CubeArray) result.Depth = 12;
        if (multi) { result.Samples = 3; result.FixedSampleLocations = false; }
        return result;
    }
    /// <summary>Provides legal buffer allocation intent independent of its consuming binding role.</summary>
    private static RHIBufferDescription Buffer(int capacity, BufferRole role) => new() { Role = role, Capacity = capacity, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw };
    /// <summary>Begins the original managed resource lifetime on the fixture context.</summary>
    private void Start() { fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); }
    /// <summary>Drains original retirements and discards test-owned selector knowledge.</summary>
    private void Finish() { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    #endregion
}
