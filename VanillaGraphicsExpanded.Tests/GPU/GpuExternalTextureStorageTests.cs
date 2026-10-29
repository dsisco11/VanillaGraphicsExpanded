using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the narrow imported texture storage outcome observation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuExternalTextureStorageTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A storage transition publishes supplied facts; a rejected request retains the old snapshot.</summary>
    [Fact]
    public void ImportedStorageChecksOnlyImmutableOutcome()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        using var driver = new StorageDriver();
        var table = OpenGLBindingTable.For(typeof(GL));
        GL.CreateTextures(TextureTarget.Texture2D, 1, out int texture);
        try
        {
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Texture, texture));
            ulong initialRevision = entry.MetadataRevision;
            var storage = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, uint, ulong, void>)table.Read("glTextureStorageMem2DEXT");

            // The first driver call leaves storage absent; the second establishes it.
            storage((uint)texture, 1, (int)SizedInternalFormat.Rgba8, 8, 4, 1, 0);
            Assert.True(entry.IsUsable);
            Assert.Equal(initialRevision, entry.MetadataRevision);
            Assert.Empty(entry.Metadata.Levels);
            driver.Accept = true;
            storage((uint)texture, 1, (int)SizedInternalFormat.Rgba8, 8, 4, 1, 0);
            Assert.True(entry.IsUsable);
            Assert.True(entry.Metadata.ImmutableStorage);
            Assert.Equal(8, Assert.Single(entry.Metadata.Levels).Width);
            Assert.Equal(2, driver.StorageCalls);
            Assert.Equal(2, driver.ImmutableReads);
            Assert.Equal(0, driver.OtherParameterReads);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteTexture(texture); fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Supplies deterministic acceptance while retaining the real texture flag query.</summary>
    private sealed class StorageDriver : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        internal bool Accept;
        internal int StorageCalls, ImmutableReads, OtherParameterReads;

        /// <summary>Matches external-memory texture storage.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void Storage(uint texture, int levels, int format, int width, int height, uint memory, ulong offset);
        /// <summary>Matches named texture parameter reads.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void Parameter(uint texture, int name, int* value);

        /// <summary>Installs the deterministic driver and its parameter-read counter.</summary>
        internal StorageDriver()
        {
            var allocate = (delegate* unmanaged[Stdcall]<uint, int, int, int, int, void>)native.Original("glTextureStorage2D");
            var getParameter = (delegate* unmanaged[Stdcall]<uint, int, int*, void>)native.Original("glGetTextureParameteriv");
            native.Add("glTextureStorageMem2DEXT", new Storage((texture, levels, format, width, height, memory, offset) =>
            {
                StorageCalls++;
                if (Accept) allocate(texture, levels, format, width, height);
            }));
            native.Add("glGetTextureParameteriv", new Parameter((texture, name, value) =>
            {
                getParameter(texture, name, value);
                if (name == (int)GetTextureParameter.TextureImmutableFormat) ImmutableReads++;
                else OtherParameterReads++;
            }));
            native.Install();
        }

        /// <summary>Restores the native loader.</summary>
        public void Dispose() => native.Dispose();
    }
    #endregion
}
