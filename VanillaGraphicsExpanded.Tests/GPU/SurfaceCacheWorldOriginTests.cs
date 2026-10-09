using System.Runtime.InteropServices;
using Moq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Protects shared terrain origin publication against animated engine camera translations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SurfaceCacheWorldOriginTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The universal publisher decomposes the camera origin independently of entity and view-matrix bob.</summary>
    [Theory]
    [InlineData(-32.375, -2, 31.625)]
    [InlineData(16777216.25, 524288, .25)]
    [InlineData(-16777216.25, -524289, 31.75)]
    public void PublisherUsesCameraOriginIndependentlyOfAnimatedMatrix(double origin, int chunk, double remainder)
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        LumOnCameraState camera = new(origin, origin, origin, origin, origin, origin, 0);
        float[] matrix = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        float[] projection = matrix.ToArray();
        var world = new Mock<IClientWorldAccessor>();
        world.SetupGet(value => value.Player).Returns(RuntimeEngineServices.CameraPlayer(() => camera));
        world.SetupGet(value => value.ElapsedMilliseconds).Returns(0L);
        var render = RuntimeEngineServices.Render(4, [], () => matrix, () => projection, () => { });
        var api = RuntimeEngineServices.Client(assets.Api, events.Api, world.Object, render, assets.Api.Shader);
        using var publisher = new VgeFrameRenderer(api);
        // Keep the terrain origin fixed while entity position and view translation change.
        foreach (float bob in new[] { -.3f, 0f, .4f })
        {
            camera = camera with { PositionX = origin + bob, PositionY = origin - 1.6 + bob, PositionZ = origin - bob };
            matrix[12] = bob * 2; matrix[13] = -1.4f + bob; matrix[14] = -bob;
            events.Render(EnumRenderStage.Before);
            events.Render(EnumRenderStage.Opaque);
            Assert.Equal(new int[] { chunk, chunk, chunk, 0 },
                MemoryMarshal.Cast<byte, int>(VgeFrameRenderer.Current.Bytes.Slice(512, 16)).ToArray());
            Assert.Equal(new float[] { (float)remainder, (float)remainder, (float)remainder, 0 },
                MemoryMarshal.Cast<byte, float>(VgeFrameRenderer.Current.Bytes.Slice(528, 16)).ToArray());
            Assert.True(VgeFrameRenderer.Current.TryBindToSlot(GpuBindingRegistry.Ubo.Frame));
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, GpuBindingRegistry.Ubo.Frame, out int buffer);
            GL.GetInteger(GetIndexedPName.UniformBufferStart, GpuBindingRegistry.Ubo.Frame, out int offset);
            byte[] published = new byte[VgeFrameUniformBuffer.PackedSize];
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.UniformBuffer, buffer);
            GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, published.Length, published);
            Assert.Equal(VgeFrameRenderer.Current.Bytes.ToArray(), published);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
