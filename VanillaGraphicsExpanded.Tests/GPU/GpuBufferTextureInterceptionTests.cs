using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises exact native buffer texture association dispatch and canonical range certification.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuBufferTextureInterceptionTests(HeadlessGLFixture fixture)
{
    private static int reads;
    private static nint capturedOffset, capturedSize;
    #region Public API
    /// <summary>Supported addressing forms update aliases and preserve explicit whole-versus-range policy.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void AssociationVariantsPublishActualDescriptors(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var first = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); first.Allocate(128);
            using var second = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); second.Allocate(128);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = first, Format = RHIFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            using var sentinel = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = first, Format = RHIFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = (GpuResourceMetadataEntry<GpuTextureMetadata>)manager.GetResourceRegistration(view)!;
            using var alias = new TextureAlias(view.TextureId); manager.AssociateResource(entry, alias);
            // Invalidate cached selection while retaining actual native binding to exercise resolution.
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.TextureBuffer, 0, sentinel.TextureId);
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.TextureBuffer, 3, view.TextureId);
            OpenGLImageBindings.ActiveTexture(StateCache.Current, variant == 6 ? 5 : 3);
            StateCache.Current.InvalidateAll();

            Associate(variant, view.TextureId, second.BufferId, SizedInternalFormat.R32ui, 0, 128);
            bool ranged = variant is 7 or 8 or 9 or 10;
            Assert.Equal(second.BufferId, view.BufferId);
            Assert.Equal(SizedInternalFormat.R32ui, view.Format);
            Assert.Equal(ranged ? 128 : 0, view.SizeBytes);
            Assert.Equal(!ranged, alias.Snapshot.Buffer!.Value.WholeBuffer);
            Assert.Equal(view.BufferId, alias.Snapshot.Buffer.Value.BufferId);
            Assert.True(view.IsValid);
            Assert.Equal((int)(variant == 6 ? TextureUnit.Texture5 : TextureUnit.Texture3), GL.GetInteger(GetPName.ActiveTexture));
            OpenGLImageBindings.ActiveTexture(StateCache.Current, 0);
            Assert.Equal(sentinel.TextureId, GL.GetInteger(GetPName.TextureBindingBuffer));
            second.Allocate(256);
            Assert.Equal(ranged ? 128 : 0, view.SizeBytes);
            Assert.True(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Rejected commands preserve certified descriptors and native errors; a valid detach removes the backing.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void RejectedRequestsRetainDescriptorAndDetachIsObserved(int rejection)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(128);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = RHIFormat.R32f, WholeBuffer = false, Offset = 0, Length = 64, Label = null }).RequireResource();
            var entry = (GpuResourceMetadataEntry<GpuTextureMetadata>)GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(view)!;
            if (rejection == 4) Associate(8, view.TextureId, buffer.BufferId, SizedInternalFormat.R32f, 0, 128);
            var original = entry.Metadata.Buffer;
            if (rejection == 0) Associate(8, view.TextureId, buffer.BufferId, SizedInternalFormat.R32f, 0, 256);
            else if (rejection == 1) Associate(8, view.TextureId, buffer.BufferId, SizedInternalFormat.R32f, 1, 16);
            else if (rejection == 2) InvalidTarget(view.TextureId, buffer.BufferId, TextureTarget.Texture2D);
            else if (rejection == 4) InvalidTarget(view.TextureId, buffer.BufferId, 0);
            else Associate(1, view.TextureId, buffer.BufferId, (SizedInternalFormat)0, 0, 0);
            Assert.NotEqual(ErrorCode.NoError, GL.GetError());
            Assert.Equal(original, entry.Metadata.Buffer);
            Assert.True(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Associate(1, view.TextureId, 0, SizedInternalFormat.R32f, 0, 0);
            Assert.Equal(0, view.BufferId);
            Assert.True(GpuResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(view));
            Assert.False(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Whole and explicit equal-capacity association modes remain distinct through resizing and reassociation.</summary>
    [Fact]
    public void WholeAndExplicitFullRangeDoNotCollapse()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(128);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = RHIFormat.R32f, WholeBuffer = true, Label = null }).RequireResource();
            Associate(8, view.TextureId, buffer.BufferId, SizedInternalFormat.R32f, 0, 128);
            Assert.Equal(128, view.SizeBytes);
            buffer.Allocate(256);
            Assert.Equal(128, view.SizeBytes);
            Associate(1, view.TextureId, buffer.BufferId, SizedInternalFormat.R32f, 0, 0);
            Assert.Equal(0, view.SizeBytes);
            buffer.Allocate(64);
            Assert.Equal(0, view.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Untracked commands forward wide ranges unchanged without metadata queries or speculative registration.</summary>
    [Fact]
    public void UntrackedWideForwardingDoesNotObserveOrNarrow()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch();
        GL.CreateTextures(TextureTarget.TextureBuffer, 1, out int texture);
        try
        {
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.GetTextureLevelParameter), [typeof(int), typeof(int), typeof(GetTextureParameter), typeof(int).MakeByRefType()]),
                prefix: new HarmonyMethod(typeof(GpuBufferTextureInterceptionTests), nameof(CountRead)));
            using var backing = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();
            backing.Allocate(64);
            reads = 0;
            Associate(1, texture, backing.BufferId, SizedInternalFormat.R32f, 0, 0);
            Assert.Equal(0, reads);
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.TextureBufferRange), [typeof(int), typeof(SizedInternalFormat), typeof(int), typeof(IntPtr), typeof(IntPtr)]),
                prefix: new HarmonyMethod(typeof(GpuBufferTextureInterceptionTests), nameof(CaptureWide)));
            reads = 0;
            nint offset = (nint)int.MaxValue + 4097, size = (nint)int.MaxValue + 8193;
            Associate(8, texture, 123, SizedInternalFormat.R32f, offset, size);
            Assert.Equal(offset, capturedOffset); Assert.Equal(size, capturedSize);
            Assert.Equal(0, reads);
            Assert.Null(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); RHICommandContext.Current.OpenGL.DeleteTexture(texture); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>A native dispatch exception preserves the original failure and withdraws the unchanged association snapshot.</summary>
    [Fact]
    public void ThrowingNativeDispatchCannotRecertifyPreviousRange()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(128);
            using var view = RHICommandContext.Current.CreateBufferTexture(new RHIBufferTextureDescription { Buffer = buffer, Format = RHIFormat.R32f, WholeBuffer = false, Offset = 0, Length = 64, Label = null }).RequireResource();
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.TextureBufferRange), [typeof(int), typeof(SizedInternalFormat), typeof(int), typeof(IntPtr), typeof(IntPtr)]),
                prefix: new HarmonyMethod(typeof(GpuBufferTextureInterceptionTests), nameof(ThrowBeforeDispatch)));
            var failure = Assert.Throws<ApplicationException>(() => Associate(8, view.TextureId, buffer.BufferId, SizedInternalFormat.R32f, 0, 64));
            Assert.Equal("native boundary failure", failure.Message);
            Assert.False(GpuResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(view));
            Assert.False(view.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Installs real production redirection on native fixture call sites.</summary>
    private static Harmony Patch()
    {
        var harmony = new Harmony("VGE.Tests.BufferTextureInterception");
        foreach (string name in new[] { nameof(Associate), nameof(InvalidTarget) })
            harmony.Patch(AccessTools.Method(typeof(GpuBufferTextureInterceptionTests), name),
                transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        return harmony;
    }
    /// <summary>Dispatches actual core and extension overloads without modifying their native signatures.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Associate(int variant, int texture, int buffer, SizedInternalFormat format, nint offset, nint size)
    {
        switch (variant)
        {
            case 0: GL.TexBuffer(TextureBufferTarget.TextureBuffer, format, buffer); break;
            case 1: GL.TextureBuffer(texture, format, buffer); break;
            case 2: GL.TextureBuffer((uint)texture, format, (uint)buffer); break;
            case 3: GL.Arb.TexBuffer(TextureTarget.TextureBuffer, format, buffer); break;
            case 4: GL.Ext.TexBuffer(TextureTarget.TextureBuffer, format, buffer); break;
            case 5: GL.Ext.TextureBuffer(texture, TextureTarget.TextureBuffer, format, buffer); break;
            case 6: GL.Ext.MultiTexBuffer(TextureUnit.Texture3, TextureTarget.TextureBuffer, (InternalFormat)format, buffer); break;
            case 7: GL.TexBufferRange(TextureBufferTarget.TextureBuffer, format, buffer, offset, size); break;
            case 8: GL.TextureBufferRange(texture, format, buffer, offset, size); break;
            case 9: GL.TextureBufferRange((uint)texture, format, (uint)buffer, offset, size); break;
            case 10: GL.Ext.TextureBufferRange(texture, TextureTarget.TextureBuffer, (InternalFormat)format, buffer, offset, size); break;
            case 11: GL.TexBuffer(TextureBufferTarget.TextureBuffer, format, buffer); break;
        }
    }
    /// <summary>Retains an invalid target as a native error without resolving another binding.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvalidTarget(int texture, int buffer, TextureTarget target) => GL.Ext.TextureBuffer(texture, target, SizedInternalFormat.R32f, buffer);
    /// <summary>Injects a dispatch failure before any native association command executes.</summary>
    private static void ThrowBeforeDispatch() => throw new ApplicationException("native boundary failure");
    /// <summary>Counts native metadata readback on the untracked forwarding path.</summary>
    private static void CountRead() => reads++;
    /// <summary>Captures a native-width boundary without submitting an unsupported allocation to the GPU.</summary>
    private static bool CaptureWide(IntPtr __3, IntPtr __4) { capturedOffset = __3; capturedSize = __4; return false; }
    /// <summary>Receives canonical association metadata without taking ownership.</summary>
    private sealed class TextureAlias(int id) : GpuResource<GpuTextureMetadata>
    {
        internal GpuTextureMetadata Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}




