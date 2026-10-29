using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies optional semaphore delivery without claiming real external-producer execution.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuSemaphoreLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Invalid untracked native imports still reach the original once without fatal debug assertions.</summary>
    [Fact]
    public void UntrackedInvalidImportRemainsAnOrdinaryNativeCall()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var driver = new SemaphoreDriver();
        GL.Ext.ImportSemaphoreF(SemaphoreDriver.Name, ExternalHandleType.HandleTypeOpaqueFdExt, -1);
        Assert.Equal(1, driver.ImportCalls);
        Assert.Null(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Semaphore, SemaphoreDriver.Name));
        Assert.Equal(0, driver.IdentityReads);
        fixture.CleanupGpuResources();
    }

    /// <summary>Void import calls preserve reservations until a caller establishes the actual lifecycle outcome.</summary>
    [Fact]
    public void NativeImportsPreserveReservationIdentityAndOpaqueHandleOwnership()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var driver = new SemaphoreDriver();
        var table = OpenGLBindingTable.For(typeof(GL));
        var manager = GpuResourceManagerSystem.CurrentManager!;
        uint name = 0;
        var generate = (delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glGenSemaphoresEXT");
        generate(1, &name);
        var entry = Assert.IsType<GpuResourceMetadataEntry<GpuSemaphoreMetadata>>(manager.FindResource(GpuResourceKind.Semaphore, (int)name));
        Assert.False(entry.IsRealized); Assert.False(entry.IsUsable); Assert.Equal(0ul, entry.MetadataRevision);
        generate(1, &name); generate(-1, &name);
        Assert.Same(entry, manager.FindResource(GpuResourceKind.Semaphore, (int)name));
        Assert.Equal(3, driver.FactoryCalls);
        ((delegate* unmanaged[Stdcall]<uint, int, int, void>)table.Read("glImportSemaphoreFdEXT"))(name,
            (int)ExternalHandleType.HandleTypeOpaqueFdExt, -1);
        Assert.False(entry.IsRealized); Assert.Equal(0, driver.IdentityReads);
        driver.ImportSucceeds = true;
        ((delegate* unmanaged[Stdcall]<uint, int, nint, void>)table.Read("glImportSemaphoreWin32HandleEXT"))(name,
            (int)ExternalHandleType.HandleTypeOpaqueWin32Ext, (nint)123);
        Assert.False(entry.IsRealized); Assert.Equal(0, driver.IdentityReads);
        Assert.True(GL.Ext.IsSemaphore((int)name));
        Assert.True(entry.IsRealized); Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
        ((delegate* unmanaged[Stdcall]<uint, int, nint, void>)table.Read("glImportSemaphoreWin32NameEXT"))(name,
            (int)ExternalHandleType.HandleTypeOpaqueWin32Ext, (nint)456);
        Assert.Equal((nint)456, driver.LastHandle); Assert.Equal(3, driver.ImportCalls);
        Assert.Equal(1ul, entry.MetadataRevision);
        Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Semaphore, (int)name, new ForbiddenReader()));
        GL.Ext.DeleteSemaphore((int)name);
        Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State);
        generate(1, &name);
        Assert.NotSame(entry, manager.FindResource(GpuResourceKind.Semaphore, (int)name));
        GL.Ext.DeleteSemaphore((int)name);
        Assert.Equal(2, driver.DeleteCalls); fixture.CleanupGpuResources();
    }

    /// <summary>The managed factory uses one original dispatch and one targeted import outcome check.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ManagedImportPublishesOnlyConfirmedOriginalPayload(bool succeeds)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var original = GpuSupport.Graphics;
        var field = AccessTools.Field(typeof(GpuSupport), "graphics");
        field.SetValue(null, original with { SupportsExtSemaphore = true, SupportsExtSemaphoreWin32 = true });
        try
        {
            var context = new RHICommandContext(new OpenGLBackend(OpenGlStateCache.Current));
            using var driver = new SemaphoreDriver { ImportSucceeds = succeeds };
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var description = new RHISemaphoreDescription(new(ExternalHandleKind.OpaqueWin32, (nint)123,
                ExternalHandleOwnership.RetainByCaller));
            if (succeeds)
            {
                using var owner = context.CreateSemaphore(description).RequireResource();
                var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
                Assert.True(entry.IsRealized); Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
                Assert.Equal(1, driver.FactoryCalls); Assert.Equal(1, driver.ImportCalls); Assert.Equal(1, driver.IdentityReads);
                owner.Dispose(); fixture.CleanupGpuResources();
                Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State);
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => context.CreateSemaphore(description));
                manager.ResourceDisposals!.DrainPending();
                Assert.Null(manager.FindResource(GpuResourceKind.Semaphore, SemaphoreDriver.Name));
                Assert.Equal(1, driver.FactoryCalls); Assert.Equal(1, driver.ImportCalls); Assert.Equal(1, driver.DeleteCalls);
                fixture.CleanupGpuResources();
            }
        }
        finally { field.SetValue(null, original); fixture.CleanupGpuResources(); }
    }

    /// <summary>An unimported owner can be disposed, but cannot submit a wait or signal.</summary>
    [Fact]
    public void UnimportedManagedReservationCannotBeConsumed()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var original = GpuSupport.Graphics;
        var field = AccessTools.Field(typeof(GpuSupport), "graphics");
        field.SetValue(null, original with { SupportsExtSemaphore = true });
        try
        {
            var context = new RHICommandContext(new OpenGLBackend(OpenGlStateCache.Current));
            using var driver = new SemaphoreDriver();
            using var owner = context.CreateSemaphore(new()).RequireResource();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = manager.GetResourceRegistration(owner)!;
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable); Assert.True(owner.IsValid);
            Assert.Throws<InvalidOperationException>(() => context.WaitSemaphore(owner, [], []));
            Assert.Throws<InvalidOperationException>(() => context.SignalSemaphore(owner, [], []));
            Assert.Equal(0, driver.IdentityReads);
            Assert.Null(manager.RecoverResource(GpuResourceKind.Semaphore, owner.ResourceId, new ForbiddenReader()));
            owner.Dispose(); fixture.CleanupGpuResources(); Assert.Equal(1, driver.DeleteCalls);
        }
        finally { field.SetValue(null, original); fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Rejects any attempted discovery of an empty semaphore descriptor.</summary>
    private sealed class ForbiddenReader : IGpuResourceMetadataReader<GpuSemaphoreMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Unexpected semaphore discovery.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuSemaphoreMetadata metadata) => throw new InvalidOperationException("Unexpected semaphore discovery.");
    }

    /// <summary>Supplies deterministic optional originals through the production binding loader and callbacks.</summary>
    private sealed class SemaphoreDriver : IDisposable
    {
        internal const int Name = 1900000001;
        private readonly NativeDriverCapture native = new();
        private bool imported;
        internal bool ImportSucceeds;
        internal int FactoryCalls, ImportCalls, IdentityReads, DeleteCalls;
        internal nint LastHandle;

        #region Internal API
        /// <summary>Installs exact-ABI stand-ins without closing any operating-system handles.</summary>
        internal SemaphoreDriver()
        {
            native.Add("glGenSemaphoresEXT", new Names((count, values) =>
            {
                FactoryCalls++;
                if (count <= 0 || values == null) return;
                for (int i = 0; i < count; i++) values[i] = Name;
            }));
            native.Add("glDeleteSemaphoresEXT", new Names((count, values) => { DeleteCalls++; if (count > 0) imported = false; }));
            native.Add("glIsSemaphoreEXT", new Exists(value => { IdentityReads++; return imported ? (byte)1 : (byte)0; }));
            native.Add("glImportSemaphoreFdEXT", new ImportFd((value, type, fd) => { ImportCalls++; LastHandle = fd; imported |= ImportSucceeds; }));
            native.Add("glImportSemaphoreWin32HandleEXT", new ImportHandle((value, type, handle) => { ImportCalls++; LastHandle = handle; imported |= ImportSucceeds; }));
            native.Add("glImportSemaphoreWin32NameEXT", new ImportHandle((value, type, handle) => { ImportCalls++; LastHandle = handle; imported |= ImportSucceeds; }));
            native.Install();
        }
        #endregion
        #region Public API
        /// <summary>Restores original loading, retaining callback originals for already fetched addresses.</summary>
        public void Dispose() => native.Dispose();
        #endregion
        #region Private
        /// <summary>Matches counted name outputs.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Names(int count, uint* values);
        /// <summary>Matches native GLboolean results.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte Exists(uint name);
        /// <summary>Matches a caller-supplied file descriptor import.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ImportFd(uint name, int type, int fd);
        /// <summary>Matches pointer-width handles or UTF-16 names.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ImportHandle(uint name, int type, nint handle);
        #endregion
    }
    #endregion
}
