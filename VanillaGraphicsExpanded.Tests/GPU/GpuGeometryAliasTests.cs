using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies optional EXT first-use delivery with explicit core-backed native stand-ins.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuGeometryAliasTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>EXT getters initialize tracked reservations without observer-initiated descriptor queries.</summary>
    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    [InlineData(false, 3)] [InlineData(true, 3)]
    public void ExtGetterRealizesOriginalName(bool core, int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current.Capabilities;
        int vao = GL.GenVertexArray();
        int previous = GL.GetInteger(GetPName.VertexArrayBinding);
        try
        {
            var original = GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.VertexArray, vao)!;
            using var driver = new NativeDriverCapture();
            nint bind = driver.Original("glBindVertexArray");
            int forwards = 0;
            // Optional EXT entrypoints are absent on some core drivers. This stand-in
            // establishes the specified first-use object and returns deterministic outputs.
            /// <summary>Emulates specified EXT first-use initialization beneath the observed slot.</summary>
            void Initialize(uint name)
            {
                forwards++;
                ((delegate* unmanaged[Stdcall]<uint, void>)bind)(name);
                ((delegate* unmanaged[Stdcall]<uint, void>)bind)((uint)previous);
            }
            driver.Add("glGetVertexArrayIntegervEXT", new IntegerQuery((name, parameter, output) => { Initialize(name); *output = 0; }));
            driver.Add("glGetVertexArrayIntegeri_vEXT", new IndexedIntegerQuery((name, index, parameter, output) => { Initialize(name); *output = 0; }));
            driver.Add("glGetVertexArrayPointervEXT", new PointerQuery((name, parameter, output) => { Initialize(name); *output = 0; }));
            driver.Add("glGetVertexArrayPointeri_vEXT", new IndexedPointerQuery((name, index, parameter, output) => { Initialize(name); *output = 0; }));
            driver.Install();
            var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
            int value = -1; nint pointer = -1;
            if (variant == 0) ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)table.Read("glGetVertexArrayIntegervEXT"))((uint)vao, (int)All.ElementArrayBufferBinding, &value);
            else if (variant == 1) ((delegate* unmanaged[Stdcall]<uint, uint, int, int*, void>)table.Read("glGetVertexArrayIntegeri_vEXT"))((uint)vao, 0, (int)All.VertexAttribArrayEnabled, &value);
            else if (variant == 2) ((delegate* unmanaged[Stdcall]<uint, int, nint*, void>)table.Read("glGetVertexArrayPointervEXT"))((uint)vao, (int)All.VertexAttribArrayPointer, &pointer);
            else ((delegate* unmanaged[Stdcall]<uint, uint, int, nint*, void>)table.Read("glGetVertexArrayPointeri_vEXT"))((uint)vao, 0, (int)All.VertexAttribArrayPointer, &pointer);
            Assert.Equal(1, forwards);
            Assert.Equal(0, variant < 2 ? value : (long)pointer);
            Assert.True(original.IsUsable); Assert.Equal(1UL, original.MetadataRevision);
            Assert.Same(original, GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.VertexArray, vao));
            Assert.Equal(previous, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteVertexArray(vao); fixture.CleanupGpuResources(); }
    }
    /// <summary>Unknown EXT provenance permits one adoption attempt; later binding cannot hide failed discovery.</summary>
    [Fact]
    public void FailedExtDiscoveryRequiresExplicitRecovery()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current.Capabilities;
        OpenGLDriverCalls.CreateVertexArrays(1, out int vao);
        try
        {
            var manager = GpuResourceManagerSystem.CurrentManager!;
            Assert.Null(manager.FindResource(GpuResourceKind.VertexArray, vao));
            using var driver = new NativeDriverCapture();
            driver.Add("glGetVertexArrayIntegervEXT", new IntegerQuery((name, parameter, output) => *output = 0));
            driver.Install();
            nint call = OpenGLBindingTable.For(typeof(GL)).Read("glGetVertexArrayIntegervEXT");
            int output = -1;
            ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)call)((uint)vao, (int)All.ElementArrayBufferBinding, &output);
            var original = manager.FindResource(GpuResourceKind.VertexArray, vao)!;
            Assert.Equal(GpuInitialDiscovery.ImplicitCreation, original.InitialDiscovery);
            Assert.False(original.IsUsable);
            var proof = new SourceProof();
            Assert.Null(manager.AcquireResource(GpuResourceKind.VertexArray, vao, proof)); Assert.Equal(1, proof.Reads);
            GL.BindVertexArray(vao);
            ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)call)((uint)vao, (int)All.ElementArrayBufferBinding, &output);
            Assert.False(original.IsUsable); Assert.Equal(GpuInitialDiscovery.None, original.InitialDiscovery);
            Assert.Null(manager.AcquireResource(GpuResourceKind.VertexArray, vao, proof)); Assert.Equal(1, proof.Reads);
            proof.Available = true;
            Assert.Same(original, manager.RecoverResource(GpuResourceKind.VertexArray, vao, proof));
            Assert.Equal(2, proof.Reads); Assert.True(original.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindVertexArray(0); GL.DeleteVertexArray(vao); fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Supplies the fixture's original owner proof only at an explicit adoption or recovery boundary.</summary>
    private sealed class SourceProof : IGpuResourceMetadataReader<GpuVertexArrayMetadata>
    {
        internal bool Available;
        internal int Reads;

        #region Public API
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuVertexArrayMetadata metadata)
        {
            Reads++; metadata = default;
            return Available;
        }
        #endregion
    }
    /// <summary>Matches the exact EXT integer getter ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IntegerQuery(uint name, int parameter, int* output);
    /// <summary>Matches the exact EXT indexed integer getter ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IndexedIntegerQuery(uint name, uint index, int parameter, int* output);
    /// <summary>Matches the exact EXT pointer-output getter ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void PointerQuery(uint name, int parameter, nint* output);
    /// <summary>Matches the exact EXT indexed pointer-output getter ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IndexedPointerQuery(uint name, uint index, int parameter, nint* output);
    #endregion
}
