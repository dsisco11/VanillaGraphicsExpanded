using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Verifies authored input capture releases mutation guards after a controlled failure.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIProgramCaptureTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A throwing authored getter cannot lock later input edits or capture attempts.</summary>
    [Fact]
    public void FailedCaptureReleasesItsMutationGuard()
    {
        // Arrange: create an executable-backed input owner and request one controlled capture failure.
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<GuardedShader>();
        shader.Fail = true;

        // Act: observe the controlled failure, then restore normal capture and input mutation.
        Assert.Throws<ArgumentException>(() => shader.CaptureBindings());
        shader.Value = 11;
        shader.Fail = false;
        var table = shader.CaptureBindings();

        // Assert: later edits and captures succeed after the failing authored callback.
        Assert.NotNull(table);
        Assert.Equal(11, shader.Value);

        // Act: retire the owner after confirming the mutation guard recovered.
        shader.Dispose();

        // Assert: disposed owners cannot be captured.
        Assert.Throws<ObjectDisposedException>(() => shader.CaptureBindings());
    }
    #endregion

    #region Private
    /// <summary>Supplies authored input sampling for the packaged generated-accessor executable.</summary>
    private sealed class GuardedShader : VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        #region Public API
        /// <summary>Uses the executable fixture's actual schema and identity rather than metadata-only accessors.</summary>
        internal override VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => RenderInfrastructureShaderProgram.Contract;

        /// <summary>Creates the real executable-backed authored capture fixture.</summary>
        public GuardedShader() { }

        /// <summary>Retains a value through the same asset mutation guard as generated setters.</summary>
        internal int Value
        {
            get => value;
            set
            {
                RequireInputMutation();
                this.value = value;
            }
        }

        internal bool Fail;
        #endregion

        #region Protected
        /// <summary>Checks capture admission and returns only the ready fixed executable identity.</summary>
        protected override RHIResourceBindings CaptureBindingsCore()
        {
            Assert.Throws<InvalidOperationException>(() => Value = 23);
            Assert.Throws<InvalidOperationException>(RequireOutsideSubmission);
            Assert.Throws<InvalidOperationException>(() => CompileAndLink());
            Assert.Throws<InvalidOperationException>(() => Dispose());
            if (Fail)
                throw new ArgumentException("Controlled capture failure.");
            return new(Shader);
        }
        #endregion

        #region Private
        private int value;
        #endregion
    }
    #endregion
}
