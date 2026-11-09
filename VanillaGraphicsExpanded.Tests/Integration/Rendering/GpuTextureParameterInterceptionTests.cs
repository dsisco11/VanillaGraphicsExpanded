using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks represented texture parameters.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed unsafe class GpuTextureParameterInterceptionTests(HeadlessGLFixture fixture)
{
    private static int observations;
    /// <summary>Matches the integer selector query ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void IntegerQuery(int parameter, int* value);
    #region Public API
    /// <summary>Unsupported target dimensions are rejected before metadata queries can reinterpret an unrelated valid object.</summary>
    [Fact]
    public void UnsupportedTargetReaderRejectsWithoutNativeErrors()
    {
        // Arrange: Use a valid 2D object while requesting metadata through an unsupported target dimension.
        fixture.EnsureContextValid();
        using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        // Act and assert: The reader rejects the unsupported target without issuing an invalid native query.
        Assert.False(new GpuTextureMetadataReader((TextureDimension)(-1)).TryRead(texture.TextureId, out _));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Parameter overloads update the canonical range without advancing allocation identity.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RepresentedParameterPublishesOnce(int variant)
    {
        // Arrange: Patch the production interception path and register an aliased texture with a known allocation revision.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(SetRange));
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), MipLevels = 4, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (4) - 1, Label = null }).RequireResource();
            using var alias = new BorrowedTexture(texture.TextureId);
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture));
            ulong revision = entry.MetadataRevision, storage = entry.StorageRevision;
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
            if (variant == 8) GL.ActiveTexture(TextureUnit.Texture5);
            OpenGlStateCache.Current.InvalidateAll();
            // Act: Apply one represented parameter through the selected native overload.
            SetRange(variant, texture.TextureId);
            // Assert: The canonical range advances metadata once without changing allocation identity or active unit.
            Assert.Equal(1, entry.Metadata.BaseLevel);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(storage, entry.StorageRevision);
            Assert.True(RHIResourceManagerSystem.CurrentManager.IsResourceMetadataAvailable(alias));
            Assert.Equal((int)(variant == 8 ? TextureUnit.Texture5 : TextureUnit.Texture3), GL.GetInteger(GetPName.ActiveTexture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Core content and sampler policy perform neither object-selector recovery nor metadata readback.</summary>
    [Fact]
    public void CoreContentsAndFilterPerformNoObservation()
    {
        // Arrange: Instrument descriptor reads and metadata acquisition for a registered, bound texture.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch(nameof(UploadContents));
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture.TextureId);
            OpenGlStateCache.Current.InvalidateAll();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(texture));
            ulong revision = entry.MetadataRevision;
            using var descriptors = new ImageDescriptorCapture(native =>
            {
                var original = (delegate* unmanaged[Stdcall]<int, int*, void>)native.Original("glGetIntegerv");
                native.Add("glGetIntegerv", new IntegerQuery((parameter, value) =>
                {
                    original(parameter, value);
                    observations++;
                }));
            });
            entry.Reader = new CountingReader();
            observations = 0;
            // Act: Upload one pixel and change filter policy through core calls.
            UploadContents(0);
            // Assert: Content and sampler changes do not trigger selector discovery or metadata publication.
            Assert.Equal(0, observations);
            Assert.Equal(0, descriptors.Reads);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion

    #region Private
    /// <summary>Installs the production transpiler while preserving each exact native signature.</summary>
    private static Harmony Patch(string method)
    {
        var harmony = new Harmony("VGE.Tests.TextureParameter." + method);
        return harmony;
    }

    /// <summary>Exercises scalar, vector, reference, unsigned-name and explicit-unit forms of represented parameters.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetRange(int variant, int id)
    {
        int signed = 1;
        uint unsigned = 1;
        switch (variant)
        {
            case 0: GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 1); break;
            case 1: GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 1f); break;
            case 2: GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, new float[] { 1 }); break;
            case 3: GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, ref signed); break;
            case 4: GL.TexParameterI(TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, ref unsigned); break;
            case 5: GL.TextureParameter((uint)id, TextureParameterName.TextureBaseLevel, 1); break;
            case 6: GL.TextureParameterI((uint)id, TextureParameterName.TextureBaseLevel, new uint[] { 1 }); break;
            case 7: GL.Ext.TextureParameter(id, TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 1); break;
            case 8: GL.Ext.MultiTexParameter(TextureUnit.Texture3, TextureTarget.Texture2D, TextureParameterName.TextureBaseLevel, 1); break;
        }
    }

    /// <summary>Writes pixels and independent sampler policy without changing explicit storage metadata.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UploadContents(int level)
    {
        GL.TexSubImage2D(TextureTarget.Texture2D, level, 0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 1, 2, 3, 4 });
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
    }


    /// <summary>Counts metadata acquisition while delegating reads to the production texture reader.</summary>
    private sealed class CountingReader : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        #region Public API

        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata metadata)
        {
            observations++;
            return GpuTextureMetadataReader.Instance.TryRead(id, out metadata);
        }
        #endregion
    }
    #endregion
}
