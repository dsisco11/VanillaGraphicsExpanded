using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the sky's procedural geometry ownership without engine mesh allocation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class AtmosphereSkyRendererTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Creating and retiring the empty sky VAO preserves unrelated native buffer storage and bindings.</summary>
    [Fact]
    public void ProceduralSkyGeometryPreservesIncomingBindings()
    {
        EnsureContextValid();
        using var priorVao = GpuVao.Create();
        using var priorArray = GpuVbo.Create();
        using var priorElements = GpuEbo.Create();
        priorVao.Bind();
        priorArray.UploadData(new float[] { 0, 0, 0 });
        priorElements.UploadIndices(new uint[] { 0 });
        priorVao.BindElementBuffer(priorElements);
        StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, priorArray.BufferId);
        using var geometry = new ArrayGraphicsGeometry(new([]), PrimitiveType.Triangles, new Dictionary<int, GpuVbo>(), 3);
        Assert.Equal(priorVao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
        Assert.Equal(priorArray.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
        Assert.Equal(priorElements.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
        geometry.Dispose();
        Assert.True(GL.IsVertexArray(priorVao.VertexArrayId));
        Assert.True(GL.IsBuffer(priorArray.BufferId));
        Assert.True(GL.IsBuffer(priorElements.BufferId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
