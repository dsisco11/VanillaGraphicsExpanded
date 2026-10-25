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
            using var buffer = GpuShaderStorageBuffer.Create(); buffer.Allocate(16);
            using var texture = DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8, immutableStorage: true);
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
                Assert.Throws<InvalidOperationException>(() => StateCache.Current.SubmitProgramLink(program));
                Assert.Throws<InvalidOperationException>(() => StateCache.Current.LinkProgram(program));
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.RequireProgramSnapshot(program));
                int selected = 0, stride = 0, divisor = 0;
                nint offset = 0;
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireVertexBindingFacts(vao, vao.VertexArrayId, 0, origin,
                    GpuVertexBindingFields.Buffer, ref selected, ref offset, ref stride, ref divisor));
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
                Assert.Throws<InvalidOperationException>(() => StateCache.Current.TryNamedBufferData(buffer, 64, IntPtr.Zero, BufferUsageHint.DynamicDraw));
                Assert.Throws<InvalidOperationException>(() => GpuTextureView.CreateFromTexture(texture, TextureTarget.Texture2DArray, PixelInternalFormat.Rgba8, 0, 1, 0, 1));
                current = origin;
            }
            Assert.True(bufferEntry.MetadataAvailable); Assert.True(textureEntry.MetadataAvailable);
            Assert.Equal(bufferRevision, bufferEntry.MetadataRevision); Assert.Equal(textureRevision, textureEntry.MetadataRevision);
            Assert.True(programEntry.MetadataAvailable); Assert.Equal(programRevision, programEntry.MetadataRevision); Assert.Null(program.PendingLink);
            Assert.True(vaoEntry.MetadataAvailable); Assert.Equal(vaoRevision, vaoEntry.MetadataRevision);
            GL.GetNamedBufferParameter(buffer.BufferId, BufferParameterName.BufferSize, out int size);
            Assert.Equal(16, size);
            Assert.Equal(4, texture.Width);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { current = origin; GpuResourceManagerSystem.Shutdown(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}
