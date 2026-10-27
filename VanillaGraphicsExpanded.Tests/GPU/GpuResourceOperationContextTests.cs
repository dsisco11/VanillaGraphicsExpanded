using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies affinity rejection occurs before native commands or canonical availability withdrawal.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceOperationContextTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Foreign logical namespace and wrong thread cannot use available snapshots to bypass admission.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AvailableInputsStillRequireOwningContextAndThread(bool worker)
    {
        fixture.EnsureContextValid();
        var origin = RenderContextRegistry.Current();
        var current = origin;
        using var manager = new GpuResourceManager(() => current, static _ => true);
        GpuResourceManagerSystem.Initialize(manager);
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(16);
            using var texture = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var program = GpuProgramObject.Create();
            using var vao = GpuVao.Create();
            var bufferEntry = manager.GetResourceRegistration(buffer)!;
            var textureEntry = manager.GetResourceRegistration(texture)!;
            var programEntry = manager.GetResourceRegistration(program)!;
            var vaoEntry = manager.GetResourceRegistration(vao)!;
            ulong vaoRevision = vaoEntry.MetadataRevision;
            ulong programRevision = programEntry.MetadataRevision;
            ulong bufferRevision = bufferEntry.MetadataRevision, textureRevision = textureEntry.MetadataRevision;
            Action attempt = () =>
            {
                Assert.Throws<InvalidOperationException>(() => { using var operation = new GpuResourceManager.MetadataOperation<GpuBufferMetadata>(buffer, GpuMetadataChange.Storage); });
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireTextureCommandInput(texture));
                Assert.Throws<InvalidOperationException>(() => new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current).SubmitProgramExecutable(program));
                if (worker) Assert.Throws<InvalidOperationException>(() => OpenGLProgramCalls.ExecuteCoreLinkProgram(program.ProgramId));
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.RequireProgramSnapshot(program));
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireEngineGeometryConfiguration(vao, new([]), [], default));
            };
            if (worker)
            {
                Exception? failure = null;
                var thread = new Thread(() => { try { attempt(); } catch (Exception error) { failure = error; } });
                thread.Start(); thread.Join();
                Assert.Null(failure);
            }
            else
            {
                current = (origin.Handle, origin.Generation + 1);
                attempt();
                // Exact native calls preserve the physical context; a simulated manager namespace is not their authority.
                OpenGLProgramCalls.ExecuteCoreLinkProgram(program.ProgramId);
                Assert.Throws<InvalidOperationException>(() => GpuBufferUploader.Allocate(buffer, 64, IntPtr.Zero, BufferUsage.DynamicDraw));
                Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = texture, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource());
                current = origin;
            }
            Assert.True(bufferEntry.MetadataAvailable); Assert.True(textureEntry.MetadataAvailable);
            Assert.Equal(bufferRevision, bufferEntry.MetadataRevision); Assert.Equal(textureRevision, textureEntry.MetadataRevision);
            Assert.True(programEntry.MetadataAvailable); Assert.Equal(programRevision, programEntry.MetadataRevision);
            Assert.True(vaoEntry.MetadataAvailable); Assert.Equal(vaoRevision, vaoEntry.MetadataRevision);
            GL.GetNamedBufferParameter(buffer.BufferId, BufferParameterName.BufferSize, out int size);
            Assert.Equal(16, size);
            Assert.Equal(4, texture.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { current = origin; GpuResourceManagerSystem.Shutdown(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}
