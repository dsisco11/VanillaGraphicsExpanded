using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks EXT renderbuffer texture associations and their canonical backing dependencies.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class GpuRenderbufferTextureMetadataTests(HeadlessGLFixture fixture,ITestOutputHelper output)
{
    private const TextureTarget Target=(TextureTarget)All.TextureRenderbufferNv;
    #region Public API
    /// <summary>Exact named and explicit-unit commands publish aliases and follow backing storage and deletion.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void NativeAssociationTracksBackingLifetime(bool multi)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        bool supported=GpuSupport.Graphics.SupportsRenderbufferTextures;
        bool ext=Enumerable.Range(0,GL.GetInteger(GetPName.NumExtensions)).Any(index=>GL.GetString(StringNameIndexed.Extensions,index)=="GL_EXT_direct_state_access");
        output.WriteLine($"Renderbuffer texture capability={supported}; EXT_DSA={ext}"); Assert.SkipUnless(supported&&ext,"Renderbuffer texture and EXT DSA capabilities required.");
        var harmony=new Harmony("VGE.Tests.RenderbufferTexture"); harmony.Patch(AccessTools.Method(typeof(GpuRenderbufferTextureMetadataTests),nameof(Associate)),transpiler:new HarmonyMethod(typeof(EngineStateSwitchingHook),nameof(EngineStateSwitchingHook.Transpiler)));
        int texture=GL.GenTexture(),first=GL.GenRenderbuffer(),second=GL.GenRenderbuffer();
        try
        {
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer,first); GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,RenderbufferStorage.Rgba8,4,4);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer,second); GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,RenderbufferStorage.Rgba8,8,8);
            GL.ActiveTexture(TextureUnit.Texture3); GL.BindTexture(Target,texture); GL.Ext.TextureRenderbuffer(texture,Target,first); Assert.Equal(ErrorCode.NoError,GL.GetError()); StateCache.Current.InvalidateAll();
            var manager=GpuResourceManagerSystem.CurrentManager!; var reader=new GpuTextureMetadataReader(Target);
            var entry=manager.AcquireResource(GpuResourceKind.Texture,texture,reader)!; Assert.True(entry.IsUsable); Assert.Equal(first,entry.Metadata.Renderbuffer); Assert.Empty(entry.Metadata.Levels);
            Assert.True(manager.FindResource(GpuResourceKind.Renderbuffer,first)!.IsUsable);
            using var alias=new TextureAlias(texture); GpuResourceManager.AcquireWrapperMetadata(alias,reader);
            GL.ActiveTexture(TextureUnit.Texture5); StateCache.Current.InvalidateAll(); ulong revision=entry.StorageRevision;
            Associate(multi,texture,second); Assert.Equal(ErrorCode.NoError,GL.GetError());
            Assert.Equal((int)TextureUnit.Texture5,GL.GetInteger(GetPName.ActiveTexture)); Assert.Equal(second,alias.Snapshot.Renderbuffer); Assert.True(entry.StorageRevision>revision); Assert.True(manager.IsResourceMetadataAvailable(alias));
            revision=entry.StorageRevision;
            var token=StateCache.BeginNativeRenderbufferStorage(second,true);
            GL.NamedRenderbufferStorage(second,RenderbufferStorage.Rgba8,16,16); token.Complete(true);
            Assert.True(entry.StorageRevision>revision);
            Associate(multi,texture,int.MaxValue); Assert.Equal(ErrorCode.InvalidOperation,GL.GetError()); Assert.Equal(second,alias.Snapshot.Renderbuffer);
            var backingMetadata=((GpuResourceMetadataEntry<GpuRenderbufferMetadata>)manager.FindResource(GpuResourceKind.Renderbuffer,second)!).Metadata;
            RHICommandContext.Current.OpenGL.DeleteRenderbuffer(second); Assert.False(manager.IsResourceMetadataAvailable(alias));
            var replacement=manager.RegisterCreatedResource<GpuRenderbufferMetadata>(GpuResourceKind.Renderbuffer,second);
            manager.PublishMetadata(replacement,backingMetadata,GpuMetadataChange.Storage);
            Assert.Throws<InvalidOperationException>(()=>manager.RefreshMetadata(entry,GpuMetadataChange.None));
            Assert.Throws<InvalidOperationException>(()=>manager.PublishMetadata(entry,entry.Metadata,GpuMetadataChange.None));
            Assert.False(manager.IsResourceMetadataAvailable(alias));
            Associate(multi,texture,first);
            Assert.True(manager.IsResourceMetadataAvailable(alias)); Assert.Equal(first,alias.Snapshot.Renderbuffer);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); RHICommandContext.Current.OpenGL.DeleteTexture(texture); RHICommandContext.Current.OpenGL.DeleteRenderbuffer(first); RHICommandContext.Current.OpenGL.DeleteRenderbuffer(second); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Leaves actual EXT named and explicit-unit signatures for production redirection.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Associate(bool multi,int texture,int renderbuffer)
    { if(multi) GL.Ext.MultiTexRenderbuffer(TextureUnit.Texture3,Target,renderbuffer); else GL.Ext.TextureRenderbuffer(texture,Target,renderbuffer); }
    /// <summary>Receives canonical associations without pretending this target has ordinary texture mip images.</summary>
    private sealed class TextureAlias(int id):GpuResource<GpuTextureMetadata>
    {
        internal GpuTextureMetadata Snapshot=>metadata;
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


