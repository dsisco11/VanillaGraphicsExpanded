using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks shared ARB shader/program deletion against original canonical resource kinds.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeShaderDeletionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Immediate destruction cannot erase the kind needed to retire an existing shader association.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArbDeletionRetiresOriginalShaderKind(bool attached)
    {
        // Arrange: Create an ARB shader/program association and retain the canonical resource kind.
        // Act: Delete the shader immediately, optionally while it is attached, then retire its association.
        // Assert: Original kind information remains available until the native object is actually retired.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int shader = GL.CreateShader(ShaderType.VertexShader);
        int program = 0;
        try
        {
            using var owner = new GpuShaderModule(shader, ShaderStage.Vertex);
            var registration = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
            if (attached)
            {
                program = GL.CreateProgram();
                GL.AttachShader(program, shader);
            }
            GL.Arb.DeleteObject(shader);
            // The attachment keeps the physical object alive, but both outcomes retire its association.
            Assert.Equal(attached, GL.IsShader(shader));
            Assert.Equal(GpuResourceRegistrationState.Deleted, registration.State);
            Assert.False(owner.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (program != 0) GL.DeleteProgram(program);
            if (GL.IsShader(shader)) GL.DeleteShader(shader);
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
