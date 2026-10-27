using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises native view creation on reserved tracked names without prematurely realizing texture objects.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuTextureViewInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Native view creation publishes actual relative levels and layers to all aliases and leaves the source entry intact.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReservedViewCreationPublishesAliases(bool unsigned)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        GL.CreateTextures(TextureTarget.Texture2DArray, 1, out int sourceId);
        int viewId = GL.GenTexture();
        try
        {
            GL.TextureStorage3D(sourceId, 4, SizedInternalFormat.Rgba8, 8, 8, 4);
            using var source = new BorrowedTexture(sourceId);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var sourceEntry = manager.GetResourceRegistration(source)!;
            ulong sourceRevision = sourceEntry.MetadataRevision, sourceStorage = sourceEntry.StorageRevision;
            var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, viewId);
            using var first = new TextureAlias(viewId);
            using var second = new TextureAlias(viewId);
            manager.AssociateResource(entry, first);
            manager.AssociateResource(entry, second);
            Assert.False(GL.IsTexture(viewId));
            Assert.False(manager.IsResourceMetadataAvailable(first));
            DefineView(unsigned, viewId, sourceId);
            Assert.True(GL.IsTexture(viewId));
            Assert.True(manager.IsResourceMetadataAvailable(first));
            Assert.True(manager.IsResourceMetadataAvailable(second));
            Assert.Equal(new GpuTextureViewMetadata(1, 2, 1, 2), first.Snapshot.View);
            Assert.Equal(first.Snapshot, second.Snapshot);
            Assert.Equal(2, first.Snapshot.Levels.Length);
            // Explicit acquisition uses named baseline queries while preserving source-relative facts.
            {
                Assert.True(new GpuTextureMetadataReader(TextureTarget.Texture2DArray).TryRead(viewId, out var boundSnapshot));
                Assert.Equal(first.Snapshot.View, boundSnapshot.View);
                Assert.Equal(first.Snapshot.Levels.ToArray(), boundSnapshot.Levels.ToArray());
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
            Assert.Contains(first.Snapshot.Levels, level => level.Level == 0 && level.Width == 4 && level.Depth == 2);
            Assert.Contains(first.Snapshot.Levels, level => level.Level == 1 && level.Width == 2 && level.Depth == 2);
            Assert.Same(sourceEntry, manager.GetResourceRegistration(source));
            Assert.Equal(sourceRevision, sourceEntry.MetadataRevision);
            Assert.Equal(sourceStorage, sourceEntry.StorageRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            var original = first.Snapshot;
            DefineView(unsigned, viewId, sourceId);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(original.View, first.Snapshot.View);
            Assert.Equal(original.Levels.ToArray(), first.Snapshot.Levels.ToArray());
            Assert.True(manager.IsResourceMetadataAvailable(first));
            Assert.Equal(sourceRevision, sourceEntry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, viewId));
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, sourceId));
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>A rejected native redefinition preserves a managed view's real range and original source dependency.</summary>
    [Fact]
    public void ManagedViewRejectsRedefinitionWithoutInventedRange()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = Patch();
        int sourceId = 0;
        try
        {
            GL.CreateTextures(TextureTarget.Texture2DArray, 1, out sourceId);
            GL.TextureStorage3D(sourceId, 4, SizedInternalFormat.Rgba8, 8, 8, 4);
            using var source = new BorrowedTexture(sourceId);
            using var view = RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = source, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 2, Label = null }).RequireResource();
            var sourceEntry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(source)!;
            ulong sourceRevision = sourceEntry.MetadataRevision;
            DefineView(false, view.TextureId, source.TextureId);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(0, view.BaseLevel);
            Assert.Equal(1, view.LevelCount);
            Assert.Equal(0, view.BaseLayer);
            Assert.Equal(2, view.LayerCount);
            Assert.True(view.IsValid);
            Assert.Equal(sourceRevision, sourceEntry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, sourceId)); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Installs production call-site redirection on the native view-definition boundary.</summary>
    private static Harmony Patch()
    {
        var harmony = new Harmony("VGE.Tests.TextureViewInterception");
        harmony.Patch(AccessTools.Method(typeof(GpuTextureViewInterceptionTests), nameof(DefineView)),
            transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        return harmony;
    }

    /// <summary>Defines the same nontrivial view range through signed and unsigned native overloads.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefineView(bool unsigned, int view, int source)
    {
        if (unsigned) GL.TextureView((uint)view, TextureTarget.Texture2DArray, (uint)source, PixelInternalFormat.Rgba8, 1u, 2u, 1u, 2u);
        else GL.TextureView(view, TextureTarget.Texture2DArray, source, PixelInternalFormat.Rgba8, 1, 2, 1, 2);
    }

    /// <summary>Receives metadata for a reserved native name without owning or realizing it.</summary>
    private sealed class TextureAlias : GpuResource<GpuTextureMetadata>
    {
        /// <summary>Retains only the generated identity before native view construction.</summary>
        internal TextureAlias(int id) { ResourceId = id; }
        /// <summary>Exposes the manager-delivered snapshot for native-result assertions.</summary>
        internal GpuTextureMetadata Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }
    #endregion
}








