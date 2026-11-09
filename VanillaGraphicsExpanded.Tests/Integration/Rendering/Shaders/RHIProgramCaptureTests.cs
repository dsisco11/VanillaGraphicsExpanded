using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks executable-backed capture preserves native selection and authored input state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIProgramCaptureTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Capture leaves the selected native program and authored values unchanged.</summary>
    [Fact]
    public void CaptureIsPureAndGuardsAuthoredCallbacks()
    {
        // Arrange: select a native program and create an executable-backed authored input owner.
        fixture.MakeCurrent();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<GuardedShader>();
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
        int before = GL.GetInteger(GetPName.CurrentProgram);

        // Act: mutate and capture the authored bindings, then make another legal input edit.
        shader.Value = 7;
        var table = shader.CaptureBindings();

        // Assert: capture returns the owner and does not select a shader or consume native errors.
        Assert.Equal(shader.Shader, table.Shader);
        Assert.Equal(7, shader.Value);
        Assert.Equal(before, GL.GetInteger(GetPName.CurrentProgram));

        // Act: verify a later legal edit remains available after capture completes.
        shader.Value = 9;

        // Assert: the edit is retained and capture did not leave a native error pending.
        Assert.Equal(9, shader.Value);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
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
