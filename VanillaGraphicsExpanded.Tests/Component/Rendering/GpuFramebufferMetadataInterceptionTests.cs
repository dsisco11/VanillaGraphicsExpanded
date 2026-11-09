using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks native framebuffer descriptor publication and lazy helper reconciliation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuFramebufferMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A foreign context cannot publish framebuffer metadata or invoke helpers owned by the manager.</summary>
    [Fact]
    public void ObservationInDifferentContextCannotNotifyHelpers()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        var origin=RenderContextRegistry.Current();
        var current=origin;
        using var manager=new RHIResourceManager(() => current, static _ => true);
        RHIResourceManagerSystem.Initialize(manager);
        // Act
        try
        {
            using var texture=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var image=GpuFramebufferAttachment.FromTexture(texture);
            using var framebuffer=DynamicGpuFramebuffer.Create([image])!;
            var entry=(GpuResourceMetadataEntry<GpuFramebufferMetadata>)manager.GetResourceRegistration(framebuffer)!;
            var reader=new CountingFramebufferReader(entry.Reader!); entry.Reader=reader;
            int callbacks=0; framebuffer.AttachmentsChanged+=() => callbacks++;
            ulong revision=entry.MetadataRevision;
            try
            {
                current=(origin.Item1,origin.Item2+1);
                OpenGlStateCache.ObserveNamedFramebufferParameteri((uint)framebuffer.FboId, (int)FramebufferDefaultParameter.FramebufferDefaultWidth, 4);
                // Assert
                Assert.Equal(0,callbacks); Assert.Equal(0,reader.Reads);
                Assert.Equal(revision,entry.MetadataRevision); Assert.True(entry.MetadataAvailable);
            }
            finally { current=origin; }
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { manager.Dispose(); RHIResourceManagerSystem.Shutdown(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Detects whether context rejection occurs before native framebuffer metadata acquisition.</summary>
    private sealed class CountingFramebufferReader(IGpuResourceMetadataReader<GpuFramebufferMetadata> inner) : IGpuResourceMetadataReader<GpuFramebufferMetadata>
    {
        internal int Reads;
        #region Public API
        /// <inheritdoc />
        public bool TryRead(nint id,out GpuFramebufferMetadata value) { Reads++; return inner.TryRead(id,out value); }
        #endregion
    }
    #endregion
}




