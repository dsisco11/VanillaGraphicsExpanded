using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Verifies VAO creation and ownership transitions against native OpenGL objects.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuVaoIntegrationTests
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates the fixture-backed VAO integration test instance.</summary>
    public GpuVaoIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    #region Creation Tests

    /// <summary>Creates a valid VAO with the default parameters.</summary>
    [Fact]
    public void Create_WithDefaultParameters_CreatesValidVao()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao = NativeResourceSetup.CreateVertexArray();

        // Assert
        Assert.True(vao.IsValid);
        Assert.True(vao.VertexArrayId > 0);
        Assert.Null(vao.DebugName);
        Assert.False(vao.IsDisposed);
    }

    /// <summary>Creates a valid VAO with the requested debug name.</summary>
    [Fact]
    public void Create_WithDebugName_SetsDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        const string debugName = "TestVAO";

        // Act
        using var vao = NativeResourceSetup.CreateVertexArray(debugName);

        // Assert
        Assert.True(vao.IsValid);
        Assert.Equal(debugName, vao.DebugName);
        Assert.True(vao.VertexArrayId > 0);
    }

    /// <summary>Assigns distinct native names to independently created vertex arrays.</summary>
    [Fact]
    public void Create_GeneratesUniqueVertexArrayIds()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao1 = NativeResourceSetup.CreateVertexArray();
        using var vao2 = NativeResourceSetup.CreateVertexArray();

        // Assert
        Assert.NotEqual(vao1.VertexArrayId, vao2.VertexArrayId);
        Assert.True(vao1.VertexArrayId > 0);
        Assert.True(vao2.VertexArrayId > 0);
    }

    /// <summary>Preserves an empty debug name on a valid VAO.</summary>
    [Fact]
    public void Create_EmptyDebugName_AcceptsEmptyString()
    {
        // Arrange
        _fixture.EnsureContextValid();

        // Act
        using var vao = NativeResourceSetup.CreateVertexArray("");

        // Assert
        Assert.Equal("", vao.DebugName);
        Assert.True(vao.IsValid);
    }

    #endregion

    #region Resource Management Tests

    /// <summary>Transfers the native vertex-array name and invalidates the VAO wrapper.</summary>
    [Fact]
    public void Detach_ValidVao_ReturnsVertexArrayIdAndInvalidatesVao()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = NativeResourceSetup.CreateVertexArray();
        int originalId = vao.VertexArrayId;

        int detachedId = 0;
        try
        {
            // Act
            detachedId = (int)vao.Detach();

            // Assert
            Assert.Equal(originalId, detachedId);
            Assert.Equal(0, vao.VertexArrayId);
            Assert.True(vao.IsDisposed);
            Assert.False(vao.IsValid);
        }
        finally
        {
            // Detach transfers native ownership, so clean up the raw name even if an assertion fails.
            if (detachedId != 0) GL.DeleteVertexArray(detachedId);
        }
    }

    /// <summary>Returns the same raw vertex-array name as detach and leaves the wrapper disposed.</summary>
    [Fact]
    public void ReleaseHandle_ValidVao_ReturnsSameAsDetach()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao1 = NativeResourceSetup.CreateVertexArray();
        var vao2 = NativeResourceSetup.CreateVertexArray();
        int id1 = vao1.VertexArrayId;
        int id2 = vao2.VertexArrayId;

        int detachResult = 0;
        int releaseResult = 0;
        try
        {
            // Act
            detachResult = (int)vao1.Detach();
            releaseResult = (int)vao2.ReleaseHandle();

            // Assert
            Assert.Equal(id1, detachResult);
            Assert.Equal(id2, releaseResult);
            Assert.True(vao1.IsDisposed);
            Assert.True(vao2.IsDisposed);
        }
        finally
        {
            // Each transfer is independent; clean up whichever raw names were already acquired.
            if (detachResult != 0) GL.DeleteVertexArray(detachResult);
            if (releaseResult != 0) GL.DeleteVertexArray(releaseResult);
        }
    }


    /// <summary>Deletes the native vertex array and invalidates its wrapper.</summary>
[Fact]
    public void Dispose_ValidVao_DeletesVertexArrayAndInvalidatesVao()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = NativeResourceSetup.CreateVertexArray();

        // Act
        vao.Dispose();

        // Assert
        Assert.Equal(0, vao.VertexArrayId);
        Assert.True(vao.IsDisposed);
        Assert.False(vao.IsValid);
    }

    /// <summary>Allows repeated disposal of a VAO without changing its disposed state.</summary>
    [Fact]
    public void MultipleDispose_ValidVao_IsSafe()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = NativeResourceSetup.CreateVertexArray();

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

    /// <summary>Preserves the full long debug name on the created VAO.</summary>
    [Fact]
    public void Create_LongDebugName_AcceptsLongNames()
    {
        // Arrange
        _fixture.EnsureContextValid();
        string longName = new string('A', 1000);

        // Act
        using var vao = NativeResourceSetup.CreateVertexArray(longName);

        // Assert
        Assert.Equal(longName, vao.DebugName);
        Assert.True(vao.IsValid);
    }

    #endregion
    #endregion
}
