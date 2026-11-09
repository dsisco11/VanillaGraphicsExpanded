using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Xunit;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies VAO disposal, detachment, and debug-name managed rules.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuVaoManagedRulesTests
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates the GPU fixture-backed VAO managed-rules test instance.</summary>
    public GpuVaoManagedRulesTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }


    #region Resource Management Tests


    /// <summary>Returns zero when detaching a previously disposed VAO.</summary>
[Fact]
    public void Detach_DisposedVao_ReturnsZero()
    {
        // Arrange
        _fixture.EnsureContextValid();
        var vao = NativeResourceSetup.CreateVertexArray();
        vao.Dispose();

        // Act
        int result = (int)vao.Detach();

        // Assert
        Assert.Equal(0, result);
    }

    /// <summary>Updates the VAO debug name through its managed setter.</summary>
    [Fact]
    public void SetDebugName_ValidVao_UpdatesDebugName()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = NativeResourceSetup.CreateVertexArray();
        const string newDebugName = "UpdatedVAO";

        // Act
        vao.SetDebugName(newDebugName);

        // Assert
        Assert.Equal(newDebugName, vao.DebugName);
    }

    /// <summary>Clears the VAO debug name when the setter receives null.</summary>
    [Fact]
    public void SetDebugName_NullName_AcceptsNull()
    {
        // Arrange
        _fixture.EnsureContextValid();
        using var vao = NativeResourceSetup.CreateVertexArray("Initial");

        // Act
        vao.SetDebugName(null);

        // Assert
        Assert.Null(vao.DebugName);
    }


#endregion

    #endregion
}
