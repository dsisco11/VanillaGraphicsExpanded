using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies initial canonical acquisition establishes known native buffer-texture dependencies.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class GpuInitialTextureDependencyTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Maintained association uses its original backing and rejects unavailable backing facts.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void InitialAcquisitionAdmitsBacking(bool unavailableBacking)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); int buffer=GL.GenBuffer(),texture=GL.GenTexture();
        try
        {
            GL.BindBuffer(BufferTarget.TextureBuffer,buffer); GL.BufferData(BufferTarget.TextureBuffer,64,IntPtr.Zero,BufferUsageHint.DynamicDraw);
            GL.BindTexture(TextureTarget.TextureBuffer,texture); GL.TexBuffer(TextureBufferTarget.TextureBuffer,SizedInternalFormat.R32f,buffer);
            var manager=GpuResourceManagerSystem.CurrentManager!;
            var reader=new KnownAssociationReader(buffer); var entry=manager.AcquireResource(GpuResourceKind.Texture,texture,reader)!;
            Assert.Equal(0, reader.Reads);
            Assert.True(entry.IsUsable); Assert.True(manager.FindResource(GpuResourceKind.Buffer,buffer)!.IsUsable);
            using var alias=new Alias(texture); GpuResourceManager.AcquireWrapperMetadata(alias,reader); Assert.True(manager.IsResourceMetadataAvailable(alias));
            if (unavailableBacking)
            {
                var backing = manager.AcquireResource(GpuResourceKind.Buffer,buffer,new GpuBufferMetadataReader())!;
                backing.BeginRefresh(GpuMetadataChange.Storage);
                Assert.False(manager.IsResourceMetadataAvailable(alias));
                Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, buffer, new GpuBufferMetadataReader()));
                Assert.NotNull(manager.RecoverResource(GpuResourceKind.Buffer, buffer, new GpuBufferMetadataReader()));
                Assert.True(manager.IsResourceMetadataAvailable(alias));
            }
            ulong revision=entry.StorageRevision;
            GL.NamedBufferData(buffer,128,IntPtr.Zero,BufferUsageHint.DynamicDraw);
            Assert.True(manager.IsResourceMetadataAvailable(alias));
            Assert.NotNull(manager.AcquireResource(GpuResourceKind.Buffer,buffer,new GpuBufferMetadataReader()));
            Assert.True(entry.StorageRevision>revision); Assert.True(manager.IsResourceMetadataAvailable(alias)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture)); RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, buffer)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Ordinary acquisition cannot replace a maintained empty association with reader-supplied facts.</summary>
    [Fact]
    public void UnavailableBackingPreventsInitialPublication()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); int texture=GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.TextureBuffer,texture);
            var manager=GpuResourceManagerSystem.CurrentManager!;
            var reader = new KnownAssociationReader(int.MaxValue);
            var entry = manager.AcquireResource(GpuResourceKind.Texture,texture,reader)!;
            Assert.Equal(0, reader.Reads);
            Assert.Null(entry.Metadata.Buffer);
            Assert.True(entry.IsUsable);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture)); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Supplies the whole-buffer association proven by this fixture's native creation; general external mode discovery is not claimed.</summary>
    private sealed class KnownAssociationReader(int buffer):IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        internal int Reads { get; private set; }
        /// <inheritdoc />
        public bool IsResource(nint id)=>GL.IsTexture((int)id);
        /// <inheritdoc />
        public bool TryRead(nint id,out GpuTextureMetadata value)
        { Reads++; value=new(TextureDimension.Buffer,ImmutableArray<GpuTextureLevelMetadata>.Empty,false,0,0,0,new(buffer,TextureFormat.R32f,0,0,true)); return true; }
    }
    /// <summary>Receives the acquired snapshot without taking texture ownership.</summary>
    private sealed class Alias(int id):GpuResource<GpuTextureMetadata>
    {
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }=id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind=>GpuResourceKind.Texture;
        /// <inheritdoc />
        protected override bool OwnsResource=>false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}

