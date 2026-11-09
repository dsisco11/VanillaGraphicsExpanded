using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises image-definition redirection through real native objects, aliases and image consumers.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Compressed uploads and framebuffer copies publish actual image definitions through their distinct native families.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressedAndCopiedImagesPublish(bool compressed)
    {
        // Arrange: Register the destination texture and bind the source framebuffer before selecting compressed or copy input.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(DefineCopiedOrCompressed));
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(source);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            framebuffer.Bind();
            using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId);
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture)!;
            ulong revision = entry.MetadataRevision;
            // Act: Define the destination image through compressed upload or framebuffer-copy entry points.
            DefineCopiedOrCompressed(compressed);
            // Assert: Published dimensions, format, and metadata revision match the executed definition.
            Assert.Equal(compressed ? 4 : 6, texture.Width);
            Assert.Equal(4, texture.Height);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(compressed ? TextureFormat.CompressedRgbaBptcUnorm : TextureFormat.Rgba8, texture.InternalFormat);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Regenerating an already allocated immutable chain changes contents without retiring existing texture views.</summary>
    [Fact]
    public void ImmutableMipRegenerationPreservesView()
    {
        // Arrange: Create immutable four-level storage and a view covering its existing mip chain.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(GenerateLevels));
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int raw);
        try
        {
            GL.TextureStorage2D(raw, 4, SizedInternalFormat.Rgba8, 8, 8);
            using var texture = new BorrowedTexture(raw);
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = texture, Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 4, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
            using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId);
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture)!;
            ulong storage = entry.StorageRevision;
            // Act: Regenerate contents in the already allocated immutable mip chain.
            GenerateLevels();
            // Assert: Content generation leaves the allocation revision and existing view valid.
            Assert.Equal(storage, entry.StorageRevision);
            Assert.True(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, raw)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Bound, named and explicit-unit image definitions update aliases without changing the selected texture unit.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ImageDefinitionPublishesAliases(int variant)
    {
        // Arrange: Register owner, alias, and framebuffer observer before choosing a native definition overload.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(DefineImage));
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var alias = new BorrowedTexture(texture.TextureId);
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int changed = 0;
            framebuffer.AttachmentsChanged += () => changed++;
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture)!;
            ulong revision = entry.MetadataRevision;
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
            if (variant == 2) GL.ActiveTexture(TextureUnit.Texture5);
            OpenGlStateCache.Current.InvalidateAll();
            if (variant == 0) GL.Enable((EnableCap)(-1));
            // Act: Define new storage through bound, named, explicit-unit, or immutable-storage entry points.
            DefineImage(variant, texture.TextureId);
            // Assert: Owner, alias, and attachment observe one publication while the caller's active unit is preserved.
            Assert.Equal(8, texture.Width);
            Assert.Equal(8, alias.Width);
            Assert.Equal(8, image.Width);
            Assert.Equal(1, changed);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal((int)(variant == 2 ? TextureUnit.Texture5 : TextureUnit.Texture3), GL.GetInteger(GetPName.ActiveTexture));
            if (variant == 0) Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Proxy and content-only calls leave metadata revisions unchanged; rejected definitions retain actual dimensions.</summary>
    [Fact]
    public void ProxyContentsAndRejectedDefinitionsPreserveFacts()
    {
        // Arrange: Retain the existing image revision while the bound texture receives proxy, content, and invalid-size calls.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(DefineSpecial));
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId);
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture)!;
            ulong revision = entry.MetadataRevision;
            // Act: Run proxy and content-only commands, then submit an invalid storage definition.
            DefineSpecial(0);
            DefineSpecial(1);
            // Assert: Non-defining calls preserve facts, and the rejected definition leaves the real dimensions unchanged.
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(4, texture.Width);
            DefineSpecial(2);
            Assert.Equal(4, texture.Width);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Generated levels are read from their actual nonzero base image rather than inferred from level zero.</summary>
    [Fact]
    public void MipGenerationObservesNonzeroBase()
    {
        // Arrange: Define level one directly and select it as the nonzero base before mip generation.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(GenerateLevels));
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId);
            // Seed level 1 only so generation and observation must respect the selected nonzero base level.
            GL.TexImage2D(TextureTarget.Texture2D, 1, PixelInternalFormat.Rgba8, 4, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            texture.SetMipRange(1, 3);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture));
            ulong revision = entry.MetadataRevision;
            // Act: Generate the selected chain, then repeat after an observer has attached to an existing level.
            GenerateLevels();
            // Assert: The first call publishes derived levels; repeating it changes neither storage metadata nor attachment observers.
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Contains(entry.Metadata.Levels, level => level.Level == 2 && level.Width == 2);
            Assert.Contains(entry.Metadata.Levels, level => level.Level == 3 && level.Width == 1);
            using var image = GpuFramebufferAttachment.FromTexture(texture, mipLevel: 1);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int changes = 0;
            framebuffer.AttachmentsChanged += () => changes++;
            revision = entry.MetadataRevision;
            ulong storage = entry.StorageRevision;
            GenerateLevels();
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(storage, entry.StorageRevision);
            Assert.Equal(0, changes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Cube-face selectors resolve the cube object and publish the selected face's image facts.</summary>
    [Fact]
    public void CubeFaceDefinitionUpdatesSelectedImage()
    {
        // Arrange: Observe the positive-X face through a framebuffer before defining that face through its cube selector.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(DefineCubeFace));
        try
        {
            using var texture = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba8, Width = 4, Height = 4, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture, cubeFace: CubeFace.PositiveX);
            using var framebuffer = DynamicGpuFramebuffer.Create([image])!;
            int changed = 0;
            framebuffer.AttachmentsChanged += () => changed++;
            using var binding = OpenGlStateCache.Current.BindTextureScope(OpenGLImageValues.Target(((GpuTextureRef)texture).Target), 0, texture.TextureId);
            // Act: Define the selected cube face while the native selector names the cube object.
            DefineCubeFace();
            // Assert: The selected image reports its new extent and notifies its framebuffer once.
            Assert.Equal(8, image.Width);
            Assert.Equal(1, changed);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>One-dimensional, volumetric and multisample image commands retain their real target and shape.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AdditionalTargetsPublish(int variant)
    {
        // Arrange: Select a 1D, 3D, or multisample native target and define its first observed shape.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int raw = GL.GenTexture();
        try
        {
            var target = variant == 0 ? TextureTarget.Texture1D : variant == 1 ? TextureTarget.Texture3D : TextureTarget.Texture2DMultisample;
            GL.BindTexture(target, raw);
            DefineTarget(variant, 4);
            OpenGlStateCache.Current.InvalidateAll();
            using var texture = new BorrowedTexture(raw);
            // Act: Redefine the same object with a wider shape, recovering metadata explicitly when needed.
            DefineTarget(variant, 8);
            if (variant == 2 && !((GpuTextureRef)texture).IsValid)
            {
                // An unchanged resolved sample count cannot certify which mutable
                // allocation survived; recovery must be explicitly requested.
                Assert.Equal(0, texture.Width);
                RHIResourceManager.RecoverWrapperMetadata(texture, new GpuTextureMetadataReader(TextureDimension.TwoMultisample));
            }
            // Assert: The borrowed wrapper reports the new width and retains the original target dimension.
            Assert.Equal(8, texture.Width);
            Assert.Equal(OpenGLImageValues.Dimension(target), texture.TextureTarget);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, raw));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Installs production redirection on one no-inline native test caller.</summary>
    private static Harmony Patch(string method)
    {
        var harmony = new Harmony("VGE.Tests.TextureMetadata." + method);
        return harmony;
    }

    /// <summary>Defines compressed storage or copies an image from the selected read framebuffer.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineCopiedOrCompressed(bool compressed)
    {
        if (compressed) GL.CompressedTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.CompressedRgbaBptcUnorm, 4, 4, 0, 16, new byte[16]);
        else GL.CopyTexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 0, 0, 6, 4, 0);
    }

    /// <summary>Forwards distinct native selectors and a caller-defined generic texel payload.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineImage(int variant, int id)
    {
        switch (variant)
        {
            case 0: GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, Enumerable.Repeat(new Texel { R = 1, G = 2, B = 3, A = 4 }, 32).ToArray()); break;
            case 1: GL.Ext.TextureImage2D(id, TextureTarget.Texture2D, 0, InternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero); break;
            case 2: GL.Ext.MultiTexImage2D(TextureUnit.Texture3, TextureTarget.Texture2D, 0, (ExtDirectStateAccess)PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero); break;
            case 3: GL.TextureStorage2D(id, 1, SizedInternalFormat.Rgba8, 8, 4); break;
        }
    }

    /// <summary>Separates object-free proxy work, content updates and a rejected storage definition.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineSpecial(int variant)
    {
        if (variant == 0) GL.TexImage2D(TextureTarget.ProxyTexture2D, 0, PixelInternalFormat.Rgba8, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        else if (variant == 1) GL.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[4]);
        else GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, -1, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
    }

    /// <summary>Exercises actual mip generation through the intercepted command boundary.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GenerateLevels() => GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

    /// <summary>Defines one cube face while the native selector refers to its encompassing cube object.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineCubeFace() => GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX, 0, PixelInternalFormat.Rgba8, 8, 8, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);

    /// <summary>Allocates target-specific native shapes without changing their established object target.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineTarget(int variant, int width)
    {
        if (variant == 0) GL.TexImage1D(TextureTarget.Texture1D, 0, PixelInternalFormat.Rgba8, width, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        else if (variant == 1) GL.TexImage3D(TextureTarget.Texture3D, 0, PixelInternalFormat.Rgba8, width, 4, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
        else GL.TexImage2DMultisample(TextureTargetMultisample.Texture2DMultisample, 4, PixelInternalFormat.Rgba8, width, 4, true);
    }

    /// <summary>Represents an unmanaged custom upload element whose generic call must retain exact marshalling.</summary>
    private struct Texel { internal byte R, G, B, A; }
    #endregion
}
