using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies real graphics owner capture guards independently of native program selection and execution.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class RHIProgramCaptureTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Capture does not select a shader, and rejects mutations, retirement and preparation while sampling authored inputs.</summary>
    [Fact]
    public void CaptureIsPureAndGuardsAuthoredCallbacks()
    {
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<GuardedShader>();
        OpenGLProgramSelection.Select(StateCache.Current,0);int before=GL.GetInteger(GetPName.CurrentProgram);
        shader.Value=7;var table=shader.CaptureBindings();Assert.Equal(shader.Shader,table.Shader);
        Assert.Equal(7,shader.Value);Assert.Equal(before,GL.GetInteger(GetPName.CurrentProgram));
        shader.Value=9;Assert.Equal(9,shader.Value);Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>A throwing authored getter cannot leave later input edits or captures permanently locked.</summary>
    [Fact]
    public void FailedCaptureReleasesItsMutationGuard()
    {
        fixture.MakeCurrent();using var programs=new ComponentShaderPrograms();var shader=programs.Create<GuardedShader>();
        shader.Fail=true;Assert.Throws<ArgumentException>(()=>shader.CaptureBindings());shader.Value=11;
        shader.Fail=false;Assert.NotNull(shader.CaptureBindings());Assert.Equal(11,shader.Value);
        shader.Dispose();Assert.Throws<ObjectDisposedException>(()=>shader.CaptureBindings());
    }
    #endregion

    #region Private
    /// <summary>Supplies authored input sampling for the packaged generated-accessor executable.</summary>
    private sealed class GuardedShader:VanillaGraphicsExpanded.Rendering.Shaders.GpuProgram
    {
        private int value;
        /// <summary>Uses the executable fixture's actual schema and identity rather than generator-only accessor metadata.</summary>
        internal override VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => RenderInfrastructureShaderProgram.Contract;
        /// <summary>Creates the real executable-backed authored capture fixture.</summary>
        public GuardedShader() { }
        /// <summary>Retains a value through the same asset mutation guard as generated setters.</summary>
        internal int Value{get=>value;set{RequireInputMutation();this.value=value;}}
        internal bool Fail;
        /// <summary>Checks capture admission and returns only the ready fixed executable identity.</summary>
        protected override RHIResourceBindings CaptureBindingsCore()
        {
            Assert.Throws<InvalidOperationException>(()=>Value=23);
            Assert.Throws<InvalidOperationException>(RequireOutsideSubmission);
            Assert.Throws<InvalidOperationException>(()=>CompileAndLink());
            Assert.Throws<InvalidOperationException>(()=>Dispose());
            if(Fail)throw new ArgumentException("Controlled capture failure.");
            return new(Shader);
        }
    }
    #endregion
}
