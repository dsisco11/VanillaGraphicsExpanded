using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes real engine-call rewriting across buffer allocation addressing and managed upload signatures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuBufferMetadataInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Generated names acquire a complete reader on their first redirected allocation.</summary>
    [Fact]
    public void FirstBoundAllocationPublishesReservedName()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("VGE.Tests.BufferMetadata.FirstAllocation");
        int reserved = GL.GenBuffer();
        try
        {
            harmony.Patch(AccessTools.Method(typeof(GpuBufferMetadataInterceptionTests), nameof(Allocate)),
                transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            harmony.Patch(AccessTools.Method(typeof(GpuBufferMetadataInterceptionTests), nameof(BindReserved)),
                transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            var manager = GpuResourceManagerSystem.CurrentManager!;
            Assert.False(GL.IsBuffer(reserved));
            BindReserved(reserved);
            Assert.True(GL.IsBuffer(reserved));
            Allocate(48);
            var entry = manager.AcquireResource(GpuResourceKind.Buffer, reserved, new GpuBufferMetadataReader())!;
            using var alias = new BufferAlias(reserved);
            manager.AssociateResource(entry, alias);
            Assert.Equal((nint)48, alias.Capacity);
            Assert.NotNull(entry.Reader);
            Assert.True(manager.IsResourceMetadataAvailable(alias));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            OpenGLBufferDeletionCalls.Core.DeleteBuffer(reserved);
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Method specification discovery identifies a caller containing only a closed generic native operation.</summary>
    [Fact]
    public void GenericOnlyCallerIsDiscovered()
    {
        var target = AccessTools.Method(typeof(GpuBufferMetadataInterceptionTests), nameof(GenericUpload));
        Assert.Contains(target, EngineStateTargets.Discover(typeof(GpuBufferMetadataInterceptionTests).Assembly));
    }

    /// <summary>Immutable storage commands publish actual size through bound and explicit-object addressing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImmutableStoragePublishes(bool named)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = new Harmony($"VGE.Tests.BufferMetadata.Immutable.{named}");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(GpuBufferMetadataInterceptionTests), nameof(AllocateStorage)),
                transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, owner.BufferId);
            AllocateStorage(named, owner.BufferId);
            AcquireAfterExternal(owner);
            Assert.Equal(96, owner.SizeBytes);
            Assert.True(GpuResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(owner));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            // Immutable storage cannot be replaced; a rejected command must preserve its measured facts and error.
            AllocateStorage(named, owner.BufferId);
            AcquireAfterExternal(owner);
            Assert.Equal(96, owner.SizeBytes);
            Assert.True(GpuResourceManagerSystem.CurrentManager.IsResourceMetadataAvailable(owner));
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Each rewritten overload publishes actual allocation facts to every alias exactly once.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void NativeOverloadsPublishAliases(int variant)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = new Harmony($"VGE.Tests.BufferMetadata.{variant}");
        try
        {
            var caller = AccessTools.Method(typeof(GpuBufferMetadataInterceptionTests), nameof(Upload));
            harmony.Patch(caller, transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            owner.Allocate(64);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            using var alias = new BufferAlias(owner.BufferId);
            manager.AssociateResource(entry, alias);
            // Force target resolution through native unknown-state recovery instead of cached selection.
            GL.BindBuffer(BufferTarget.ArrayBuffer, owner.BufferId);
            OpenGlStateCache.Current.InvalidateAll();
            ulong revision = entry.MetadataRevision, storage = entry.StorageRevision;
            Upload(variant, owner.BufferId);
            AcquireAfterExternal(owner);
            Assert.Equal(16, owner.SizeBytes);
            Assert.Equal((nint)16, alias.Capacity);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.Same(entry, manager.GetResourceRegistration(owner));
            Assert.Equal(owner.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Rejected allocation retains proven capacity and its native error, while raw objects remain unregistered.</summary>
    [Fact]
    public void RejectedAndUntrackedCallsPreserveContracts()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = new Harmony("VGE.Tests.BufferMetadata.Rejected");
        int raw = 0;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(GpuBufferMetadataInterceptionTests), nameof(Allocate)),
                transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            owner.Allocate(64);
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, owner.BufferId);
            Allocate(-1);
            AcquireAfterExternal(owner);
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            raw = GL.GenBuffer();
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, raw);
            Allocate(24);
            Assert.Null(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Buffer, raw));
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int size);
            Assert.Equal(24, size);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            if (raw != 0) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, raw));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Models a consumer explicitly acquiring the allocation withdrawn by an external command.</summary>
    private static void AcquireAfterExternal(GpuBufferObject owner)
    {
        var manager = GpuResourceManagerSystem.CurrentManager!;
        Assert.False(manager.IsResourceMetadataAvailable(owner));
        Assert.NotNull(manager.AcquireResource(GpuResourceKind.Buffer, owner.BufferId, new GpuBufferMetadataReader()));
    }

    /// <summary>Isolates a generic native member reference for discovery through its MethodSpecification token.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GenericUpload(Payload[] values) => GL.BufferData(BufferTarget.ArrayBuffer, 16, values, BufferUsageHint.StaticDraw);

    /// <summary>Dispatches immutable allocation without going through an owning wrapper helper.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AllocateStorage(bool named, int id)
    {
        if (named) GL.NamedBufferStorage(id, 96, IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
        else GL.BufferStorage(BufferTarget.ArrayBuffer, 96, IntPtr.Zero, BufferStorageFlags.DynamicStorageBit);
    }

    /// <summary>Provides distinct closed managed signatures for the real Harmony call-site adapter.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Upload(int variant, int id)
    {
        var value = new Payload { A = 1, B = 2, C = 3, D = 4 };
        switch (variant)
        {
            case 0: GL.BufferData(BufferTarget.ArrayBuffer, (IntPtr)16, IntPtr.Zero, BufferUsageHint.DynamicDraw); break;
            case 1: GL.BufferData(BufferTarget.ArrayBuffer, 16, new[] { value }, BufferUsageHint.DynamicDraw); break;
            case 2: GL.BufferData(BufferTarget.ArrayBuffer, 16, ref value, BufferUsageHint.DynamicDraw); break;
            case 3: GL.BufferData(BufferTarget.ArrayBuffer, 16, new Payload[,] { { value } }, BufferUsageHint.DynamicDraw); break;
            case 4: GL.NamedBufferData(id, 16, IntPtr.Zero, BufferUsageHint.DynamicDraw); break;
            case 5: GL.NamedBufferData((uint)id, (IntPtr)16, new[] { value }, BufferUsageHint.DynamicDraw); break;
            case 6: GL.Arb.BufferData(BufferTargetArb.ArrayBuffer, 16, new[] { value }, BufferUsageArb.DynamicDraw); break;
            case 7: GL.Ext.NamedBufferData(id, 16, IntPtr.Zero, BufferUsageHint.DynamicDraw); break;
        }
    }

    /// <summary>Realizes a reserved name through the exact external binding boundary.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BindReserved(int id) => GL.BindBuffer(BufferTarget.ArrayBuffer, id);

    /// <summary>Preserves a native invalid-size call that the public owning API would reject before dispatch.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Allocate(int size) => GL.BufferData(BufferTarget.ArrayBuffer, size, IntPtr.Zero, BufferUsageHint.StaticDraw);

    /// <summary>Provides a caller-defined unmanaged element type for generic overload closure.</summary>
    private struct Payload { internal int A, B, C, D; }

    /// <summary>Receives shared snapshots without taking deletion ownership.</summary>
    private sealed class BufferAlias : GpuResource<GpuBufferMetadata>
    {
        /// <summary>Retains the existing allocation identity.</summary>
        internal BufferAlias(int id) { ResourceId = id; }
        internal nint Capacity => metadata.Capacity;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }
    #endregion
}
