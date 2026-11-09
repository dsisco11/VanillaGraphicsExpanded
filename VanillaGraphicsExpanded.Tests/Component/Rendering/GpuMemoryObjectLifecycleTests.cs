using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Qualifies external-memory identity delivery independently of optional hardware payload support.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed unsafe class GpuMemoryObjectLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Invalid untracked memory imports remain nonfatal native calls without speculative registration.</summary>
    [Fact]
    public void UntrackedMemoryImportDoesNotTriggerFatalDiagnostics()
    {
        // Arrange: Observe the actual generated import callback for an untracked native memory name.
        // Act and assert: Forward an invalid import once without publishing speculative managed registration.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var driver = new MemoryDriver();
        GL.Ext.ImportMemoryF(1900000000, 4096, ExternalHandleType.HandleTypeOpaqueFdExt, -1);
        Assert.Equal(1, driver.ImportCalls);
        Assert.Null(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.MemoryObject, 1900000000));
        fixture.CleanupGpuResources();
    }

    /// <summary>Duplicate creation and imports preserve one registration and revision through retirement and replacement.</summary>
    [Fact]
    public void GeneratedMemoryCallbacksPreserveRegistrationIdentityAndRevision()
    {
        // Arrange: Install controlled optional-memory outcomes and create a generated memory-object registration.
        // Act and assert: Replay creation, forward three import forms, reject metadata reads, then retire and replace the registration.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var driver = new MemoryDriver();
        var table = OpenGLBindingTable.For(typeof(GL));
        var manager = RHIResourceManagerSystem.CurrentManager!;
        uint name = 0;
        bool registrationMayExist = false;
        Exception? testFailure = null;
        var cleanupFailures = new List<Exception>();

        // Retain the behavioral failure while making every cleanup attempt, so a failed assertion cannot poison the shared GPU fixture.
        void TryCleanup(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception exception) { cleanupFailures.Add(exception); }
        }

        try
        {
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glCreateMemoryObjectsEXT"))(1, &name);
            registrationMayExist = true;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuMemoryObjectMetadata>>(manager.FindResource(GpuResourceKind.MemoryObject, (int)name));
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            Assert.Equal(1, driver.FactoryCalls);
            // Replayed live names and rejected counts cannot overwrite the original registration.
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glCreateMemoryObjectsEXT"))(1, &name);
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glCreateMemoryObjectsEXT"))(-1, &name);
            Assert.Same(entry, manager.FindResource(GpuResourceKind.MemoryObject, (int)name));
            Assert.Equal(1ul, entry.MetadataRevision); Assert.Equal(3, driver.FactoryCalls);
            ((delegate* unmanaged[Stdcall]<uint, ulong, int, int, void>)table.Read("glImportMemoryFdEXT"))(name, 4096,
                (int)ExternalHandleType.HandleTypeOpaqueFdExt, -1);
            ((delegate* unmanaged[Stdcall]<uint, ulong, int, nint, void>)table.Read("glImportMemoryWin32HandleEXT"))(name, 4096,
                (int)ExternalHandleType.HandleTypeOpaqueWin32Ext, (nint)123);
            ((delegate* unmanaged[Stdcall]<uint, ulong, int, nint, void>)table.Read("glImportMemoryWin32NameEXT"))(name, 4096,
                (int)ExternalHandleType.HandleTypeOpaqueWin32Ext, (nint)456);
            int dedicated = 1;
            ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)table.Read("glMemoryObjectParameterivEXT"))(name,
                (int)MemoryObjectParameterName.DedicatedMemoryObjectExt, &dedicated);
            Assert.Equal(3, driver.ImportCalls); Assert.Equal((nint)456, driver.LastHandle);
            Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.MemoryObject, (int)name, new ForbiddenReader()));
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glDeleteMemoryObjectsEXT"))(1, &name);
            Assert.Equal(1, driver.DeleteCalls); Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State);
            registrationMayExist = false;
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glCreateMemoryObjectsEXT"))(1, &name);
            registrationMayExist = true;
            var replacement = manager.FindResource(GpuResourceKind.MemoryObject, (int)name);
            Assert.NotSame(entry, replacement); Assert.True(replacement!.IsUsable);
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glDeleteMemoryObjectsEXT"))(1, &name);
            registrationMayExist = false;
        }
        catch (Exception exception)
        {
            testFailure = exception;
            throw;
        }
        finally
        {
            // Delete a partially created registration before restoring driver bindings, then always drain fixture-owned resources.
            if (registrationMayExist)
            {
                try { ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glDeleteMemoryObjectsEXT"))(1, &name); }
                catch (Exception exception) { cleanupFailures.Add(exception); }
            }
            TryCleanup(fixture.CleanupGpuResources);
            if (testFailure is null && cleanupFailures.Count > 0)
                throw new AggregateException("Memory-object test cleanup failed.", cleanupFailures);
        }
    }
    #endregion

    #region Private
    /// <summary>Rejects descriptor and validity discovery for an empty maintained schema.</summary>
    private sealed class ForbiddenReader : IGpuResourceMetadataReader<GpuMemoryObjectMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Unexpected memory probe.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuMemoryObjectMetadata metadata) => throw new InvalidOperationException("Unexpected memory discovery.");
    }

    /// <summary>Supplies deterministic optional-extension driver outcomes to the actual generated callbacks.</summary>
    private sealed class MemoryDriver : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        private bool live;
        internal int FactoryCalls, ImportCalls, DeleteCalls;
        internal nint LastHandle;
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Names(int count, uint* values);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte Exists(uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ImportFd(uint value, ulong size, int type, int fd);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void ImportHandle(uint value, ulong size, int type, nint handle);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Parameter(uint value, int pname, int* data);

        #region Internal API
        /// <summary>Installs controlled originals without claiming optional native hardware execution.</summary>
        internal MemoryDriver()
        {
            native.Add("glCreateMemoryObjectsEXT", new Names((count, values) =>
            {
                FactoryCalls++;
                if (count <= 0 || values == null) return;
                for (int i = 0; i < count; i++) values[i] = 1900000000;
                live = true;
            }));
            native.Add("glDeleteMemoryObjectsEXT", new Names((count, values) => { DeleteCalls++; if (count > 0) live = false; }));
            native.Add("glIsMemoryObjectEXT", new Exists(value => live ? (byte)1 : (byte)0));
            native.Add("glImportMemoryFdEXT", new ImportFd((value, size, type, fd) => { ImportCalls++; LastHandle = fd; }));
            native.Add("glImportMemoryWin32HandleEXT", new ImportHandle((value, size, type, handle) => { ImportCalls++; LastHandle = handle; }));
            native.Add("glImportMemoryWin32NameEXT", new ImportHandle((value, size, type, handle) => { ImportCalls++; LastHandle = handle; }));
            native.Add("glMemoryObjectParameterivEXT", new Parameter((value, pname, data) => { }));
            native.Install();
        }
        #endregion
        #region Public API
        /// <summary>Restores ordinary loading while retaining previously fetched callback originals.</summary>
        public void Dispose() => native.Dispose();
        #endregion

    }
    #endregion
}
