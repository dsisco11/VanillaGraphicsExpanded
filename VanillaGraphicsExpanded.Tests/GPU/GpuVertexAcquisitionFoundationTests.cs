using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks field-specific manager acquisition without a canonical vertex layout mirror.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuVertexAcquisitionFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Only requested fields are queried, including an offset too wide for a signed 32-bit value.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManagerRecoversOnlyRequestedFieldsWithNativeWidth(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(true);
            using var vao = GpuVao.Create();
            using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource(); buffer.Allocate(64);
            nint wideOffset = checked((nint)((long)int.MaxValue + 1024));
            StateCache.Current.VertexArrayVertexBuffer(vao.VertexArrayId, 1, buffer.BufferId, wideOffset, 24);
            StateCache.Current.VertexArrayBindingDivisor(vao.VertexArrayId, 1, 7);
            int actualBuffer = -2, stride = -3, divisor = -4;
            nint offset = -5;
            using var capture = new BindingCallCapture();
            GpuResourceManager.AcquireVertexBindingFacts(vao, vao.VertexArrayId, 1, RenderContextRegistry.Current(),
                GpuVertexBindingFields.Offset, ref actualBuffer, ref offset, ref stride, ref divisor);
            Assert.Equal(wideOffset, offset);
            Assert.Equal(-2, actualBuffer); Assert.Equal(-3, stride); Assert.Equal(-4, divisor);
            Assert.Equal(1, capture.Calls(nameof(GL.GetVertexArrayIndexed64)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            capture.Reset();
            GpuResourceManager.AcquireVertexBindingFacts(vao, vao.VertexArrayId, 1, RenderContextRegistry.Current(),
                GpuVertexBindingFields.Stride | GpuVertexBindingFields.Divisor, ref actualBuffer, ref offset, ref stride, ref divisor);
            Assert.Equal(24, stride); Assert.Equal(7, divisor); Assert.Equal(-2, actualBuffer);
            Assert.Equal(wideOffset, offset);
            Assert.Equal(2, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed64)));
            capture.Reset();
            GpuResourceManager.AcquireVertexBindingFacts(vao, vao.VertexArrayId, 1, RenderContextRegistry.Current(),
                GpuVertexBindingFields.None, ref actualBuffer, ref offset, ref stride, ref divisor);
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed64)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Failed multi-field readback cannot expose partly acquired helper state or consume the caller's native error.</summary>
    [Fact]
    public void FailedRecoveryLeavesCallerFieldsUnchanged()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao = GpuVao.Create();
            int buffer = 12, stride = 16, divisor = 3;
            nint offset = 32;
            Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireVertexBindingFacts(
                vao, vao.VertexArrayId, int.MaxValue, RenderContextRegistry.Current(), GpuVertexBindingFields.All,
                ref buffer, ref offset, ref stride, ref divisor));
            Assert.Equal(12, buffer); Assert.Equal(16, stride); Assert.Equal(3, divisor); Assert.Equal((nint)32, offset);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            while (GL.GetError() != ErrorCode.NoError) { }
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
