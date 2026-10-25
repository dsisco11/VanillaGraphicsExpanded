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
    /// <summary>First texture admission acquires or recovers its real backing before publishing an available snapshot.</summary>
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
            if(unavailableBacking) { var old=manager.AcquireResource(GpuResourceKind.Buffer,buffer,new GpuBufferMetadataReader())!; old.BeginRefresh(GpuMetadataChange.Storage); Assert.False(old.IsUsable); }
            var reader=new KnownAssociationReader(buffer); var entry=manager.AcquireResource(GpuResourceKind.Texture,texture,reader)!;
            Assert.True(entry.IsUsable); Assert.True(manager.FindResource(GpuResourceKind.Buffer,buffer)!.IsUsable);
            using var alias=new Alias(texture); GpuResourceManager.AcquireWrapperMetadata(alias,reader); Assert.True(manager.IsResourceMetadataAvailable(alias));
            ulong revision=entry.StorageRevision;
            var operation=StateCache.InvalidateNativeBufferStorage(buffer,true);
            try { GL.NamedBufferData(buffer,128,IntPtr.Zero,BufferUsageHint.DynamicDraw); }
            finally { operation.Complete(); }
            Assert.False(manager.IsResourceMetadataAvailable(alias));
            Assert.NotNull(manager.AcquireResource(GpuResourceKind.Buffer,buffer,new GpuBufferMetadataReader()));
            Assert.True(entry.StorageRevision>revision); Assert.True(manager.IsResourceMetadataAvailable(alias)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { StateCache.Current.DeleteTexture(texture); StateCache.Current.DeleteBuffer(buffer); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>A failed association acquisition cannot publish a texture whose reported backing is nonexistent.</summary>
    [Fact]
    public void UnavailableBackingPreventsInitialPublication()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); int texture=GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.TextureBuffer,texture);
            var manager=GpuResourceManagerSystem.CurrentManager!;
            Assert.Throws<InvalidOperationException>(()=>manager.AcquireResource(GpuResourceKind.Texture,texture,new KnownAssociationReader(int.MaxValue)));
            // Failed initial admission removes its provisional identity rather than retaining unusable publication.
            Assert.Null(manager.FindResource(GpuResourceKind.Texture,texture));
            // Invalid-object admission is rejected before any descriptor query.
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { StateCache.Current.DeleteTexture(texture); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Supplies the whole-buffer association proven by this fixture's native creation; general external mode discovery is not claimed.</summary>
    private sealed class KnownAssociationReader(int buffer):IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id)=>GL.IsTexture((int)id);
        /// <inheritdoc />
        public bool TryRead(nint id,out GpuTextureMetadata value)
        { value=new(TextureTarget.TextureBuffer,ImmutableArray<GpuTextureLevelMetadata>.Empty,false,0,0,0,new(buffer,SizedInternalFormat.R32f,0,0,true)); return true; }
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

