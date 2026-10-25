using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Verifies helper scopes recover unknown native bindings before temporary buffer operations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuBufferUnknownBindingTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Unknown VAO and element selection survive a temporary EBO wrapper scope.</summary>
    [Fact]
    public void ElementScopeRestoresUnknownNativeSelection()
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        try
        {
            using var vao = GpuVao.Create();
            using var sentinel = GpuEbo.Create();
            using var target = GpuEbo.Create();
            GL.BindVertexArray(vao.VertexArrayId);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, sentinel.BufferId);
            StateCache.Current.InvalidateAll();
            using (target.BindScope()) Assert.Equal(target.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(vao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Fallback mapping, flushing and unmapping each restore unknown native selections and retain written data.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FallbackMappingRestoresUnknownBindings(bool element)
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        var field = AccessTools.Field(AccessTools.Inner(typeof(GpuBufferObject), "BufferMapDsa"), "enabledState");
        object previous = field.GetValue(null)!;
        try
        {
            using var copySentinel = GpuVbo.Create();
            using var vao = GpuVao.Create();
            using GpuBufferObject sentinel = element ? GpuEbo.Create() : GpuVbo.Create();
            using GpuBufferObject target = element ? GpuEbo.Create() : GpuVbo.Create();
            target.Allocate(16);
            field.SetValue(null, -1);
            GL.BindBuffer(BufferTarget.CopyWriteBuffer, copySentinel.BufferId);
            var query = element ? GetPName.ElementArrayBufferBinding : GetPName.ArrayBufferBinding;
            GL.BindVertexArray(vao.VertexArrayId);
            GL.BindBuffer(target.Target, sentinel.BufferId);
            StateCache.Current.InvalidateAll();
            using (var mapped = target.MapRange<int>(0, 4, MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapFlushExplicitBit))
            {
                Assert.True(mapped.IsMapped);
                Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
                Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
                mapped.Span[0] = 12345;
                StateCache.Current.InvalidateAll();
                mapped.Flush();
                Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
                Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
                StateCache.Current.InvalidateAll();
            }
            Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
            Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
            Assert.Equal(vao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            using (var read = target.MapRange<int>(0, 4, MapBufferAccessMask.MapReadBit)) Assert.Equal(12345, read.Span[0]);
            Assert.Equal(sentinel.BufferId, GL.GetInteger(query));
            Assert.Equal(copySentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            field.SetValue(null, previous);
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Managed array fallback preserves unknown bindings and publishes its uploaded capacity.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ArrayUploadFallbackRestoresUnknownBindings(bool element)
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        var graphics = GpuSupport.Graphics;
        try
        {
            using var vao = GpuVao.Create();
            using GpuBufferObject sentinel = element ? GpuEbo.Create() : GpuVbo.Create();
            using GpuBufferObject target = element ? GpuEbo.Create() : GpuVbo.Create();
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, graphics with { SupportsArbDirectStateAccess = false });
            GL.BindVertexArray(vao.VertexArrayId);
            GL.BindBuffer(target.Target, sentinel.BufferId);
            StateCache.Current.InvalidateAll();
            GpuBufferUploader.BufferData(target, 16, new[] { 12, 34, 56, 78 }, BufferUsageHint.DynamicDraw);
            Assert.Equal(sentinel.BufferId, GL.GetInteger(element ? GetPName.ElementArrayBufferBinding : GetPName.ArrayBufferBinding));
            Assert.Equal(vao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(16, target.SizeBytes);
            using (var read = target.MapRange<int>(0, 4, MapBufferAccessMask.MapReadBit))
            Assert.Equal(new[] { 12, 34, 56, 78 }, read.Span.ToArray());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, graphics);
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    /// <summary>A native fallback mapping exception still restores the unknown copy-write selection.</summary>
    [Fact]
    public void MappingExceptionRestoresUnknownSelection()
    {
        fixture.MakeCurrent();
        fixture.InitializeResourceDisposal();
        var field = AccessTools.Field(AccessTools.Inner(typeof(GpuBufferObject), "BufferMapDsa"), "enabledState");
        object previous = field.GetValue(null)!;
        var harmony = new Harmony("Tests.BufferMapUnwind");
        try
        {
            using var sentinel = GpuVbo.Create();
            using var target = GpuEbo.Create();
            target.Allocate(16);
            field.SetValue(null, -1);
            GL.BindBuffer(BufferTarget.CopyWriteBuffer, sentinel.BufferId);
            StateCache.Current.InvalidateAll();
            harmony.Patch(((Func<BufferTarget, IntPtr, IntPtr, MapBufferAccessMask, IntPtr>)GL.MapBufferRange).Method,
                prefix: new HarmonyMethod(typeof(GpuBufferUnknownBindingTests), nameof(RejectMapping)));
            Assert.Throws<InvalidOperationException>(() => target.MapRange<int>(0, 4, MapBufferAccessMask.MapReadBit));
            Assert.Equal(sentinel.BufferId, GL.GetInteger((GetPName)BufferTarget.CopyWriteBuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            field.SetValue(null, previous);
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Rejects the native mapping invocation to exercise scope cleanup without changing GPU state.</summary>
    private static void RejectMapping() => throw new InvalidOperationException("Injected mapping failure.");
    #endregion
}
