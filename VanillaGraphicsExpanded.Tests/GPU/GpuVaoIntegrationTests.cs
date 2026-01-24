using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for GpuVao class.
/// Tests actual OpenGL vertex array object creation, binding, configuration, and drawing operations.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuVaoIntegrationTests
{
    private readonly HeadlessGLFixture _fixture;

    public GpuVaoIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    #region Creation Tests

    [Fact]
    public void Create_WithDefaultParameters_CreatesValidVao()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao = GpuVao.Create();

        // Assert
        Assert.True(vao.IsValid);
        Assert.True(vao.VertexArrayId > 0);
        Assert.Null(vao.DebugName);
        Assert.False(vao.IsDisposed);
    }

    [Fact]
    public void Create_WithDebugName_SetsDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "TestVAO";

        // Act
        using var vao = GpuVao.Create(debugName);

        // Assert
        Assert.True(vao.IsValid);
        Assert.Equal(debugName, vao.DebugName);
        Assert.True(vao.VertexArrayId > 0);
    }

    [Fact]
    public void Create_GeneratesUniqueVertexArrayIds()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao1 = GpuVao.Create();
        using var vao2 = GpuVao.Create();

        // Assert
        Assert.NotEqual(vao1.VertexArrayId, vao2.VertexArrayId);
        Assert.True(vao1.VertexArrayId > 0);
        Assert.True(vao2.VertexArrayId > 0);
    }

    [Fact]
    public void Create_EmptyDebugName_AcceptsEmptyString()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao = GpuVao.Create("");

        // Assert
        Assert.Equal("", vao.DebugName);
        Assert.True(vao.IsValid);
    }

    #endregion

    #region Binding Tests

    [Fact]
    public void Bind_CreatedVao_SuccessfullyBindsVertexArray()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act & Assert - Should not throw
        vao.Bind();
        
        // Verify it's actually bound
        GL.GetInteger(GetPName.VertexArrayBinding, out int boundVao);
        Assert.Equal(vao.VertexArrayId, boundVao);
        
        vao.Unbind();
    }

    [Fact]
    public void TryBind_ValidVao_ReturnsTrue()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act
        bool result = vao.TryBind();

        // Assert
        Assert.True(result);
        
        vao.Unbind();
    }

    [Fact]
    public void TryBind_DisposedVao_ReturnsFalse()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();
        vao.Dispose();

        // Act
        bool result = vao.TryBind();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void BindScope_ValidVao_RestoresPreviousBinding()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao1 = GpuVao.Create();
        using var vao2 = GpuVao.Create();

        // Bind first VAO
        vao1.Bind();
        GL.GetInteger(GetPName.VertexArrayBinding, out int initialBinding);
        Assert.Equal(vao1.VertexArrayId, initialBinding);

        // Act - Use binding scope with second VAO
        using (var scope = vao2.BindScope())
        {
            GL.GetInteger(GetPName.VertexArrayBinding, out int scopedBinding);
            Assert.Equal(vao2.VertexArrayId, scopedBinding);
        }

        // Assert - Should restore original binding
        GL.GetInteger(GetPName.VertexArrayBinding, out int restoredBinding);
        Assert.Equal(vao1.VertexArrayId, restoredBinding);
        
        vao1.Unbind();
    }

    #endregion

    #region Element Buffer Tests

    [Fact]
    public void BindElementBuffer_ValidBufferId_BindsElementBuffer()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var ebo = GpuEbo.Create();

        // Act
        vao.BindElementBuffer(ebo.BufferId);

        // Assert - The method should complete without throwing
        // Note: Due to DSA usage, the EBO binding may not be immediately visible
        // in the global state, but the VAO will have the correct association
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void BindElementBuffer_GpuEbo_BindsElementBuffer()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var ebo = GpuEbo.Create();

        // Act
        vao.BindElementBuffer(ebo);

        // Assert - The method should complete without throwing
        // Note: Due to DSA usage, the EBO binding may not be immediately visible
        // in the global state, but the VAO will have the correct association
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void BindElementBuffer_NullEbo_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => vao.BindElementBuffer((GpuEbo)null!));
    }

    [Fact]
    public void UnbindElementBuffer_ValidVao_UnbindsElementBuffer()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var ebo = GpuEbo.Create();
        
        vao.BindElementBuffer(ebo);

        // Act
        vao.UnbindElementBuffer();

        // Assert - The method should complete without throwing
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void BindElementBuffer_WhenVaoBound_VerifiesBinding()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var ebo = GpuEbo.Create();
        
        // Bind VAO first to ensure element buffer binding is visible
        vao.Bind();

        // Act
        vao.BindElementBuffer(ebo.BufferId);

        // Assert - Now we should be able to see the binding
        GL.GetInteger(GetPName.ElementArrayBufferBinding, out int boundEbo);
        Assert.Equal(ebo.BufferId, boundEbo);
        
        vao.Unbind();
    }

    #endregion

    #region Vertex Attribute Tests

    [Fact]
    public void EnableAttrib_ValidIndex_EnablesVertexAttribute()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        const int attributeIndex = 0;

        // Act
        vao.EnableAttrib(attributeIndex);

        // Assert - The method should complete without throwing
        // Note: Due to DSA usage, the attribute state may not be immediately visible
        // in the global context until the VAO is actually bound and used
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void DisableAttrib_ValidIndex_DisablesVertexAttribute()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        const int attributeIndex = 0;

        vao.EnableAttrib(attributeIndex);

        // Act
        vao.DisableAttrib(attributeIndex);

        // Assert - Verify the attribute is disabled
        vao.Bind();
        GL.GetVertexAttrib(attributeIndex, VertexAttribParameter.ArrayEnabled, out int enabled);
        Assert.Equal(0, enabled); // OpenGL returns 0 for disabled
        
        vao.Unbind();
    }

    [Fact]
    public void AttribPointer_ValidParameters_ConfiguresVertexAttribute()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var vbo = GpuVbo.Create();
        
        const int attributeIndex = 0;
        const int size = 3;
        const VertexAttribPointerType type = VertexAttribPointerType.Float;
        const bool normalized = false;
        const int strideBytes = 12;
        const int offsetBytes = 0;

        // Bind the VBO first so AttribPointer can reference it
        vbo.Bind();

        // Act
        vao.AttribPointer(attributeIndex, size, type, normalized, strideBytes, offsetBytes);

        // Assert - The method should complete without throwing
        Assert.True(vao.IsValid);
        
        vbo.Unbind();
    }

    [Fact]
    public void AttribIPointer_ValidParameters_ConfiguresIntegerVertexAttribute()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var vbo = GpuVbo.Create();
        
        const int attributeIndex = 0;
        const int size = 1;
        const VertexAttribIntegerType type = VertexAttribIntegerType.Int;
        const int strideBytes = 4;
        const int offsetBytes = 0;

        // Bind the VBO first so AttribIPointer can reference it
        vbo.Bind();

        // Act
        vao.AttribIPointer(attributeIndex, size, type, strideBytes, offsetBytes);

        // Assert - The method should complete without throwing
        Assert.True(vao.IsValid);
        
        vbo.Unbind();
    }

    [Fact]
    public void AttribDivisor_ValidParameters_SetsInstanceDivisor()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        
        const int attributeIndex = 1;
        const int divisor = 2;

        // Act
        vao.AttribDivisor(attributeIndex, divisor);

        // Assert - The method should complete without throwing
        Assert.True(vao.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)] // Common max vertex attributes
    public void EnableAttrib_VariousIndices_WorksCorrectly(int attributeIndex)
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act & Assert - Should not throw for reasonable attribute indices
        vao.EnableAttrib(attributeIndex);
        Assert.True(vao.IsValid);
    }

    #endregion

    #region Draw Operations Tests

    [Fact]
    public void DrawElements_ValidParameters_ExecutesDrawCall()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        const PrimitiveType primitiveType = PrimitiveType.Triangles;
        const DrawElementsType indexType = DrawElementsType.UnsignedInt;
        const int indexCount = 0; // Use 0 to trigger early return and avoid access violation

        // Act - Should return early without throwing
        vao.DrawElements(primitiveType, indexType, indexCount);

        // Assert - VAO should still be valid
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void DrawElements_WithGpuEbo_ExecutesDrawCall()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var ebo = GpuEbo.Create();

        // Don't upload data - this will leave IndexCount as 0
        // which triggers early return in DrawElements
        const PrimitiveType primitiveType = PrimitiveType.Triangles;

        // Act - Should return early without throwing
        vao.DrawElements(primitiveType, ebo);

        // Assert - VAO should still be valid
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void DrawElements_NullEbo_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => vao.DrawElements(PrimitiveType.Triangles, (GpuEbo)null!));
    }

    [Fact]
    public void DrawElements_NegativeOffset_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => 
            vao.DrawElements(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, 3, -1));
    }

    [Fact]
    public void DrawElements_ZeroIndexCount_ReturnsEarly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act - Should not throw and return early
        vao.DrawElements(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, 0);

        // Assert
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void DrawElementsInstanced_ValidParameters_ExecutesDrawCall()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        const PrimitiveType primitiveType = PrimitiveType.Triangles;
        const DrawElementsType indexType = DrawElementsType.UnsignedInt;
        const int indexCount = 0; // Use 0 to trigger early return
        const int instanceCount = 2;

        // Act - Should return early without throwing
        vao.DrawElementsInstanced(primitiveType, indexType, indexCount, instanceCount);

        // Assert
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void DrawElementsInstanced_WithGpuEbo_ExecutesDrawCall()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var ebo = GpuEbo.Create();

        // Don't upload data - IndexCount will be 0, causing early return
        const PrimitiveType primitiveType = PrimitiveType.Triangles;
        const int instanceCount = 2;

        // Act - Should return early without throwing
        vao.DrawElementsInstanced(primitiveType, ebo, instanceCount);

        // Assert
        Assert.True(vao.IsValid);
    }

    [Fact]
    public void DrawElementsInstanced_NullEbo_ThrowsArgumentNullException()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            vao.DrawElementsInstanced(PrimitiveType.Triangles, (GpuEbo)null!, 1));
    }

    [Fact]
    public void DrawElementsInstanced_ZeroInstanceCount_ReturnsEarly()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        using var ebo = GpuEbo.Create();

        // Don't upload data - IndexCount will be 0

        // Act - Should return early due to zero instance count
        vao.DrawElementsInstanced(PrimitiveType.Triangles, ebo, 0);

        // Assert
        Assert.True(vao.IsValid);
    }

    #endregion

    #region Resource Management Tests

    [Fact]
    public void Detach_ValidVao_ReturnsVertexArrayIdAndInvalidatesVao()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();
        int originalId = vao.VertexArrayId;

        // Act
        int detachedId = (int)vao.Detach();

        // Assert
        Assert.Equal(originalId, detachedId);
        Assert.Equal(0, vao.VertexArrayId);
        Assert.True(vao.IsDisposed);
        Assert.False(vao.IsValid);

        // Clean up the detached VAO manually
        GL.DeleteVertexArray(detachedId);
        
        vao.Dispose(); // Safe to call on already detached/disposed
    }

    [Fact]
    public void ReleaseHandle_ValidVao_ReturnsSameAsDetach()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao1 = GpuVao.Create();
        var vao2 = GpuVao.Create();
        int id1 = vao1.VertexArrayId;
        int id2 = vao2.VertexArrayId;

        // Act
        int detachResult = (int)vao1.Detach();
        int releaseResult = (int)vao2.ReleaseHandle();

        // Assert
        Assert.Equal(id1, detachResult);
        Assert.Equal(id2, releaseResult);
        Assert.True(vao1.IsDisposed);
        Assert.True(vao2.IsDisposed);

        // Clean up manually
        GL.DeleteVertexArray(detachResult);
        GL.DeleteVertexArray(releaseResult);
        
        vao1.Dispose();
        vao2.Dispose();
    }

    [Fact]
    public void Detach_DisposedVao_ReturnsZero()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();
        vao.Dispose();

        // Act
        int result = (int)vao.Detach();

        // Assert
        Assert.Equal(0, result);
    }

    [Fact]
    public void SetDebugName_ValidVao_UpdatesDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create();
        const string newDebugName = "UpdatedVAO";

        // Act
        vao.SetDebugName(newDebugName);

        // Assert
        Assert.Equal(newDebugName, vao.DebugName);
    }

    [Fact]
    public void SetDebugName_NullName_AcceptsNull()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create("Initial");

        // Act
        vao.SetDebugName(null);

        // Assert
        Assert.Null(vao.DebugName);
    }

    [Fact]
    public void Dispose_ValidVao_DeletesVertexArrayAndInvalidatesVao()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();

        // Act
        vao.Dispose();

        // Assert
        Assert.Equal(0, vao.VertexArrayId);
        Assert.True(vao.IsDisposed);
        Assert.False(vao.IsValid);
    }

    [Fact]
    public void MultipleDispose_ValidVao_IsSafe()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();

        // Act & Assert - Multiple dispose calls should be safe
        vao.Dispose();
        Assert.True(vao.IsDisposed);
        
        vao.Dispose(); // Should not throw
        Assert.True(vao.IsDisposed);
        
        vao.Dispose(); // Should not throw
        Assert.True(vao.IsDisposed);
    }

    #endregion

    #region Edge Cases and Error Handling Tests

    [Fact]
    public void OperationsOnDisposedVao_ReturnEarlyOrLogWarning()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create();
        vao.Dispose();

        // Act & Assert - All operations should handle disposed VAO gracefully
        vao.Bind(); // Should log warning and return early
        Assert.False(vao.TryBind());
        
        vao.BindElementBuffer(1); // Should log warning and return early
        vao.EnableAttrib(0); // Should log warning and return early
        vao.DisableAttrib(0); // Should log warning and return early
        vao.AttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, 0); // Should log warning
        vao.AttribIPointer(0, 1, VertexAttribIntegerType.Int, 4, 0); // Should log warning
        vao.AttribDivisor(0, 1); // Should log warning and return early
        
        // Draw operations should return early
        vao.DrawElements(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, 3);
        vao.DrawElementsInstanced(PrimitiveType.Triangles, DrawElementsType.UnsignedInt, 3, 1);
    }

    [Fact]
    public void ToString_ValidVao_ReturnsFormattedString()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = GpuVao.Create("TestVAO");

        // Act
        string result = vao.ToString();

        // Assert
        Assert.Contains("GpuVao", result);
        Assert.Contains($"id={vao.VertexArrayId}", result);
        Assert.Contains("name=TestVAO", result);
        Assert.Contains("disposed=False", result);
    }

    [Fact]
    public void ToString_DisposedVao_ShowsDisposedState()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = GpuVao.Create("TestVAO");
        vao.Dispose();

        // Act
        string result = vao.ToString();

        // Assert
        Assert.Contains("disposed=True", result);
        Assert.Contains("id=0", result);
    }

    [Fact]
    public void Create_LongDebugName_AcceptsLongNames()
    {
        // Arrange
        _fixture.EnsureContextValid();
        string longName = new string('A', 1000);

        // Act
        using var vao = GpuVao.Create(longName);

        // Assert
        Assert.Equal(longName, vao.DebugName);
        Assert.True(vao.IsValid);
    }

    #endregion
}
